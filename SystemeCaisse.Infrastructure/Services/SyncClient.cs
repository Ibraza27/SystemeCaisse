using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SystemeCaisse.Infrastructure.Services
{
    /// <summary>
    /// Client SignalR pour la synchronisation bidirectionnelle avec TransitManager.
    /// Se connecte au Hub /hubs/sync avec authentification par clé API.
    /// Reconnexion automatique avec backoff exponentiel.
    /// </summary>
    public class SyncClient : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private HubConnection? _connection;
        private readonly string _hubUrl;
        private readonly string _apiKey;
        private bool _isConnected;
        private int _reconnectAttempt;

        // ── Événements pour les ViewModels ──
        public event Func<OrderSyncDto, Task>? OnOrderReceived;
        public event Func<OrderSyncDto, Task>? OnOrderUpdated;
        public event Func<Guid, Task>? OnOrderDeleted;
        public event Func<Guid, PaymentSyncDto, Task>? OnPaymentAdded;
        public event Func<ProductSyncDto, Task>? OnProductUpdated;
        public event Func<ProductSyncDto, Task>? OnProductCreated;
        public event Action<string>? OnTestPingReceived;
        public event Action<string>? OnStatusChanged;

        public bool IsConnected => _isConnected;

        public SyncClient(string hubUrl, string apiKey, ILogger logger)
        {
            _hubUrl = hubUrl;
            _apiKey = apiKey;
            _logger = logger;
        }

        /// <summary>Initialise et démarre la connexion SignalR</summary>
        public async Task StartAsync(CancellationToken ct = default)
        {
            _connection = new HubConnectionBuilder()
                .WithUrl($"{_hubUrl}?clientType=caisse", options =>
                {
                    options.Headers.Add("X-Api-Key", _apiKey);
                    
                    // Forcer les WebSockets pour éviter les déconnexions LongPolling
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets | 
                                         Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                    
                    options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                    };
                })
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(30)
                })
                .Build();

            // Configurer le ping
            _connection.ServerTimeout = TimeSpan.FromMinutes(5);
            _connection.KeepAliveInterval = TimeSpan.FromSeconds(15);

            _connection.Reconnecting += _ =>
            {
                _isConnected = false;
                _reconnectAttempt++;
                _logger.LogWarning("[SyncClient] Reconnexion en cours... (tentative {Attempt})", _reconnectAttempt);
                OnStatusChanged?.Invoke($"🔄 Reconnexion... ({_reconnectAttempt})");
                return Task.CompletedTask;
            };

            _connection.Reconnected += _ =>
            {
                _isConnected = true;
                _reconnectAttempt = 0;
                _logger.LogInformation("[SyncClient] ✅ Reconnecté au Hub");
                OnStatusChanged?.Invoke("✅ Connecté");
                return Task.CompletedTask;
            };

            _connection.Closed += async ex =>
            {
                _isConnected = false;
                _logger.LogWarning(ex, "[SyncClient] Connexion fermée");
                OnStatusChanged?.Invoke("❌ Déconnecté");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    if (_connection != null) await _connection.StartAsync(ct);
                }
                catch { /* ignore — auto-reconnect will handle */ }
            };

            RegisterHandlers();

            try
            {
                await _connection.StartAsync(ct);
                _isConnected = true;
                _reconnectAttempt = 0;
                _logger.LogInformation("[SyncClient] ✅ Connecté au Hub : {Url}", _hubUrl);
                OnStatusChanged?.Invoke("✅ Connecté");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SyncClient] ❌ Connexion initiale échouée");
                OnStatusChanged?.Invoke("❌ Connexion échouée");
            }
        }

        private void RegisterHandlers()
        {
            if (_connection == null) return;

            _connection.On<OrderSyncDto>("OrderCreated", async dto =>
            {
                _logger.LogInformation("[SyncClient] 📥 OrderCreated: {Num}", dto.NumeroCommande);
                if (OnOrderReceived != null) await OnOrderReceived(dto);
            });

            _connection.On<OrderSyncDto>("OrderUpdated", async dto =>
            {
                _logger.LogInformation("[SyncClient] 📥 OrderUpdated: {SyncId}", dto.SyncId);
                if (OnOrderUpdated != null) await OnOrderUpdated(dto);
            });

            _connection.On<Guid>("OrderDeleted", async syncId =>
            {
                _logger.LogInformation("[SyncClient] 📥 OrderDeleted: {SyncId}", syncId);
                if (OnOrderDeleted != null) await OnOrderDeleted(syncId);
            });

            _connection.On<Guid, PaymentSyncDto>("PaymentAdded", async (syncId, payment) =>
            {
                _logger.LogInformation("[SyncClient] 📥 PaymentAdded: {SyncId}", syncId);
                if (OnPaymentAdded != null) await OnPaymentAdded(syncId, payment);
            });

            _connection.On<ProductSyncDto>("ProductUpdated", async dto =>
            {
                _logger.LogInformation("[SyncClient] 📥 ProductUpdated: {Nom}", dto.Nom);
                if (OnProductUpdated != null) await OnProductUpdated(dto);
            });

            _connection.On<ProductSyncDto>("ProductCreated", async dto =>
            {
                _logger.LogInformation("[SyncClient] 📥 ProductCreated: {Nom}", dto.Nom);
                if (OnProductCreated != null) await OnProductCreated(dto);
            });

            _connection.On<string>("TestPing", message =>
            {
                _logger.LogInformation("[SyncClient] 📡 TEST PING REÇU: {Message}", message);
                OnTestPingReceived?.Invoke(message);
            });

            _connection.On<string>("TestPong", message =>
            {
                _logger.LogInformation("[SyncClient] 📡 TEST PONG REÇU: {Message}", message);
                OnTestPingReceived?.Invoke(message);
            });
        }

        // ═══ ÉMISSION vers le Hub ═══

        public async Task NotifyOrderCreated(OrderSyncDto order)
        {
            if (_connection?.State != HubConnectionState.Connected) return;
            try { await _connection.InvokeAsync("OrderCreatedFromCaisse", order); }
            catch (Exception ex) { _logger.LogWarning(ex, "[SyncClient] Erreur émission OrderCreated"); }
        }

        public async Task NotifyOrderUpdated(OrderSyncDto order)
        {
            if (_connection?.State != HubConnectionState.Connected) return;
            try { await _connection.InvokeAsync("OrderUpdatedFromCaisse", order); }
            catch (Exception ex) { _logger.LogWarning(ex, "[SyncClient] Erreur émission OrderUpdated"); }
        }

        public async Task NotifyOrderDeleted(Guid syncId)
        {
            if (_connection?.State != HubConnectionState.Connected) return;
            try { await _connection.InvokeAsync("OrderDeletedFromCaisse", syncId); }
            catch (Exception ex) { _logger.LogWarning(ex, "[SyncClient] Erreur émission OrderDeleted"); }
        }

        public async Task NotifyPaymentAdded(Guid syncId, PaymentSyncDto payment)
        {
            if (_connection?.State != HubConnectionState.Connected) return;
            try { await _connection.InvokeAsync("PaymentAddedFromCaisse", syncId, payment); }
            catch (Exception ex) { _logger.LogWarning(ex, "[SyncClient] Erreur émission PaymentAdded"); }
        }

        public async Task NotifyProductUpdated(ProductSyncDto product)
        {
            if (_connection?.State != HubConnectionState.Connected) return;
            try { await _connection.InvokeAsync("ProductUpdatedFromCaisse", product); }
            catch (Exception ex) { _logger.LogWarning(ex, "[SyncClient] Erreur émission ProductUpdated"); }
        }

        public async Task NotifyTestPing(string message)
        {
            if (_connection?.State != HubConnectionState.Connected) return;
            try { await _connection.InvokeAsync("TestPingFromCaisse", message); }
            catch (Exception ex) { _logger.LogWarning(ex, "[SyncClient] Erreur émission TestPing"); }
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                try { await _connection.StopAsync(); } catch { }
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  DTOs de sync (miroir de TransitManager.Core.DTOs.SyncDtos)
    // ═══════════════════════════════════════════════════════════

    public class OrderSyncDto
    {
        public Guid SyncId { get; set; } = Guid.NewGuid();
        public string NumeroCommande { get; set; } = "";
        public string Source { get; set; } = "web";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string Nom { get; set; } = "";
        public string Prenom { get; set; } = "";
        public string Telephone { get; set; } = "";
        public string? Email { get; set; }
        public string? Ville { get; set; }
        public string? CodePostal { get; set; }
        public string ModeLivraison { get; set; } = "depot";
        public string? AdresseLivraison { get; set; }
        public decimal? DistanceKm { get; set; }
        public decimal FraisLivraison { get; set; }
        public decimal MontantLivraison { get; set; }
        public bool FraisADeterminer { get; set; }
        public decimal SousTotal { get; set; }
        public decimal TotalRemise { get; set; }
        public decimal Total { get; set; }
        public decimal MontantPaye { get; set; }
        public string? ModePaiement { get; set; }
        public string Statut { get; set; } = "en_attente";
        public DateTime? DateTraitement { get; set; }
        public string? NoteAdmin { get; set; }
        public List<LineSyncDto> Lignes { get; set; } = new();
        public List<PaymentSyncDto> Paiements { get; set; } = new();
    }

    public class LineSyncDto
    {
        public int ProduitSourceId { get; set; }
        public string ProduitNom { get; set; } = "";
        public string? CategorieNom { get; set; }
        public decimal PrixUnitaire { get; set; }
        public decimal Quantite { get; set; }
        public decimal TotalLigne { get; set; }
        public decimal? Remise { get; set; }
        public string? PromotionAppliquee { get; set; }
    }

    public class PaymentSyncDto
    {
        public decimal Montant { get; set; }
        public string ModePaiement { get; set; } = "espece";
        public DateTime DatePaiement { get; set; } = DateTime.UtcNow;
        public string? Note { get; set; }
    }

    public class ProductSyncDto
    {
        public int SourceId { get; set; }
        public string Nom { get; set; } = "";
        public decimal PrixVente { get; set; }
        public string? Categorie { get; set; }
        public string TypeVente { get; set; } = "unite";
        public bool Actif { get; set; } = true;
        public string? CodeBarre { get; set; }
        public string? ImagePath { get; set; }
    }
}
