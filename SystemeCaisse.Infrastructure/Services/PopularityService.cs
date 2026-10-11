using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    /// <summary>Quantités vendues par produit (popularité), calculées par la base.</summary>
    public sealed class PopularitySnapshot
    {
        public IReadOnlyDictionary<int, decimal> ByProductId { get; init; } = new Dictionary<int, decimal>();
        /// <summary>Lignes anciennes sans ProduitId : regroupées par nom (compatibilité).</summary>
        public IReadOnlyDictionary<string, decimal> ByProductName { get; init; } = new Dictionary<string, decimal>();
        public DateTime ComputedAt { get; init; } = DateTime.Now;

        public decimal Get(int productId, string? productName)
        {
            if (ByProductId.TryGetValue(productId, out var q)) return q;
            if (productName != null && ByProductName.TryGetValue(productName, out var n)) return n;
            return 0;
        }
    }

    /// <summary>
    /// Calcul de popularité : GROUP BY exécuté côté base (quelques centaines de lignes transférées
    /// au lieu de TOUTES les lignes de vente) + cache mémoire.
    /// </summary>
    public sealed class PopularityService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly TimeSpan _cacheDuration;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private PopularitySnapshot? _cache;

        public PopularityService(IDbContextFactory<AppDbContext> factory, TimeSpan? cacheDuration = null)
        {
            _factory = factory;
            _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(10);
        }

        /// <summary>Invalide le cache (après une vente sur ce poste ou un changement signalé par DataVersions).</summary>
        public void Invalidate() => _cache = null;

        public async Task<PopularitySnapshot> GetAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            var cached = _cache;
            if (!forceRefresh && cached != null && DateTime.Now - cached.ComputedAt < _cacheDuration) return cached;

            await _gate.WaitAsync(ct);
            try
            {
                cached = _cache;
                if (!forceRefresh && cached != null && DateTime.Now - cached.ComputedAt < _cacheDuration) return cached;

                var snapshot = await ComputeAsync(ct);
                _cache = snapshot;
                return snapshot;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<PopularitySnapshot> ComputeAsync(CancellationToken ct)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var lines = db.LignesVente.AsNoTracking().Where(l => l.Vente == null || l.Vente.Statut != "annulee");

            try
            {
                // CAST en double : agrégat natif sur les deux moteurs (SQLite stocke les décimaux en TEXT).
                var byId = await lines.Where(l => l.ProduitId != null)
                    .GroupBy(l => l.ProduitId!.Value)
                    .Select(g => new { Id = g.Key, Qty = g.Sum(x => (double)x.Quantite) })
                    .ToListAsync(ct);

                var byName = await lines.Where(l => l.ProduitId == null)
                    .GroupBy(l => l.ProduitNom)
                    .Select(g => new { Name = g.Key, Qty = g.Sum(x => (double)x.Quantite) })
                    .ToListAsync(ct);

                return new PopularitySnapshot
                {
                    ByProductId = byId.ToDictionary(x => x.Id, x => Math.Round((decimal)x.Qty, 3)),
                    ByProductName = byName.Where(x => x.Name != null)
                        .GroupBy(x => x.Name)
                        .ToDictionary(g => g.Key, g => Math.Round((decimal)g.Sum(x => x.Qty), 3)),
                    ComputedAt = DateTime.Now
                };
            }
            catch (InvalidOperationException ex)
            {
                // Repli (traduction SQL impossible sur un moteur) : agrégat en mémoire sur colonnes minimales.
                AppLog.Warn("Popularity", $"Agrégat SQL impossible, repli mémoire : {ex.Message}");
                var raw = await lines.Select(l => new { l.ProduitId, l.ProduitNom, l.Quantite }).ToListAsync(ct);
                return new PopularitySnapshot
                {
                    ByProductId = raw.Where(r => r.ProduitId.HasValue)
                        .GroupBy(r => r.ProduitId!.Value)
                        .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantite)),
                    ByProductName = raw.Where(r => !r.ProduitId.HasValue && r.ProduitNom != null)
                        .GroupBy(r => r.ProduitNom)
                        .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantite)),
                    ComputedAt = DateTime.Now
                };
            }
        }
    }
}
