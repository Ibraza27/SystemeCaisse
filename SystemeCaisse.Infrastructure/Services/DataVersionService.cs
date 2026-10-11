using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    /// <summary>
    /// Incrément des compteurs de version (à appeler dans la même transaction que l'écriture métier).
    /// </summary>
    public static class DataVersionBump
    {
        public static async Task BumpAsync(AppDbContext db, CancellationToken ct, params string[] domains)
        {
            var now = DateTime.Now;
            foreach (var domain in domains.Distinct())
            {
                int updated = await db.DataVersions
                    .Where(v => v.Domaine == domain)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.Version, v => v.Version + 1)
                        .SetProperty(v => v.UpdatedAt, now), ct);

                if (updated == 0)
                {
                    // Ligne absente (base créée avant le seed) : on la crée.
                    db.DataVersions.Add(new DataVersion { Domaine = domain, Version = 1, UpdatedAt = now });
                    await db.SaveChangesAsync(ct);
                }
            }
        }
    }

    /// <summary>
    /// Surveille la table DataVersions (quelques octets) et signale les domaines modifiés
    /// par N'IMPORTE QUEL poste. Remplace les rechargements complets systématiques.
    /// </summary>
    public sealed class DataVersionMonitor : IDisposable
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly TimeSpan _interval;
        private readonly Dictionary<string, long> _known = new();
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _loop;

        /// <summary>Déclenché (thread de fond) avec la liste des domaines modifiés depuis le dernier contrôle.</summary>
        public event Action<IReadOnlyCollection<string>>? Changed;

        public DataVersionMonitor(IDbContextFactory<AppDbContext> factory, TimeSpan? interval = null)
        {
            _factory = factory;
            _interval = interval ?? TimeSpan.FromSeconds(15);
        }

        public void Start()
        {
            if (_loop != null) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        /// <summary>Force un contrôle immédiat (ex : retour sur un onglet).</summary>
        public Task<IReadOnlyCollection<string>> CheckNowAsync(CancellationToken ct = default) => PollAsync(ct);

        private async Task LoopAsync(CancellationToken ct)
        {
            // Premier passage : mémorise l'état sans notifier.
            await PollAsync(ct, notify: false);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, ct);
                    await PollAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Serveur momentanément injoignable : le ConnectivityMonitor gère l'affichage.
                    AppLog.Warn("DataVersion", $"Contrôle des versions impossible : {ex.Message}");
                }
            }
        }

        private async Task<IReadOnlyCollection<string>> PollAsync(CancellationToken ct, bool notify = true)
        {
            List<DataVersion> rows;
            try
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                rows = await db.DataVersions.AsNoTracking().ToListAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<string>();
            }
            catch (Exception ex)
            {
                AppLog.Warn("DataVersion", $"Lecture DataVersions impossible : {ex.Message}");
                return Array.Empty<string>();
            }

            var changed = new List<string>();
            lock (_lock)
            {
                foreach (var row in rows)
                {
                    if (_known.TryGetValue(row.Domaine, out var v) && v == row.Version) continue;
                    if (_known.ContainsKey(row.Domaine)) changed.Add(row.Domaine);
                    _known[row.Domaine] = row.Version;
                }
            }

            if (notify && changed.Count > 0)
            {
                try { Changed?.Invoke(changed); }
                catch (Exception ex) { AppLog.Error("DataVersion", "Erreur dans un abonné Changed", ex); }
            }
            return changed;
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { /* déjà arrêté */ }
            _cts?.Dispose();
            _cts = null;
            _loop = null;
        }
    }
}
