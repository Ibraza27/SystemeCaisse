using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    /// <summary>Résultat d'un enregistrement de vente.</summary>
    /// <param name="Vente">Vente telle qu'enregistrée (Id renseigné).</param>
    /// <param name="AlreadyRecorded">True si la vente existait déjà (renvoi idempotent depuis la file hors-ligne).</param>
    public sealed record SaleResult(Vente Vente, bool AlreadyRecorded);

    /// <summary>
    /// Enregistrement ATOMIQUE d'une vente : vente + lignes + mouvements de stock + décrément du stock,
    /// dans une seule transaction, avec reprise automatique sur coupure réseau.
    ///
    /// - Le stock est décrémenté côté base (UPDATE ... SET StockActuel = StockActuel - q) : deux postes
    ///   qui vendent le même produit en même temps ne s'écrasent jamais.
    /// - Idempotence : chaque vente porte un ClientUid (GUID) unique ; un renvoi ne crée pas de doublon.
    /// </summary>
    public sealed class SalesService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public SalesService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        /// <summary>Génère un numéro de ticket lisible et unique par poste : CODE-yyMMddHHmmssf.</summary>
        public static string GenerateTicketNumber(string codePoste, DateTime when)
        {
            var code = string.IsNullOrWhiteSpace(codePoste) ? "P" : codePoste.Trim().ToUpperInvariant();
            if (code.Length > 4) code = code[..4];
            return $"{code}-{when:yyMMddHHmmss}{when.Millisecond / 100}";
        }

        public async Task<SaleResult> RecordSaleAsync(Vente vente, CancellationToken ct = default)
        {
            if (vente == null) throw new ArgumentNullException(nameof(vente));
            if (string.IsNullOrWhiteSpace(vente.ClientUid)) vente.ClientUid = Guid.NewGuid().ToString("D");

            // 1. Déjà enregistrée ? (renvoi depuis la file hors-ligne)
            var existing = await FindByClientUidAsync(vente.ClientUid!, ct);
            if (existing != null) return new SaleResult(existing, true);

            // 2. Produits encore existants (un produit supprimé entre-temps ne doit pas bloquer la vente).
            var productIds = vente.Lignes.Where(l => l.ProduitId.HasValue).Select(l => l.ProduitId!.Value).Distinct().ToList();
            HashSet<int> existingIds;
            await using (var db = await _factory.CreateDbContextAsync(ct))
            {
                existingIds = (await db.Produits.AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .Select(p => p.Id)
                    .ToListAsync(ct)).ToHashSet();
            }

            try
            {
                await using var strategyDb = await _factory.CreateDbContextAsync(ct);
                var strategy = strategyDb.Database.CreateExecutionStrategy();
                var saved = await strategy.ExecuteAsync(async token =>
                {
                    // Contexte et entités NEUFS à chaque tentative (une tentative échouée laisse un état sale).
                    await using var db = await _factory.CreateDbContextAsync(token);
                    await using var tx = await db.Database.BeginTransactionAsync(token);

                    var entity = CloneForInsert(vente, existingIds);
                    db.Ventes.Add(entity);

                    var now = entity.CreatedAt;
                    foreach (var ligne in entity.Lignes.Where(l => l.ProduitId.HasValue))
                    {
                        db.MouvementsStock.Add(new MouvementStock
                        {
                            ProduitId = ligne.ProduitId!.Value,
                            TypeMouvement = "sortie",
                            Quantite = ligne.Quantite,
                            PrixUnitaire = ligne.PrixUnitaire,
                            DateMouvement = now,
                            Commentaire = "Vente",
                            ProduitNomSnapshot = ligne.ProduitNom
                        });
                    }
                    await db.SaveChangesAsync(token);

                    foreach (var group in entity.Lignes.Where(l => l.ProduitId.HasValue).GroupBy(l => l.ProduitId!.Value))
                        await StockOperations.AdjustStockAsync(db, group.Key, -group.Sum(l => l.Quantite), token);

                    await DataVersionBump.BumpAsync(db, token, DataDomains.Ventes, DataDomains.Stocks);
                    await tx.CommitAsync(token);
                    return entity;
                }, ct);

                return new SaleResult(saved, false);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Une tentative précédente avait en fait abouti (réponse perdue) : on renvoie la vente existante.
                var already = await FindByClientUidAsync(vente.ClientUid!, ct);
                if (already != null)
                {
                    AppLog.Info("Sales", $"Vente {vente.ClientUid} déjà enregistrée (renvoi idempotent).");
                    return new SaleResult(already, true);
                }
                throw;
            }
        }

        private async Task<Vente?> FindByClientUidAsync(string clientUid, CancellationToken ct)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            return await db.Ventes.AsNoTracking().Include(v => v.Lignes)
                .FirstOrDefaultAsync(v => v.ClientUid == clientUid, ct);
        }

        private static Vente CloneForInsert(Vente src, HashSet<int> existingProductIds)
        {
            var v = new Vente
            {
                NumeroTicket = src.NumeroTicket,
                CreatedAt = src.CreatedAt,
                ClientUid = src.ClientUid,
                Poste = src.Poste,
                Total = src.Total,
                NbArticles = src.NbArticles,
                MoyenPaiement = src.MoyenPaiement,
                TotalRemise = src.TotalRemise,
                MontantEspeces = src.MontantEspeces,
                MontantCB = src.MontantCB,
                MonnaieRendue = src.MonnaieRendue,
                Statut = src.Statut
            };
            foreach (var l in src.Lignes)
            {
                int? pid = l.ProduitId.HasValue && existingProductIds.Contains(l.ProduitId.Value) ? l.ProduitId : null;
                v.Lignes.Add(new LigneVente
                {
                    ProduitId = pid,
                    ProduitNom = l.ProduitNom,
                    CategorieNom = l.CategorieNom,
                    PrixUnitaire = l.PrixUnitaire,
                    Quantite = l.Quantite,
                    TotalLigne = l.TotalLigne,
                    Remise = l.Remise,
                    PromotionAppliquee = l.PromotionAppliquee,
                    TaxTier = l.TaxTier
                });
            }
            return v;
        }

        internal static bool IsUniqueViolation(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation) return true;
                if (e is SqliteException sq && sq.SqliteErrorCode == 19 &&
                    (sq.SqliteExtendedErrorCode == 2067 || sq.SqliteExtendedErrorCode == 1555)) return true;
            }
            return false;
        }
    }

    /// <summary>Opérations de stock atomiques, partagées par les services.</summary>
    public static class StockOperations
    {
        /// <summary>
        /// Ajoute <paramref name="delta"/> (négatif pour une sortie) au stock, en une seule instruction SQL
        /// exécutée par la base : aucune lecture préalable, donc aucun écrasement entre postes.
        /// </summary>
        public static async Task AdjustStockAsync(AppDbContext db, int productId, decimal delta, CancellationToken ct)
        {
            if (delta == 0) return;
            var now = DateTime.Now;
            if (db.IsPostgres)
            {
                await db.Produits.Where(p => p.Id == productId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.StockActuel, p => p.StockActuel + delta)
                        .SetProperty(p => p.UpdatedAt, now), ct);
            }
            else
            {
                // SQLite stocke les décimaux en TEXT : calcul en REAL arrondi à 3 décimales (quantités au gramme).
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"Produits\" SET \"StockActuel\" = ROUND(CAST(\"StockActuel\" AS REAL) + {(double)delta}, 3), \"UpdatedAt\" = {now} WHERE \"Id\" = {productId}", ct);
            }
        }
    }
}
