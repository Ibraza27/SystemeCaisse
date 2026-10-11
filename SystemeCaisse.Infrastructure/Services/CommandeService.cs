using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    /// <summary>Critères de recherche des commandes (filtres appliqués côté base quand c'est possible).</summary>
    public sealed class CommandeFilter
    {
        /// <summary>"en_attente", "traitee", "annulee" ou null pour tous.</summary>
        public string? Statut { get; set; }
        public DateTime? DateDebut { get; set; }
        public DateTime? DateFin { get; set; }
        public string? Recherche { get; set; }
        /// <summary>Nombre maximum de commandes renvoyées (les plus récentes).</summary>
        public int Max { get; set; } = 500;
    }

    /// <summary>
    /// Toutes les écritures sur les commandes : numérotation unique, changements de statut
    /// avec décrément de stock atomique, paiements, suppression, et réception SignalR.
    /// </summary>
    public sealed class CommandeService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public CommandeService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        // ───────────────────────── Lecture ─────────────────────────

        public async Task<List<Commande>> SearchAsync(CommandeFilter filter, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            IQueryable<Commande> query = db.Commandes.AsNoTracking().Include(c => c.Lignes);

            if (!string.IsNullOrEmpty(filter.Statut))
                query = query.Where(c => c.Statut == filter.Statut);

            if (filter.DateDebut.HasValue)
            {
                var start = filter.DateDebut.Value.Date;
                query = query.Where(c => c.CreatedAt >= start);
            }
            if (filter.DateFin.HasValue)
            {
                var endExclusive = filter.DateFin.Value.Date.AddDays(1);
                query = query.Where(c => c.CreatedAt < endExclusive);
            }

            if (!string.IsNullOrWhiteSpace(filter.Recherche))
            {
                var q = filter.Recherche.Trim().ToLower();
                query = query.Where(c =>
                    c.NumeroCommande.ToLower().Contains(q) ||
                    c.Nom.ToLower().Contains(q) ||
                    c.Prenom.ToLower().Contains(q) ||
                    c.Telephone.Contains(q));
            }

            return await query
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .Take(Math.Max(1, filter.Max))
                .AsSplitQuery()
                .ToListAsync(ct);
        }

        // ───────────────────────── Création / modification ─────────────────────────

        /// <summary>
        /// Crée la commande avec un numéro CMD-yyyyMMdd-NNN unique (index unique + nouvelle tentative
        /// si deux postes prennent le même numéro au même instant).
        /// </summary>
        public async Task<Commande> CreateAsync(Commande commande, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(commande.SyncId)) commande.SyncId = Guid.NewGuid().ToString();
            var datePart = commande.CreatedAt.ToString("yyyyMMdd");
            var prefix = $"CMD-{datePart}-";

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await using var db = await _factory.CreateDbContextAsync(ct);
                    var strategy = db.Database.CreateExecutionStrategy();
                    return await strategy.ExecuteAsync(async token =>
                    {
                        await using var ctx = await _factory.CreateDbContextAsync(token);
                        await using var tx = await ctx.Database.BeginTransactionAsync(token);

                        var numbers = await ctx.Commandes.AsNoTracking()
                            .Where(c => c.NumeroCommande.StartsWith(prefix))
                            .Select(c => c.NumeroCommande)
                            .ToListAsync(token);
                        int next = numbers
                            .Select(n => int.TryParse(n.Substring(prefix.Length).Split('-')[0], out var x) ? x : 0)
                            .DefaultIfEmpty(0).Max() + 1;

                        var entity = CloneCommande(commande);
                        entity.NumeroCommande = $"{prefix}{next:D3}";
                        ctx.Commandes.Add(entity);
                        await ctx.SaveChangesAsync(token);
                        await DataVersionBump.BumpAsync(ctx, token, DataDomains.Commandes);
                        await tx.CommitAsync(token);
                        return entity;
                    }, ct);
                }
                catch (DbUpdateException ex) when (SalesService.IsUniqueViolation(ex) && attempt < 5)
                {
                    AppLog.Info("Commandes", $"Numéro de commande déjà pris, nouvelle tentative ({attempt}).");
                    await Task.Delay(50 * attempt, ct);
                }
            }
        }

        /// <summary>Remplace les informations et les lignes d'une commande existante.</summary>
        public async Task<Commande?> UpdateAsync(int id, Action<Commande> applyFields, IEnumerable<LigneCommande> newLines, CancellationToken ct = default)
        {
            var lines = newLines.ToList();
            await using var db0 = await _factory.CreateDbContextAsync(ct);
            return await db0.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var db = await _factory.CreateDbContextAsync(token);
                await using var tx = await db.Database.BeginTransactionAsync(token);
                var existing = await db.Commandes.Include(c => c.Lignes).FirstOrDefaultAsync(c => c.Id == id, token);
                if (existing == null) return null;

                applyFields(existing);
                existing.UpdatedAt = DateTime.Now;
                db.LignesCommande.RemoveRange(existing.Lignes);
                existing.Lignes = lines.Select(CloneLine).ToList();

                await db.SaveChangesAsync(token);
                await DataVersionBump.BumpAsync(db, token, DataDomains.Commandes);
                await tx.CommitAsync(token);
                return existing;
            }, ct);
        }

        /// <summary>
        /// Change le statut de plusieurs commandes. Le passage en "traitee" décrémente le stock
        /// (UPDATE atomique) et crée les mouvements, dans la même transaction.
        /// </summary>
        public async Task<int> ChangeStatusAsync(IReadOnlyCollection<int> ids, string newStatus, CancellationToken ct = default)
        {
            await using var db0 = await _factory.CreateDbContextAsync(ct);
            return await db0.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var db = await _factory.CreateDbContextAsync(token);
                await using var tx = await db.Database.BeginTransactionAsync(token);

                var commandes = await db.Commandes.Include(c => c.Lignes)
                    .Where(c => ids.Contains(c.Id)).ToListAsync(token);
                bool stockChanged = false;
                var now = DateTime.Now;

                var productIds = commandes.SelectMany(c => c.Lignes).Where(l => l.ProduitId.HasValue)
                    .Select(l => l.ProduitId!.Value).Distinct().ToList();
                var existingProducts = (await db.Produits.AsNoTracking().Where(p => productIds.Contains(p.Id))
                    .Select(p => p.Id).ToListAsync(token)).ToHashSet();

                var deltas = new Dictionary<int, decimal>();
                foreach (var cmd in commandes)
                {
                    if (newStatus == "traitee" && cmd.Statut != "traitee")
                    {
                        foreach (var ligne in cmd.Lignes.Where(l => l.ProduitId.HasValue && existingProducts.Contains(l.ProduitId.Value)))
                        {
                            deltas[ligne.ProduitId!.Value] = deltas.GetValueOrDefault(ligne.ProduitId.Value) - ligne.Quantite;
                            db.MouvementsStock.Add(new MouvementStock
                            {
                                ProduitId = ligne.ProduitId.Value,
                                TypeMouvement = "sortie",
                                Quantite = ligne.Quantite,
                                PrixUnitaire = ligne.PrixUnitaire,
                                DateMouvement = now,
                                Commentaire = $"Commande {cmd.NumeroCommande}",
                                ProduitNomSnapshot = ligne.ProduitNom
                            });
                            stockChanged = true;
                        }
                    }
                    cmd.Statut = newStatus;
                    cmd.UpdatedAt = now;
                }

                await db.SaveChangesAsync(token);
                foreach (var (pid, delta) in deltas)
                    await StockOperations.AdjustStockAsync(db, pid, delta, token);

                if (stockChanged)
                    await DataVersionBump.BumpAsync(db, token, DataDomains.Commandes, DataDomains.Stocks);
                else
                    await DataVersionBump.BumpAsync(db, token, DataDomains.Commandes);

                await tx.CommitAsync(token);
                return commandes.Count;
            }, ct);
        }

        public async Task<int> DeleteAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
        {
            await using var db0 = await _factory.CreateDbContextAsync(ct);
            return await db0.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var db = await _factory.CreateDbContextAsync(token);
                await using var tx = await db.Database.BeginTransactionAsync(token);
                await db.LignesCommande.Where(l => ids.Contains(l.CommandeId)).ExecuteDeleteAsync(token);
                int n = await db.Commandes.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync(token);
                await DataVersionBump.BumpAsync(db, token, DataDomains.Commandes);
                await tx.CommitAsync(token);
                return n;
            }, ct);
        }

        /// <summary>Ajoute un paiement (addition faite par la base : pas d'écrasement entre postes).</summary>
        public async Task<Commande?> AddPaymentAsync(int id, decimal montant, string? modePaiement, CancellationToken ct = default)
        {
            await using var db0 = await _factory.CreateDbContextAsync(ct);
            await db0.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var db = await _factory.CreateDbContextAsync(token);
                await using var tx = await db.Database.BeginTransactionAsync(token);
                await AddToMontantPayeAsync(db, id, montant, modePaiement, token);
                await DataVersionBump.BumpAsync(db, token, DataDomains.Commandes);
                await tx.CommitAsync(token);
            }, ct);

            await using var read = await _factory.CreateDbContextAsync(ct);
            return await read.Commandes.AsNoTracking().Include(c => c.Lignes).FirstOrDefaultAsync(c => c.Id == id, ct);
        }

        private static async Task AddToMontantPayeAsync(AppDbContext db, int id, decimal montant, string? mode, CancellationToken ct)
        {
            var now = DateTime.Now;
            if (db.IsPostgres)
            {
                if (mode != null)
                    await db.Commandes.Where(c => c.Id == id).ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.MontantPaye, c => c.MontantPaye + montant)
                        .SetProperty(c => c.ModePaiement, mode)
                        .SetProperty(c => c.UpdatedAt, now), ct);
                else
                    await db.Commandes.Where(c => c.Id == id).ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.MontantPaye, c => c.MontantPaye + montant)
                        .SetProperty(c => c.UpdatedAt, now), ct);
            }
            else
            {
                if (mode != null)
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE \"Commandes\" SET \"MontantPaye\" = ROUND(CAST(\"MontantPaye\" AS REAL) + {(double)montant}, 2), \"ModePaiement\" = {mode}, \"UpdatedAt\" = {now} WHERE \"Id\" = {id}", ct);
                else
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE \"Commandes\" SET \"MontantPaye\" = ROUND(CAST(\"MontantPaye\" AS REAL) + {(double)montant}, 2), \"UpdatedAt\" = {now} WHERE \"Id\" = {id}", ct);
            }
        }

        // ───────────────────────── Réception SignalR (TransitManager) ─────────────────────────

        /// <summary>Insère une commande reçue du web si elle n'existe pas déjà (idempotent par SyncId).</summary>
        public async Task<bool> UpsertFromSyncAsync(OrderSyncDto dto, CancellationToken ct = default)
        {
            var syncId = dto.SyncId.ToString();
            await using var db = await _factory.CreateDbContextAsync(ct);
            if (await db.Commandes.AnyAsync(c => c.SyncId == syncId, ct)) return false;

            var productIds = dto.Lignes.Select(l => l.ProduitSourceId).Distinct().ToList();
            var existingProducts = (await db.Produits.AsNoTracking().Where(p => productIds.Contains(p.Id))
                .Select(p => p.Id).ToListAsync(ct)).ToHashSet();

            var commande = new Commande
            {
                NumeroCommande = Truncate(dto.NumeroCommande, 30),
                SyncId = syncId,
                Source = Truncate(dto.Source, 10),
                Nom = Truncate(dto.Nom, 100),
                Prenom = Truncate(dto.Prenom, 100),
                Telephone = Truncate(dto.Telephone, 20),
                Email = TruncateN(dto.Email, 200),
                Ville = TruncateN(dto.Ville, 100),
                CodePostal = TruncateN(dto.CodePostal, 10),
                AvecLivraison = dto.ModeLivraison != "depot",
                Adresse = TruncateN(dto.AdresseLivraison, 200),
                MontantLivraison = dto.MontantLivraison,
                Total = dto.Total,
                TotalRemise = dto.TotalRemise,
                MontantPaye = dto.MontantPaye,
                ModePaiement = TruncateN(dto.ModePaiement, 20),
                Statut = Truncate(dto.Statut, 20),
                NbArticles = dto.Lignes.Sum(l => (int)l.Quantite),
                CreatedAt = dto.CreatedAt.ToLocalTime(),
                UpdatedAt = dto.UpdatedAt.ToLocalTime()
            };
            foreach (var line in dto.Lignes)
            {
                commande.Lignes.Add(new LigneCommande
                {
                    ProduitId = existingProducts.Contains(line.ProduitSourceId) ? line.ProduitSourceId : null,
                    ProduitNom = line.ProduitNom,
                    CategorieNom = line.CategorieNom ?? "Autre",
                    PrixUnitaire = line.PrixUnitaire,
                    Quantite = line.Quantite,
                    TotalLigne = line.TotalLigne,
                    Remise = line.Remise ?? 0,
                    PromotionAppliquee = line.PromotionAppliquee
                });
            }

            try
            {
                db.Commandes.Add(commande);
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (SalesService.IsUniqueViolation(ex))
            {
                // Même SyncId déjà inséré (message reçu deux fois) ou numéro déjà pris.
                db.ChangeTracker.Clear();
                if (await db.Commandes.AnyAsync(c => c.SyncId == syncId, ct)) return false;
                commande.Id = 0;
                foreach (var l in commande.Lignes) { l.Id = 0; l.CommandeId = 0; }
                commande.NumeroCommande = Truncate($"{dto.NumeroCommande}-{DateTime.Now:HHmmss}", 30);
                db.Commandes.Add(commande);
                await db.SaveChangesAsync(ct);
            }
            await DataVersionBump.BumpAsync(db, ct, DataDomains.Commandes);
            return true;
        }

        public async Task<bool> ApplySyncUpdateAsync(OrderSyncDto dto, CancellationToken ct = default)
        {
            var syncId = dto.SyncId.ToString();
            await using var db = await _factory.CreateDbContextAsync(ct);
            var existing = await db.Commandes.FirstOrDefaultAsync(c => c.SyncId == syncId, ct);
            if (existing == null) return false;
            existing.Nom = Truncate(dto.Nom, 100);
            existing.Prenom = Truncate(dto.Prenom, 100);
            existing.Telephone = Truncate(dto.Telephone, 20);
            existing.Statut = Truncate(dto.Statut, 20);
            existing.MontantPaye = dto.MontantPaye;
            existing.Total = dto.Total;
            existing.UpdatedAt = dto.UpdatedAt.ToLocalTime();
            await db.SaveChangesAsync(ct);
            await DataVersionBump.BumpAsync(db, ct, DataDomains.Commandes);
            return true;
        }

        public async Task<bool> DeleteBySyncIdAsync(Guid syncGuid, CancellationToken ct = default)
        {
            var syncId = syncGuid.ToString();
            await using var db = await _factory.CreateDbContextAsync(ct);
            var id = await db.Commandes.Where(c => c.SyncId == syncId).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
            if (id == null) return false;
            await DeleteAsync(new[] { id.Value }, ct);
            return true;
        }

        public async Task<bool> AddSyncPaymentAsync(Guid syncGuid, PaymentSyncDto payment, CancellationToken ct = default)
        {
            var syncId = syncGuid.ToString();
            await using var db = await _factory.CreateDbContextAsync(ct);
            var id = await db.Commandes.Where(c => c.SyncId == syncId).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
            if (id == null) return false;
            await AddPaymentAsync(id.Value, payment.Montant, null, ct);
            return true;
        }

        // ───────────────────────── Outils ─────────────────────────

        public static OrderSyncDto MapToSync(Commande cmd)
        {
            Guid.TryParse(cmd.SyncId, out var syncGuid);
            return new OrderSyncDto
            {
                SyncId = syncGuid != Guid.Empty ? syncGuid : Guid.NewGuid(),
                NumeroCommande = cmd.NumeroCommande,
                Source = cmd.Source ?? "caisse",
                CreatedAt = cmd.CreatedAt,
                UpdatedAt = cmd.UpdatedAt,
                Nom = cmd.Nom,
                Prenom = cmd.Prenom,
                Telephone = cmd.Telephone,
                Email = cmd.Email,
                Ville = cmd.Ville,
                CodePostal = cmd.CodePostal,
                ModeLivraison = cmd.AvecLivraison ? "livraison" : "depot",
                AdresseLivraison = cmd.Adresse,
                MontantLivraison = cmd.MontantLivraison,
                SousTotal = cmd.Total + cmd.TotalRemise,
                TotalRemise = cmd.TotalRemise,
                Total = cmd.Total,
                MontantPaye = cmd.MontantPaye,
                ModePaiement = cmd.ModePaiement,
                Statut = cmd.Statut,
                Lignes = cmd.Lignes.Select(l => new LineSyncDto
                {
                    ProduitSourceId = l.ProduitId ?? 0,
                    ProduitNom = l.ProduitNom,
                    CategorieNom = l.CategorieNom,
                    PrixUnitaire = l.PrixUnitaire,
                    Quantite = l.Quantite,
                    TotalLigne = l.TotalLigne,
                    Remise = l.Remise,
                    PromotionAppliquee = l.PromotionAppliquee
                }).ToList(),
                Paiements = new List<PaymentSyncDto>()
            };
        }

        private static Commande CloneCommande(Commande s) => new()
        {
            NumeroCommande = s.NumeroCommande,
            Nom = s.Nom,
            Prenom = s.Prenom,
            Telephone = s.Telephone,
            SyncId = s.SyncId,
            Source = s.Source,
            Email = s.Email,
            Adresse = s.Adresse,
            Ville = s.Ville,
            CodePostal = s.CodePostal,
            Total = s.Total,
            TotalRemise = s.TotalRemise,
            MontantPaye = s.MontantPaye,
            MontantLivraison = s.MontantLivraison,
            AvecLivraison = s.AvecLivraison,
            NbArticles = s.NbArticles,
            Statut = s.Statut,
            ModePaiement = s.ModePaiement,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
            Poste = s.Poste,
            Lignes = s.Lignes.Select(CloneLine).ToList()
        };

        private static LigneCommande CloneLine(LigneCommande l) => new()
        {
            ProduitId = l.ProduitId,
            ProduitNom = l.ProduitNom,
            CategorieNom = l.CategorieNom,
            PrixUnitaire = l.PrixUnitaire,
            Quantite = l.Quantite,
            TotalLigne = l.TotalLigne,
            Remise = l.Remise,
            PromotionAppliquee = l.PromotionAppliquee,
            TaxTier = l.TaxTier
        };

        private static string Truncate(string? s, int max) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max]);
        private static string? TruncateN(string? s, int max) => s == null ? null : (s.Length <= max ? s : s[..max]);
    }
}
