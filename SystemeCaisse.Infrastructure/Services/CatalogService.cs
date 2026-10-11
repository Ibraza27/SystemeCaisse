using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    /// <summary>Modification d'un produit : valeurs actuelles + valeurs au chargement (null = nouveau produit).</summary>
    public sealed record ProductChange(Produit Current, ProductSnapshot? Original);

    /// <summary>Copie figée des champs éditables d'un produit, prise au chargement.</summary>
    public sealed record ProductSnapshot(
        int Id, string Nom, int TaxTier, string? CodeBarre, decimal PrixVente, decimal PrixAchat,
        string TypeVente, string? Categorie, int? FournisseurId, decimal StockActuel, decimal StockAlerte,
        decimal TauxTVA, bool Actif, string? ImagePath)
    {
        public static ProductSnapshot Of(Produit p) => new(
            p.Id, p.Nom, p.TaxTier, p.CodeBarre, p.PrixVente, p.PrixAchat, p.TypeVente, p.Categorie,
            p.FournisseurId, p.StockActuel, p.StockAlerte, p.TauxTVA, p.Actif, p.ImagePath);

        public bool DiffersFrom(Produit p) => this != Of(p);
    }

    /// <summary>
    /// Lecture du catalogue (toujours AsNoTracking) et écritures produits / stock.
    ///
    /// Règle clé multi-poste : on n'écrit JAMAIS une valeur de stock lue auparavant. Toute variation
    /// de stock est appliquée en delta par la base (voir <see cref="StockOperations"/>), ce qui préserve
    /// les ventes faites entre-temps sur les autres postes.
    /// </summary>
    public sealed class CatalogService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public CatalogService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        // ───────────────────────── Lecture ─────────────────────────

        public async Task<List<Produit>> GetProductsAsync(bool includeFournisseur = false, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            IQueryable<Produit> q = db.Produits.AsNoTracking();
            if (includeFournisseur) q = q.Include(p => p.Fournisseur);
            return await q.OrderBy(p => p.Nom).ToListAsync(ct);
        }

        public async Task<List<Fournisseur>> GetFournisseursAsync(CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            return await db.Fournisseurs.AsNoTracking().ToListAsync(ct);
        }

        public async Task<List<string>> GetCategoriesAsync(CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var cats = await db.Produits.AsNoTracking()
                .Where(p => p.Categorie != null && p.Categorie != "")
                .Select(p => p.Categorie!)
                .Distinct()
                .ToListAsync(ct);
            return cats.OrderBy(c => c).ToList();
        }

        public async Task<List<Promotion>> GetActivePromotionsAsync(CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var now = DateTime.Now;
            return await db.Promotions.AsNoTracking()
                .Include(p => p.Tiers)
                .Include(p => p.BundleItems)
                .Where(p => p.Actif && p.DateDebut <= now && (p.DateFin == null || p.DateFin >= now))
                .AsSplitQuery()
                .ToListAsync(ct);
        }

        public async Task<Entreprise?> GetEntrepriseAsync(CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            return await db.Entreprise.AsNoTracking().OrderBy(e => e.Id).FirstOrDefaultAsync(ct);
        }

        /// <summary>Valeurs de la table Configuration partagée (réglages MAGASIN, pas périphériques).</summary>
        public async Task<Dictionary<string, string?>> GetConfigurationAsync(CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            return await db.Configuration.AsNoTracking().ToDictionaryAsync(c => c.Cle, c => c.Valeur, ct);
        }

        public async Task<bool> HasSalesAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            return await db.LignesVente.AnyAsync(l => l.ProduitId == productId, ct);
        }

        /// <summary>Derniers mouvements de stock (limités côté base).</summary>
        public async Task<List<MouvementStock>> GetLastMovementsAsync(int take = 100, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            return await db.MouvementsStock.AsNoTracking()
                .Include(m => m.Produit)
                .OrderByDescending(m => m.DateMouvement)
                .ThenByDescending(m => m.Id)
                .Take(take)
                .ToListAsync(ct);
        }

        /// <summary>Date de dernière entrée en stock par produit (agrégat côté base).</summary>
        public async Task<Dictionary<int, DateTime>> GetLastEntryDatesAsync(CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var rows = await db.MouvementsStock.AsNoTracking()
                .Where(m => m.TypeMouvement == "entree")
                .GroupBy(m => m.ProduitId)
                .Select(g => new { g.Key, Last = g.Max(m => m.DateMouvement) })
                .ToListAsync(ct);
            return rows.ToDictionary(r => r.Key, r => r.Last);
        }

        // ───────────────────────── Écriture produits ─────────────────────────

        /// <summary>
        /// Enregistre les produits modifiés / ajoutés. Les champs descriptifs sont copiés sur l'entité
        /// relue en base ; une modification du stock est appliquée en DELTA (atomique).
        /// </summary>
        /// <returns>Les produits ajoutés (Id renseigné).</returns>
        public async Task<List<Produit>> SaveProductsAsync(IReadOnlyList<ProductChange> changes, CancellationToken ct = default)
        {
            if (changes.Count == 0) return new List<Produit>();
            await using var db0 = await _factory.CreateDbContextAsync(ct);
            return await db0.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var db = await _factory.CreateDbContextAsync(token);
                await using var tx = await db.Database.BeginTransactionAsync(token);
                var now = DateTime.Now;
                var added = new List<Produit>();
                var stockDeltas = new List<(int Id, decimal Delta)>();

                foreach (var change in changes)
                {
                    var src = change.Current;
                    if (change.Original == null || src.Id == 0)
                    {
                        var entity = new Produit { CreatedAt = now };
                        CopyEditableFields(src, entity);
                        entity.StockActuel = src.StockActuel;
                        entity.UpdatedAt = now;
                        db.Produits.Add(entity);
                        added.Add(entity);
                        continue;
                    }

                    var fresh = await db.Produits.FirstOrDefaultAsync(p => p.Id == src.Id, token);
                    if (fresh == null) continue; // supprimé par un autre poste entre-temps

                    CopyEditableFields(src, fresh);
                    fresh.UpdatedAt = now;

                    var delta = src.StockActuel - change.Original.StockActuel;
                    if (delta != 0) stockDeltas.Add((src.Id, delta));
                }

                await db.SaveChangesAsync(token);
                foreach (var (id, delta) in stockDeltas)
                    await StockOperations.AdjustStockAsync(db, id, delta, token);

                if (stockDeltas.Count > 0)
                    await DataVersionBump.BumpAsync(db, token, DataDomains.Produits, DataDomains.Stocks);
                else
                    await DataVersionBump.BumpAsync(db, token, DataDomains.Produits);

                await tx.CommitAsync(token);
                return added;
            }, ct);
        }

        public async Task<Produit> AddProductAsync(Produit produit, CancellationToken ct = default)
        {
            var added = await SaveProductsAsync(new[] { new ProductChange(produit, null) }, ct);
            return added[0];
        }

        public async Task DeleteProductAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var entity = await db.Produits.FirstOrDefaultAsync(p => p.Id == productId, ct);
            if (entity == null) return;
            db.Produits.Remove(entity);
            await db.SaveChangesAsync(ct);
            await DataVersionBump.BumpAsync(db, ct, DataDomains.Produits, DataDomains.Stocks);
        }

        private static void CopyEditableFields(Produit src, Produit dest)
        {
            dest.Nom = src.Nom;
            dest.TaxTier = src.TaxTier;
            dest.CodeBarre = src.CodeBarre;
            dest.PrixVente = src.PrixVente;
            dest.PrixAchat = src.PrixAchat;
            dest.TypeVente = src.TypeVente;
            dest.Categorie = string.IsNullOrWhiteSpace(src.Categorie) ? "Autre" : src.Categorie;
            dest.FournisseurId = src.FournisseurId;
            dest.StockAlerte = src.StockAlerte;
            dest.TauxTVA = src.TauxTVA;
            dest.Actif = src.Actif;
            dest.ImagePath = src.ImagePath;
        }

        // ───────────────────────── Mouvements de stock ─────────────────────────

        /// <summary>
        /// Enregistre un mouvement manuel ("entree", "sortie", "inventaire" = correction relative)
        /// et applique la variation de stock de façon atomique.
        /// </summary>
        public async Task RecordMovementAsync(int productId, string productName, string type, decimal quantite,
            decimal? prixUnitaire, string? commentaire, CancellationToken ct = default)
        {
            await using var db0 = await _factory.CreateDbContextAsync(ct);
            await db0.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var db = await _factory.CreateDbContextAsync(token);
                await using var tx = await db.Database.BeginTransactionAsync(token);

                db.MouvementsStock.Add(new MouvementStock
                {
                    ProduitId = productId,
                    TypeMouvement = type,
                    Quantite = quantite,
                    PrixUnitaire = type == "entree" ? prixUnitaire : null,
                    Commentaire = commentaire,
                    DateMouvement = DateTime.Now,
                    ProduitNomSnapshot = productName
                });
                await db.SaveChangesAsync(token);

                decimal delta = type switch
                {
                    "entree" => quantite,
                    "sortie" => -quantite,
                    "inventaire" => quantite,
                    _ => 0
                };
                await StockOperations.AdjustStockAsync(db, productId, delta, token);

                if (type == "entree" && prixUnitaire.HasValue && prixUnitaire.Value > 0)
                {
                    var prix = prixUnitaire.Value;
                    await db.Produits.Where(p => p.Id == productId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.PrixAchat, prix), token);
                }

                await DataVersionBump.BumpAsync(db, token, DataDomains.Stocks);
                await tx.CommitAsync(token);
            }, ct);
        }
    }
}
