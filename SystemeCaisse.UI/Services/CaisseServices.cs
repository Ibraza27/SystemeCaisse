using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.Infrastructure.Services;

namespace SystemeCaisse.UI.Services
{
    /// <summary>Instantané du catalogue utilisé pour démarrer la Caisse hors-ligne.</summary>
    public sealed class CatalogSnapshot
    {
        public DateTime SavedAt { get; set; } = DateTime.Now;
        public List<Produit> Produits { get; set; } = new();
        public List<Promotion> Promotions { get; set; } = new();
        public List<Promotion> DisplayPromotions { get; set; } = new();
        public Entreprise? Entreprise { get; set; }
        public Dictionary<string, string?> Configuration { get; set; } = new();
    }

    /// <summary>
    /// Cache local (JSON) du catalogue : produits, promotions actives, entreprise, réglages magasin.
    /// Mis à jour à chaque chargement réussi ; relu au démarrage si la base est injoignable.
    /// </summary>
    public sealed class CatalogCache
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _file = Path.Combine(AppPaths.CacheDir, "catalogue.json");
        private readonly object _gate = new();

        public void Save(CatalogSnapshot snapshot)
        {
            try
            {
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                lock (_gate)
                {
                    var tmp = _file + ".tmp";
                    File.WriteAllText(tmp, json);
                    File.Move(tmp, _file, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("CatalogCache", $"Écriture du cache catalogue impossible : {ex.Message}");
            }
        }

        public CatalogSnapshot? Load()
        {
            try
            {
                lock (_gate)
                {
                    if (!File.Exists(_file)) return null;
                    return JsonSerializer.Deserialize<CatalogSnapshot>(File.ReadAllText(_file), JsonOptions);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("CatalogCache", $"Lecture du cache catalogue impossible : {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Point d'accès unique aux services de données (singleton DI).
    /// Tous les ViewModels passent par ici : ils ne gardent JAMAIS de DbContext ouvert.
    /// </summary>
    public sealed class CaisseServices : IDisposable
    {
        public LocalSettingsStore Settings { get; }
        public CaisseDbContextFactory Factory { get; }
        public SalesService Sales { get; }
        public CatalogService Catalog { get; }
        public CommandeService Commandes { get; }
        public PopularityService Popularity { get; }
        public DataVersionMonitor Versions { get; }
        public ConnectivityMonitor Connectivity { get; }
        public OfflineSalesQueue OfflineQueue { get; }
        public ImageStoreService Images { get; }
        public DataTransferService Transfer { get; }
        public PostgresProvisioningService Provisioning { get; }
        public CatalogCache Cache { get; } = new();

        /// <summary>True si l'application a démarré sans base joignable (mode Caisse seule).</summary>
        public bool StartedOffline { get; set; }

        public CaisseServices(LocalSettingsStore settings, CaisseDbContextFactory factory)
        {
            Settings = settings;
            Factory = factory;
            Sales = new SalesService(factory);
            Catalog = new CatalogService(factory);
            Commandes = new CommandeService(factory);
            Popularity = new PopularityService(factory);
            Versions = new DataVersionMonitor(factory);
            Connectivity = new ConnectivityMonitor(factory);
            OfflineQueue = new OfflineSalesQueue();
            Images = new ImageStoreService(factory);
            Transfer = new DataTransferService(factory);
            Provisioning = new PostgresProvisioningService(settings);
        }

        /// <summary>Réglages du poste (copie à jour).</summary>
        public PosteSettings Poste => Settings.Current;

        public bool IsOnline => Connectivity.IsOnline;

        /// <summary>Démarre la surveillance (connexion + versions) et le renvoi automatique des ventes hors-ligne.</summary>
        public void StartBackground(bool online)
        {
            Connectivity.StatusChanged += OnConnectivityChanged;
            Versions.Changed += OnVersionsChanged;
            Connectivity.Start(online);
            Versions.Start();
            if (online)
            {
                if (OfflineQueue.Count > 0) _ = FlushOfflineAsync();
                if (Factory.Provider == DatabaseProvider.PostgreSql)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await Images.SyncCacheAsync(); } catch { }
                    });
                }
                if (Poste.EstPostePrincipal)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await Transfer.RunAutoBackupIfDueAsync(); } catch { }
                    });
                }
            }
        }

        private void OnVersionsChanged(IReadOnlyCollection<string> domains)
        {
            if (domains.Contains(DataDomains.Images))
            {
                _ = Task.Run(async () =>
                {
                    try { await Images.SyncCacheAsync(); } catch { }
                });
            }
        }

        private void OnConnectivityChanged(bool online, string? error)
        {
            if (online)
            {
                _ = FlushOfflineAsync();
                if (Factory.Provider == DatabaseProvider.PostgreSql)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await Images.SyncCacheAsync(); } catch { }
                    });
                }
            }
        }

        /// <summary>Renvoie les ventes en attente ; ne lève jamais d'exception.</summary>
        public async Task<int> FlushOfflineAsync()
        {
            try
            {
                if (OfflineQueue.Count == 0) return 0;
                int sent = await OfflineQueue.FlushAsync(Sales);
                if (sent > 0) Popularity.Invalidate();
                return sent;
            }
            catch (Exception ex)
            {
                AppLog.Error("CaisseServices", "Renvoi des ventes hors-ligne impossible", ex);
                return 0;
            }
        }

        /// <summary>
        /// Enregistre une vente ; en cas de coupure, la met en file locale (elle sera envoyée au retour du réseau).
        /// </summary>
        /// <returns>True si enregistrée en base, false si mise en file hors-ligne.</returns>
        public async Task<bool> RecordSaleOrQueueAsync(Vente vente)
        {
            if (!Connectivity.IsOnline)
            {
                OfflineQueue.Enqueue(vente);
                return false;
            }
            try
            {
                await Sales.RecordSaleAsync(vente);
                Popularity.Invalidate();
                return true;
            }
            catch (Exception ex) when (OfflineSalesQueue.IsConnectivityError(ex))
            {
                Connectivity.ReportFailure(ex.InnerException?.Message ?? ex.Message);
                OfflineQueue.Enqueue(vente);
                return false;
            }
        }

        public void Dispose()
        {
            Connectivity.StatusChanged -= OnConnectivityChanged;
            Versions.Changed -= OnVersionsChanged;
            Connectivity.Dispose();
            Versions.Dispose();
        }
    }
}
