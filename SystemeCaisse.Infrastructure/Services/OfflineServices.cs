using System.Text.Json;
using System.Text.Json.Serialization;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    /// <summary>
    /// Surveille la disponibilité de la base (SELECT 1 toutes les N secondes, hors thread UI).
    /// </summary>
    public sealed class ConnectivityMonitor : IDisposable
    {
        private readonly CaisseDbContextFactory _factory;
        private readonly TimeSpan _onlineInterval;
        private readonly TimeSpan _offlineInterval;
        private CancellationTokenSource? _cts;
        private volatile bool _isOnline = true;

        /// <summary>Déclenché (thread de fond) à chaque changement d'état : true = base joignable.</summary>
        public event Action<bool, string?>? StatusChanged;

        public bool IsOnline => _isOnline;
        public string? LastError { get; private set; }

        public ConnectivityMonitor(CaisseDbContextFactory factory, TimeSpan? onlineInterval = null, TimeSpan? offlineInterval = null)
        {
            _factory = factory;
            _onlineInterval = onlineInterval ?? TimeSpan.FromSeconds(10);
            _offlineInterval = offlineInterval ?? TimeSpan.FromSeconds(5);
        }

        public void Start(bool initialOnline = true)
        {
            if (_cts != null) return;
            _isOnline = initialOnline;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_isOnline ? _onlineInterval : _offlineInterval, token);
                        await CheckNowAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("Connectivity", $"Boucle de surveillance : {ex.Message}");
                    }
                }
            }, token);
        }

        /// <summary>Teste immédiatement la connexion et notifie si l'état change.</summary>
        public async Task<bool> CheckNowAsync(CancellationToken ct = default)
        {
            var error = await _factory.TestConnectionAsync(ct);
            bool online = error == null;
            LastError = error;
            if (online != _isOnline)
            {
                _isOnline = online;
                AppLog.Info("Connectivity", online ? "Base de données de nouveau joignable." : $"Base injoignable : {error}");
                try { StatusChanged?.Invoke(online, error); }
                catch (Exception ex) { AppLog.Error("Connectivity", "Erreur dans un abonné StatusChanged", ex); }
            }
            return online;
        }

        /// <summary>Signale une erreur de connexion constatée par un service (bascule immédiate hors-ligne).</summary>
        public void ReportFailure(string error)
        {
            if (!_isOnline) return;
            _isOnline = false;
            LastError = error;
            AppLog.Warn("Connectivity", $"Échec signalé : {error}");
            try { StatusChanged?.Invoke(false, error); }
            catch (Exception ex) { AppLog.Error("Connectivity", "Erreur dans un abonné StatusChanged", ex); }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { /* déjà arrêté */ }
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// File locale des ventes encaissées pendant une coupure (fichiers JSON dans %ProgramData%).
    /// Chaque vente porte un ClientUid : le renvoi est idempotent (aucun doublon possible).
    /// </summary>
    public sealed class OfflineSalesQueue
    {
        private sealed class Envelope
        {
            public int Attempts { get; set; }
            public string? LastError { get; set; }
            public DateTime QueuedAt { get; set; } = DateTime.Now;
            public Vente Vente { get; set; } = new();
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _dir;
        private readonly string _failedDir;
        private readonly SemaphoreSlim _flushGate = new(1, 1);
        private const int MaxAttemptsBeforeQuarantine = 20;

        /// <summary>Nombre de ventes en attente (déclenché sur thread quelconque).</summary>
        public event Action<int>? CountChanged;

        public OfflineSalesQueue(string? directory = null)
        {
            _dir = directory ?? AppPaths.OfflineQueueDir;
            _failedDir = Path.Combine(_dir, "echecs");
            Directory.CreateDirectory(_dir);
        }

        public int Count
        {
            get
            {
                try { return Directory.GetFiles(_dir, "*.json").Length; }
                catch (Exception ex)
                {
                    AppLog.Warn("OfflineQueue", $"Lecture de la file impossible : {ex.Message}");
                    return 0;
                }
            }
        }

        /// <summary>Met une vente en file (écriture atomique). La vente doit avoir un ClientUid.</summary>
        public void Enqueue(Vente vente)
        {
            if (string.IsNullOrWhiteSpace(vente.ClientUid)) vente.ClientUid = Guid.NewGuid().ToString("D");
            var env = new Envelope { Vente = StripNavigation(vente) };
            var path = Path.Combine(_dir, $"{vente.CreatedAt:yyyyMMddHHmmssfff}_{vente.ClientUid}.json");
            WriteAtomic(path, env);
            AppLog.Info("OfflineQueue", $"Vente {vente.NumeroTicket} mise en file hors-ligne ({vente.Total:0.00} €).");
            CountChanged?.Invoke(Count);
        }

        /// <summary>
        /// Renvoie les ventes en attente vers la base. S'arrête à la première erreur de connexion.
        /// </summary>
        /// <returns>Nombre de ventes envoyées.</returns>
        public async Task<int> FlushAsync(SalesService sales, CancellationToken ct = default)
        {
            if (!await _flushGate.WaitAsync(0, ct)) return 0;
            int sent = 0;
            try
            {
                foreach (var file in Directory.GetFiles(_dir, "*.json").OrderBy(f => f))
                {
                    ct.ThrowIfCancellationRequested();
                    Envelope? env;
                    try
                    {
                        env = JsonSerializer.Deserialize<Envelope>(await File.ReadAllTextAsync(file, ct), JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("OfflineQueue", $"Fichier illisible mis de côté : {file}", ex);
                        Quarantine(file);
                        continue;
                    }
                    if (env == null) { Quarantine(file); continue; }

                    try
                    {
                        await sales.RecordSaleAsync(env.Vente, ct);
                        File.Delete(file);
                        sent++;
                    }
                    catch (Exception ex) when (IsConnectivityError(ex))
                    {
                        AppLog.Warn("OfflineQueue", $"Renvoi interrompu (base injoignable) : {ex.Message}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        env.Attempts++;
                        env.LastError = ex.InnerException?.Message ?? ex.Message;
                        AppLog.Error("OfflineQueue", $"Renvoi de la vente {env.Vente.ClientUid} refusé (tentative {env.Attempts})", ex);
                        if (env.Attempts >= MaxAttemptsBeforeQuarantine) Quarantine(file);
                        else WriteAtomic(file, env);
                    }
                }
            }
            finally
            {
                _flushGate.Release();
                CountChanged?.Invoke(Count);
            }
            if (sent > 0) AppLog.Info("OfflineQueue", $"{sent} vente(s) hors-ligne synchronisée(s).");
            return sent;
        }

        public static bool IsConnectivityError(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                switch (e)
                {
                    case TimeoutException:
                    case System.Net.Sockets.SocketException:
                    case IOException:
                        return true;
                    case Npgsql.NpgsqlException npg when npg.IsTransient:
                        return true;
                    case Npgsql.NpgsqlException npg2 when npg2 is not Npgsql.PostgresException:
                        return true; // erreur de transport (pas une erreur SQL renvoyée par le serveur)
                    case Microsoft.Data.Sqlite.SqliteException sq when sq.SqliteErrorCode is 5 or 6 or 10 or 14:
                        return true; // BUSY, LOCKED, IOERR, CANTOPEN
                }
                if (e.GetType().Name == "RetryLimitExceededException") return true;
            }
            return false;
        }

        private static Vente StripNavigation(Vente v)
        {
            foreach (var l in v.Lignes)
            {
                l.Vente = null;
                l.Produit = null;
            }
            return v;
        }

        private void Quarantine(string file)
        {
            try
            {
                Directory.CreateDirectory(_failedDir);
                File.Move(file, Path.Combine(_failedDir, Path.GetFileName(file)), overwrite: true);
            }
            catch (Exception ex)
            {
                AppLog.Error("OfflineQueue", $"Impossible de déplacer {file} vers {_failedDir}", ex);
            }
        }

        private static void WriteAtomic(string path, Envelope env)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(env, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
