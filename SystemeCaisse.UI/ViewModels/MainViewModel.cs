using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.UI.Views;
using SystemeCaisse.UI.Services;
using SystemeCaisse.Core.Interfaces;
using SystemeCaisse.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace SystemeCaisse.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly AppDbContext _context;
        private readonly PrintService _printService;
        private readonly IDataMigrationService _migrationService;
        private readonly CaisseServices _caisseServices;
        private volatile bool _caisseCatalogDirty;
        private volatile bool _caissePromosDirty;

        public void MarkCaisseDirty(bool catalog = true, bool promotions = false)
        {
            if (catalog) _caisseCatalogDirty = true;
            if (promotions) _caissePromosDirty = true;
        }

        private void OnDataVersionsChanged(IReadOnlyCollection<string> domains)
        {
            if (domains.Contains(DataDomains.Produits) || domains.Contains(DataDomains.Stocks))
            {
                _caisseCatalogDirty = true;
                if (SelectedTabIndex == 1 && Panier.Count == 0)
                {
                    _caisseCatalogDirty = false;
                    _ = ReloadProductsFromDbAsync();
                }
            }
            if (domains.Contains(DataDomains.Promotions))
            {
                _caissePromosDirty = true;
                if (SelectedTabIndex == 1 && Panier.Count == 0)
                {
                    _caissePromosDirty = false;
                    _ = Task.Run(() =>
                    {
                        LoadPromotions();
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            ApplyAutomaticPromotions();
                            UpdateTotal();
                        }));
                    });
                }
            }
            if (domains.Contains(DataDomains.Commandes))
            {
                if (SelectedTabIndex == 2)
                {
                    _ = CommandesVM?.RefreshIfNeededAsync();
                }
            }
        }

        private List<Promotion> _activePromotions = new();
        
        public ObservableCollection<Produit> Produits { get; set; }
        public ObservableCollection<Produit> TopProducts { get; set; }
        public ICollectionView ProductsView { get; private set; }
        public List<Produit> SearchSuggestions { get; private set; } = new();
        public ObservableCollection<CartItemViewModel> Panier { get; set; } = new();
        
        public ProductsViewModel ProductsVM { get; private set; }
        public StocksViewModel StocksVM { get; private set; }
        public HistoryViewModel HistoryVM { get; private set; }
        public SettingsViewModel SettingsVM { get; private set; }
        public DashboardViewModel DashboardVM { get; private set; }
        public PromotionsViewModel PromotionsVM { get; private set; }
        public InventoryViewModel InventoryVM { get; private set; }
        public AnalysisViewModel AnalysisVM { get; private set; }
        public CommandesViewModel CommandesVM { get; private set; }
        
        // Aliases for Customer Display Binding
        public ObservableCollection<CartItemViewModel> LignesVente => Panier;
        public decimal TotalVente => Total;
        
        private bool _showDisplayPromotions = true;
        public bool ShowDisplayPromotions
        {
            get => _showDisplayPromotions;
            set { _showDisplayPromotions = value; OnPropertyChanged(); OnPropertyChanged(nameof(CartColumnWidth)); }
        }

        public GridLength CartColumnWidth => ShowDisplayPromotions ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);

        private bool _showThankYouMessage;
        public bool ShowThankYouMessage
        {
            get => _showThankYouMessage;
            set { _showThankYouMessage = value; OnPropertyChanged(); }
        }

        private bool _isCompactCustomerDisplay;
        public bool IsCompactCustomerDisplay
        {
            get => _isCompactCustomerDisplay;
            set { _isCompactCustomerDisplay = value; OnPropertyChanged(); }
        }

        // Customer Display: promotions carousel
        public ObservableCollection<Promotion> DisplayPromotions { get; set; } = new();
        
        private Promotion? _currentDisplayPromotion;
        public Promotion? CurrentDisplayPromotion
        {
            get => _currentDisplayPromotion;
            set { _currentDisplayPromotion = value; OnPropertyChanged(); }
        }
        
        private int _currentPromoIndex;
        private System.Windows.Threading.DispatcherTimer? _promoCarouselTimer;

        public string CurrentDateTime => DateTime.Now.ToString("dddd dd MMMM yyyy HH:mm", new System.Globalization.CultureInfo("fr-FR"));
        public string CurrentDate => DateTime.Now.ToString("dd/MM/yyyy");
        public string CurrentTime => DateTime.Now.ToString("HH:mm:ss");

        public decimal TotalHorsRemise => Panier.Sum(x => x.TotalLigneStandard);
        public decimal TotalRemises => TotalRemise;
        public double TotalEuro => (double)TotalVente / 655.957;

        private System.Windows.Threading.DispatcherTimer? _clockTimer;
        private SystemeCaisse.UI.Views.CustomerDisplayWindow? _customerDisplay;
        private SerialScaleService? _scaleService;
        private VerifonePaymentTerminalService? _tpeService;

        /// <summary>Service balance RS-232 actif (peut être null si désactivé).</summary>
        public SerialScaleService? ScaleService => _scaleService;

        /// <summary>Service TPE Verifone actif (peut être null si désactivé).</summary>
        public VerifonePaymentTerminalService? TPEService => _tpeService;

        // === TPE Properties ===
        private bool _isTPEConnected;
        public bool IsTPEConnected
        {
            get => _isTPEConnected;
            set { _isTPEConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowTPEPayButton)); }
        }

        private bool _isTPEPaymentInProgress;
        public bool IsTPEPaymentInProgress
        {
            get => _isTPEPaymentInProgress;
            set { _isTPEPaymentInProgress = value; OnPropertyChanged(); }
        }

        private string _tpeStatusMessage = "";
        public string TPEStatusMessage
        {
            get => _tpeStatusMessage;
            set { _tpeStatusMessage = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Le bouton "Payer par TPE" est visible uniquement si :
        /// - Le mode de paiement est CB ou Mixte
        /// - Un TPE est connecté
        /// </summary>
        public bool ShowTPEPayButton => IsTPEConnected && (SelectedPaiementMode == "CB" || SelectedPaiementMode == "Mixte");

        private Produit? _selectedSearchProduct;
        public Produit? SelectedSearchProduct
        {
            get => _selectedSearchProduct;
            set
            {
                if (_selectedSearchProduct != value)
                {
                    _selectedSearchProduct = value;
                    OnPropertyChanged();
                    if (_selectedSearchProduct != null)
                    {
                        AddToCart((object)_selectedSearchProduct);
                        // Clear search after selection
                        _selectedSearchProduct = null;
                        OnPropertyChanged(nameof(SelectedSearchProduct));
                        SearchText = string.Empty;
                    }
                }
            }
        }

        private bool _isSearchDropDownOpen;
        public bool IsSearchDropDownOpen
        {
            get => _isSearchDropDownOpen;
            set => SetProperty(ref _isSearchDropDownOpen, value);
        }

        private string? _searchText;
        public string? SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();

                    if (!string.IsNullOrWhiteSpace(_searchText))
                    {
                        string search = _searchText.Trim();
                        bool isNumeric = search.All(char.IsDigit);

                        // Only open dropdown for text searches, not numeric barcodes
                        if (!isNumeric)
                        {
                            UpdateSearchSuggestions(search);
                        }
                        else
                        {
                            IsSearchDropDownOpen = false;
                            if (SearchSuggestions.Count > 0)
                            {
                                SearchSuggestions = new List<Produit>();
                                OnPropertyChanged(nameof(SearchSuggestions));
                            }
                        }
                    }
                    else
                    {
                        IsSearchDropDownOpen = false;
                        if (SearchSuggestions.Count > 0)
                        {
                            SearchSuggestions = new List<Produit>();
                            OnPropertyChanged(nameof(SearchSuggestions));
                        }
                    }
                }
            }
        }

        private void UpdateSearchSuggestions(string search)
        {
            if (Produits == null || string.IsNullOrWhiteSpace(search))
            {
                IsSearchDropDownOpen = false;
                if (SearchSuggestions.Count > 0)
                {
                    SearchSuggestions = new List<Produit>();
                    OnPropertyChanged(nameof(SearchSuggestions));
                }
                return;
            }

            var startsWithMatches = new List<Produit>(35);
            var containsMatches = new List<Produit>(35);

            foreach (var p in Produits)
            {
                if (!p.Actif) continue;

                bool nameStarts = p.Nom != null && p.Nom.StartsWith(search, StringComparison.OrdinalIgnoreCase);
                if (nameStarts)
                {
                    startsWithMatches.Add(p);
                    if (startsWithMatches.Count >= 35) break;
                    continue;
                }

                bool nameContains = p.Nom != null && p.Nom.Contains(search, StringComparison.OrdinalIgnoreCase);
                bool codeContains = p.CodeBarre != null && p.CodeBarre.Contains(search, StringComparison.OrdinalIgnoreCase);
                if (nameContains || codeContains)
                {
                    containsMatches.Add(p);
                }
            }

            var results = new List<Produit>(35);
            results.AddRange(startsWithMatches);
            if (results.Count < 35 && containsMatches.Count > 0)
            {
                int remaining = 35 - results.Count;
                for (int i = 0; i < containsMatches.Count && i < remaining; i++)
                {
                    results.Add(containsMatches[i]);
                }
            }

            SearchSuggestions = results;
            OnPropertyChanged(nameof(SearchSuggestions));
            IsSearchDropDownOpen = results.Count > 0;
        }

        private decimal _total;
        public decimal Total
        {
            get => _total;
            set
            {
                _total = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalVente));
                RecalculateMonnaie();
            }
        }

        private decimal _totalRemise;
        public decimal TotalRemise
        {
            get => _totalRemise;
            set
            {
                _totalRemise = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDiscount));
            }
        }

        public decimal TotalSansRemise => Panier?.Sum(i => i.TotalLigneStandard) ?? 0;

        public bool HasDiscount => TotalRemise > 0;

        private bool _isReturnMode;
        public bool IsReturnMode
        {
             get => _isReturnMode;
             set
             {
                 _isReturnMode = value;
                 OnPropertyChanged();
                 StatusMessage = value ? "Mode RETOUR activé" : "Prêt";
             }
        }

        private CartItemViewModel? _selectedCartItem;
        public CartItemViewModel? SelectedCartItem
        {
            get => _selectedCartItem;
            set { _selectedCartItem = value; OnPropertyChanged(); }
        }

        [ObservableProperty]
        private ObservableCollection<Promotion> _availablePromotions = new();

        [ObservableProperty]
        private decimal _basketRemiseManuelle;

        // Payment Properties
        private string _selectedPaiementMode = "CB";
        public string SelectedPaiementMode
        {
            get => _selectedPaiementMode;
            set
            {
                _selectedPaiementMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowTPEPayButton));
                RecalculateMonnaie();
            }
        }

        private decimal _montantRecu;
        public decimal MontantRecu
        {
            get => _montantRecu;
            set
            {
                _montantRecu = value;
                OnPropertyChanged();
                RecalculateMonnaie();
            }
        }

        private decimal _monnaieRendre;
        public decimal MonnaieRendre
        {
            get => _monnaieRendre;
            private set
            {
                _monnaieRendre = value;
                OnPropertyChanged();
            }
        }

        private decimal _montantCarte;
        public decimal MontantCarte
        {
            get => _montantCarte;
            private set
            {
                _montantCarte = value;
                OnPropertyChanged();
            }
        }

        private void RecalculateMonnaie()
        {
            if (SelectedPaiementMode == "Especes")
            {
                MonnaieRendre = Math.Max(0, MontantRecu - Total);
                MontantCarte = 0;
            }
            else if (SelectedPaiementMode == "Mixte" || SelectedPaiementMode == "Espece/CB")
            {
                MonnaieRendre = 0;
                MontantCarte = Math.Max(0, Total - MontantRecu);
            }
            else if (SelectedPaiementMode == "Wero" || SelectedPaiementMode == "En ligne")
            {
                MonnaieRendre = 0;
                MontantCarte = 0;
            }
            else // CB
            {
                MonnaieRendre = 0;
                MontantCarte = Total;
            }
        }

        private string _statusMessage = "Prêt";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // === Network Database Mode Properties ===
        private bool _isNetworkMode;
        public bool IsNetworkMode
        {
            get => _isNetworkMode;
            set { _isNetworkMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(DatabaseModeDisplay)); OnPropertyChanged(nameof(DatabaseModeColor)); }
        }

        private bool _isNetworkDisconnected;
        public bool IsNetworkDisconnected
        {
            get => _isNetworkDisconnected;
            set { _isNetworkDisconnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(DatabaseModeDisplay)); OnPropertyChanged(nameof(DatabaseModeColor)); }
        }

        private string _databasePath = "";
        public string DatabasePath
        {
            get => _databasePath;
            set { _databasePath = value; OnPropertyChanged(); }
        }

        private string? _customDatabaseModeText;
        public string DatabaseModeDisplay
        {
            get
            {
                if (!string.IsNullOrEmpty(_customDatabaseModeText)) return _customDatabaseModeText;
                return IsNetworkDisconnected ? "⚠ Réseau perdu" : (IsNetworkMode ? "🟢 Réseau" : "🔴 Local");
            }
        }

        public string DatabaseModeColor => IsNetworkDisconnected ? "#E65100" : (IsNetworkMode ? "#2E7D32" : "#C62828");

        private void RefreshPostgresStatusBanner()
        {
            if (_caisseServices.Factory.Provider != DatabaseProvider.PostgreSql) return;

            var poste = _caisseServices.Poste;
            var db = poste.Database;
            bool online = _caisseServices.IsOnline;
            int queued = _caisseServices.OfflineQueue.Count;
            bool isPrincipal = poste.EstPostePrincipal ||
                               string.Equals(db.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(db.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);

            IsNetworkMode = true;
            IsNetworkDisconnected = !online;
            DatabasePath = $"PostgreSQL {db.Host}:{db.Port}/{db.Database} (Poste : {poste.NomPoste})";

            if (!online)
            {
                _customDatabaseModeText = queued > 0
                    ? $"⚠ Hors-ligne ({queued} vente(s) en attente)"
                    : "⚠ Serveur PostgreSQL injoignable";
            }
            else if (isPrincipal)
            {
                var ts = PostgresProvisioningService.GetNetworkAndTailscaleStatus();
                _customDatabaseModeText = !string.IsNullOrEmpty(ts.TailscaleIp)
                    ? $"🟢 Serveur Principal ({ts.TailscaleIp}:{db.Port})"
                    : $"🟢 Serveur Principal (localhost:{db.Port})";
            }
            else
            {
                _customDatabaseModeText = $"🟢 Connecté ({db.Host}:{db.Port})";
            }

            OnPropertyChanged(nameof(DatabaseModeDisplay));
            OnPropertyChanged(nameof(DatabaseModeColor));
        }

        private Entreprise? _currentEntreprise;
        public Entreprise? CurrentEntreprise
        {
            get => _currentEntreprise;
            set
            {
                _currentEntreprise = value;
                OnPropertyChanged();
            }
        }

        private void LoadEntrepriseInfo()
        {
            try
            {
                _context.Entreprise.Load();
                CurrentEntreprise = _context.Entreprise.Local.FirstOrDefault();
            }
            catch { }
            
            if (CurrentEntreprise == null)
            {
                CurrentEntreprise = new Entreprise { Nom = "Mon Magasin", Adresse = "Adresse inconnue" };
            }
        }

        public void ReloadEntrepriseInfo()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var entreprise = context.Entreprise.FirstOrDefault();
                if (entreprise != null)
                {
                    CurrentEntreprise = entreprise;
                    OnPropertyChanged(nameof(CurrentEntreprise));
                    
                    InitializeCustomerDisplay();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RELOAD ETP ERROR: {ex.Message}");
            }
        }

        public ICommand AddToCartCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand CheckoutCommand { get; }
        public ICommand PaymentModeCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand OpenWeightDialogCommand { get; }
        public ICommand OpenDiscountDialogCommand { get; }
        public ICommand CalculateProductPopularityCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ViderPanierCommand { get; }
        public ICommand SendToTPECommand { get; }
        public ICommand FillExactAmountCommand { get; }

        private async Task ReloadProductsFromDbAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    _context.ChangeTracker.Clear();
                    _context.Produits.Load();
                });

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Produits = _context.Produits.Local.ToObservableCollection();
                    ProductsView = CollectionViewSource.GetDefaultView(Produits);

                    OnPropertyChanged(nameof(Produits));
                    OnPropertyChanged(nameof(ProductsView));
                });

                await CalculateProductPopularity();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reload Error: {ex.Message}");
            }
        }

        public async Task ReloadAllDataAfterRestoreAsync()
        {
            try
            {
                await new SystemeCaisse.Infrastructure.Services.DatabaseBootstrapper(_caisseServices.Factory).InitializeAsync(backupBeforeMigration: false);
                _caisseServices.Popularity.Invalidate();
                _caisseCatalogDirty = false;
                _caissePromosDirty = false;

                await ReloadProductsFromDbAsync();
                ReloadEntrepriseInfo();
                LoadPromotions();
                ApplyAutomaticPromotions();
                UpdateTotal();

                _ = SafeInitAsync("ProductsVM", () => ProductsVM.InitializeAsync());
                _ = SafeInitAsync("StocksVM", () => StocksVM.InitializeAsync());
                _ = SafeInitAsync("HistoryVM", () => HistoryVM.InitializeAsync());
                _ = SafeInitAsync("SettingsVM", () => SettingsVM.InitializeAsync());
                _ = SafeInitAsync("DashboardVM", () => DashboardVM.InitializeAsync());
                _ = SafeInitAsync("PromotionsVM", () => PromotionsVM.InitializeAsync());
                _ = SafeInitAsync("InventoryVM", () => InventoryVM.InitializeAsync());
                _ = SafeInitAsync("AnalysisVM", () => AnalysisVM.InitializeAsync());
                _ = SafeInitAsync("CommandesVM", () => CommandesVM.InitializeAsync());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReloadAllDataAfterRestoreAsync Error: {ex.Message}");
            }
        }

        private void ReloadProductsFromDb()
        {
            _ = ReloadProductsFromDbAsync();
        }

        private int _selectedTabIndex = 1;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    OnPropertyChanged(nameof(SelectedTabIndex));
                    OnSelectedTabIndexChanged(value);
                }
            }
        }

        private int _lastIndex = 1; // Start at Caisse
        private void OnSelectedTabIndexChanged(int value)
        {
            int previousIndex = _lastIndex;
            if (previousIndex != value)
            {
                _lastIndex = value;

                if (value == 0)
                {
                    _ = DashboardVM?.LoadDashboardDataInternalAsync();
                }
                else if (value == 1)
                {
                    if (_caisseCatalogDirty)
                    {
                        _caisseCatalogDirty = false;
                        _ = ReloadProductsFromDbAsync();
                    }

                    if (_caissePromosDirty)
                    {
                        _caissePromosDirty = false;
                        _ = Task.Run(() =>
                        {
                            LoadPromotions();
                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                ApplyAutomaticPromotions();
                                UpdateTotal();
                            }));
                        });
                    }
                    else if (Panier.Count > 0)
                    {
                        ApplyAutomaticPromotions();
                        UpdateTotal();
                    }
                }
                else if (value == 2)
                {
                    _ = CommandesVM?.RefreshIfNeededAsync();
                }
                else if (value == 3)
                {
                    _ = ProductsVM?.RefreshIfIdleAsync();
                }
                else if (value == 4)
                {
                    _ = StocksVM?.LoadDataAsync();
                }
                else if (value == 5)
                {
                    _ = HistoryVM?.LoadDataAsync();
                }
            }
        }

        public MainViewModel(IDbContextFactory<AppDbContext> contextFactory, PrintService printService, IDataMigrationService migrationService, SystemeCaisse.UI.Services.CaisseServices caisseServices)
        {
            _context = contextFactory.CreateDbContext();
            _contextFactory = contextFactory;
            _printService = printService;
            _migrationService = migrationService;
            _caisseServices = caisseServices;
            _caisseServices.Versions.Changed += OnDataVersionsChanged;
            _caisseServices.Images.ImagesUpdated += () =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    ProductsVM?.RefreshImages();
                    ProductsView?.Refresh();
                }));
            };
            
            AvailablePromotions = new ObservableCollection<Promotion>();
            Panier = new ObservableCollection<CartItemViewModel>();
            TopProducts = new ObservableCollection<Produit>();
            AddToCartCommand = new BasicRelayCommand(AddToCart);

            // Initialize Child ViewModels (instantiation only, no DB load)
            ProductsVM = new ProductsViewModel(caisseServices);
            StocksVM = new StocksViewModel(caisseServices);
            HistoryVM = new HistoryViewModel(caisseServices, _printService);
            SettingsVM = new SettingsViewModel(contextFactory, migrationService, caisseServices);
            DashboardVM = new DashboardViewModel(contextFactory);
            PromotionsVM = new PromotionsViewModel(contextFactory);
            InventoryVM = new InventoryViewModel(contextFactory);
            AnalysisVM = new AnalysisViewModel(contextFactory);
            CommandesVM = new CommandesViewModel(caisseServices, _printService);
            CommandesVM.SetMainViewModel(this);
            RemoveItemCommand = new BasicRelayCommand(RemoveItem);
            CheckoutCommand = new BasicRelayCommand(Checkout, _ => Panier.Count > 0);
            PaymentModeCommand = new BasicRelayCommand(SetPaymentMode);
            CancelCommand = new BasicRelayCommand(_ => ResetSale());
            ViderPanierCommand = new BasicRelayCommand(_ => ResetSale());
            ScanCommand = new BasicRelayCommand(_ => HandleScan());
            EditQuantityCommand = new BasicRelayCommand(EditQuantity);
            CalculateProductPopularityCommand = new BasicRelayCommand(async _ => await CalculateProductPopularity());
            ClearSearchCommand = new BasicRelayCommand(_ => SearchText = string.Empty);
            SendToTPECommand = new BasicRelayCommand(async _ => await SendToTPE(), _ => Panier.Count > 0 && IsTPEConnected && !IsTPEPaymentInProgress);
            FillExactAmountCommand = new BasicRelayCommand(_ => MontantRecu = Total);
            
            OpenWeightDialogCommand = new BasicRelayCommand(_ => 
            {
                var weightProducts = Produits.Where(p => p.Actif && string.Equals(p.TypeVente, "poids", StringComparison.OrdinalIgnoreCase)).ToList();
                var selectDialog = new SystemeCaisse.UI.Views.ProductSelectionWindow(weightProducts);
                SetupWindowOwner(selectDialog);
                if (selectDialog.ShowDialog() == true && selectDialog.SelectedProduct != null)
                {
                    AddToCart((object)selectDialog.SelectedProduct);
                }
            });

            OpenDiscountDialogCommand = new BasicRelayCommand(_ => ApplyManualDiscount());
            
            SuspendSaleCommand = new BasicRelayCommand(SuspendSale, _ => Panier.Count > 0);
            ResumeSaleCommand = new BasicRelayCommand(ResumeSale);
            SuspendedSales = new ObservableCollection<SuspendedSale>();

            Panier.CollectionChanged += (s, e) => 
            {
                if (e.NewItems != null)
                {
                    foreach (CartItemViewModel item in e.NewItems)
                    {
                        item.PropertyChanged += (sender, args) => 
                        {
                            if (args.PropertyName == nameof(CartItemViewModel.Quantite))
                            {
                                ApplyAutomaticPromotions();
                                UpdateTotal();
                            }
                        };
                    }
                }
                
                ApplyAutomaticPromotions();
                UpdateTotal();
            };

            UpdateTotal();

            // Start Live Clock Timer
            _clockTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => { OnPropertyChanged(nameof(CurrentDateTime)); OnPropertyChanged(nameof(CurrentDate)); OnPropertyChanged(nameof(CurrentTime)); };
            _clockTimer.Start();
        }

        public async Task InitializeAsync()
        {
            // 1. Charger uniquement les données essentielles à l'ouverture immédiate de la Caisse
            await Task.Run(async () => 
            {
                try 
                {
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] _context.Produits.Load()\n");
                    _context.Produits.Load();
                    
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Dispatcher.Invoke (Produits list)\n");
                    await Application.Current.Dispatcher.InvokeAsync(() => 
                    {
                        Produits = _context.Produits.Local.ToObservableCollection();
                        ProductsView = CollectionViewSource.GetDefaultView(Produits);
                    });

                    LoadEntrepriseInfo();
                    LoadPromotions();
                    await CalculateProductPopularity();

                    // Sauvegarder un instantané local du catalogue pour le mode hors-ligne
                    try
                    {
                        _caisseServices.Cache.Save(new CatalogSnapshot
                        {
                            SavedAt = DateTime.Now,
                            Produits = Produits.ToList(),
                            Promotions = _activePromotions.ToList(),
                            Entreprise = CurrentEntreprise
                        });
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] MAIN INIT ERROR (repli cache local): {ex.Message}\n");
                    var cached = _caisseServices.Cache.Load();
                    if (cached != null)
                    {
                        _activePromotions = cached.Promotions ?? new List<Promotion>();
                        CurrentEntreprise = cached.Entreprise ?? new Entreprise { Nom = "Mon Magasin" };
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Produits = new ObservableCollection<Produit>(cached.Produits);
                            ProductsView = CollectionViewSource.GetDefaultView(Produits);
                            TopProducts.Clear();
                            foreach (var p in cached.Produits.Where(x => x.Actif).Take(20))
                                TopProducts.Add(p);
                        });
                    }
                }
            });

            // 2. Initialize Network / PostgreSQL Multi-Postes monitoring
            try
            {
                if (_caisseServices.Factory.Provider == DatabaseProvider.PostgreSql)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        RefreshPostgresStatusBanner();
                        if (!_caisseServices.IsOnline)
                        {
                            StatusMessage = "⚠ Serveur PostgreSQL injoignable — Mode Caisse hors-ligne actif (synchronisation auto).";
                        }
                    });

                    _caisseServices.Connectivity.StatusChanged += (online, error) =>
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            RefreshPostgresStatusBanner();
                            if (!online)
                            {
                                StatusMessage = "⚠ Connexion au Poste Principal perdue — Les ventes sont sauvegardées localement.";
                            }
                            else
                            {
                                StatusMessage = "✅ Connexion PostgreSQL rétablie — Synchronisation automatique effectuée.";
                                if (Panier.Count == 0)
                                    _ = ReloadProductsFromDbAsync();
                                else
                                    _caisseCatalogDirty = true;
                            }
                        }));
                    };

                    _caisseServices.OfflineQueue.CountChanged += count =>
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(RefreshPostgresStatusBanner));
                    };
                }
                else
                {
                    var networkService = NetworkDatabaseService.Instance;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsNetworkMode = networkService.IsNetworkMode;
                        DatabasePath = networkService.CurrentDbPath;

                        networkService.NetworkStatusChanged += isConnected =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                IsNetworkDisconnected = !isConnected;
                                if (!isConnected)
                                {
                                    StatusMessage = "⚠ Connexion réseau perdue ! Les données ne sont plus synchronisées.";
                                }
                                else
                                {
                                    StatusMessage = "✅ Connexion réseau rétablie.";
                                    IsNetworkDisconnected = false;
                                }
                            });
                        };

                        networkService.StartPeriodicCheck();
                    });
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] NetworkDB init error: {ex.Message}\n");
            }

            // 3. Initialiser les onglets secondaires et périphériques en tâche de fond sans bloquer l'affichage de la fenêtre
            _ = Task.Run(async () =>
            {
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Sub-VM execution start\n");

                await SafeInitAsync("SettingsVM", () => SettingsVM.InitializeAsync());
                await SafeInitAsync("CommandesVM", () => CommandesVM.InitializeAsync());
                await SafeInitAsync("ProductsVM", () => ProductsVM.InitializeAsync());
                await SafeInitAsync("StocksVM", () => StocksVM.InitializeAsync());
                await SafeInitAsync("HistoryVM", () => HistoryVM.InitializeAsync());
                await SafeInitAsync("DashboardVM", () => DashboardVM.InitializeAsync());
                await SafeInitAsync("PromotionsVM", () => PromotionsVM.InitializeAsync());
                await SafeInitAsync("InventoryVM", () => InventoryVM.InitializeAsync());
                await SafeInitAsync("AnalysisVM", () => AnalysisVM.InitializeAsync());

                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Sub-VM execution end\n");

                // Initialize Scale Service
                try
                {
                    using var scaleCtx = _contextFactory.CreateDbContext();
                    var scaleEnabled = scaleCtx.Configuration.Find("scale_enabled");
                    if (scaleEnabled != null && bool.TryParse(scaleEnabled.Valeur, out bool se) && se)
                    {
                        var scalePort = scaleCtx.Configuration.Find("scale_port_name");
                        var scaleBaud = scaleCtx.Configuration.Find("scale_baud_rate");
                        string portName = scalePort?.Valeur ?? "COM3";
                        int baudRate = 9600;
                        if (scaleBaud != null) int.TryParse(scaleBaud.Valeur, out baudRate);

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                _scaleService = new SerialScaleService();
                                _scaleService.Start(portName, baudRate);
                                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Scale connected on {portName} at {baudRate}\n");
                            }
                            catch (Exception ex)
                            {
                                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Scale FAILED: {ex.Message}\n");
                                _scaleService = null;
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Scale init error: {ex.Message}\n");
                }

                // Initialize TPE Service
                try
                {
                    using var tpeCtx = _contextFactory.CreateDbContext();
                    var tpeEnabled = tpeCtx.Configuration.Find("tpe_enabled");
                    if (tpeEnabled != null && bool.TryParse(tpeEnabled.Valeur, out bool te) && te)
                    {
                        var tpePort = tpeCtx.Configuration.Find("tpe_port_name");
                        var tpeBaud = tpeCtx.Configuration.Find("tpe_baud_rate");
                        var tpeTimeout = tpeCtx.Configuration.Find("tpe_timeout");
                        string portName = tpePort?.Valeur ?? "COM1";
                        int baudRate = 9600;
                        if (tpeBaud != null) int.TryParse(tpeBaud.Valeur, out baudRate);
                        int timeout = 60;
                        if (tpeTimeout != null) int.TryParse(tpeTimeout.Valeur, out timeout);

                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            try
                            {
                                _tpeService = new VerifonePaymentTerminalService();
                                _tpeService.TransactionTimeoutSeconds = timeout;
                                _tpeService.StatusChanged += msg =>
                                {
                                    Application.Current.Dispatcher.Invoke(() => TPEStatusMessage = msg);
                                };
                                bool connected = await _tpeService.InitializeAsync(portName, baudRate);
                                IsTPEConnected = connected;
                                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] TPE init: {(connected ? "connected" : "failed")} on {portName} at {baudRate}\n");
                            }
                            catch (Exception ex)
                            {
                                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] TPE FAILED: {ex.Message}\n");
                                _tpeService = null;
                                IsTPEConnected = false;
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] TPE init error: {ex.Message}\n");
                }
            });
        }

        private async Task SafeInitAsync(string name, Func<Task> init)
        {
            try
            {
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] {name}.Init start\n");
                await init();
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] {name}.Init FAILED: {ex.Message}\nStack: {ex.StackTrace}\n");
                System.Diagnostics.Debug.WriteLine($"SILENT STABILITY: {name} init failed: {ex.Message}");
            }
        }

        public void InitializeCustomerDisplay()
        {
            if (_customerDisplay != null)
            {
                try { _customerDisplay.Close(); } catch { }
                _customerDisplay = null;
            }

            try
            {
                using var context = _contextFactory.CreateDbContext();
                
                var enabledConfig = context.Configuration.Find("customer_display_enabled");
                bool isEnabled = enabledConfig == null || (bool.TryParse(enabledConfig.Valeur, out bool e) && e);
                
                if (!isEnabled)
                {
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] CustomerDisplay: DISABLED by config.\n");
                    return;
                }

                var cdPromo = context.Configuration.Find("customer_display_show_promotions");
                ShowDisplayPromotions = cdPromo == null || (bool.TryParse(cdPromo.Valeur, out bool p) && p);

                var cdCompact = context.Configuration.Find("customer_display_compact");
                IsCompactCustomerDisplay = cdCompact != null && bool.TryParse(cdCompact.Valeur, out bool cp) && cp;

                LoadDisplayPromotions(context);

                if (ShowDisplayPromotions && DisplayPromotions.Count > 0)
                {
                    StartPromoCarousel();
                }

                var screenConfig = context.Configuration.Find("customer_display_screen_index");
                int savedScreenIndex = (screenConfig != null && int.TryParse(screenConfig.Valeur, out int si)) ? si : -1;
                
                var screens = ScreenHelper.GetScreens();
                
                string screenLog = $"[{DateTime.Now}] Screen Discovery: Found {screens.Count} screens.\n";
                for (int i = 0; i < screens.Count; i++)
                {
                    var s = screens[i];
                    screenLog += $"  - Screen {i}: Primary={s.IsPrimary}, Bounds={s.Bounds.Width}x{s.Bounds.Height}, LogBounds={s.LogicalBounds.Width}x{s.LogicalBounds.Height}\n";
                }
                System.IO.File.AppendAllText("startup_log_v2.txt", screenLog);

                if (screens.Count < 2)
                {
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] CustomerDisplay: Only {screens.Count} screen(s) detected — need 2+ for client display. Skipping.\n");
                    return; 
                }

                ScreenHelper.ScreenInfo? targetScreen = null;
                if (savedScreenIndex >= 0 && savedScreenIndex < screens.Count)
                {
                    targetScreen = screens[savedScreenIndex];
                }
                else
                {
                    targetScreen = screens.FirstOrDefault(s => !s.IsPrimary) ?? (screens.Count > 1 ? screens[1] : null);
                }
                
                if (targetScreen == null)
                {
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] CustomerDisplay: No valid target screen found. Skipping.\n");
                    return;
                }

                var adminScreen = screens.FirstOrDefault(s => s != targetScreen) ?? screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];

                _customerDisplay = new SystemeCaisse.UI.Views.CustomerDisplayWindow(this);

                _customerDisplay.WindowStartupLocation = WindowStartupLocation.Manual;
                _customerDisplay.Topmost = true;
                _customerDisplay.WindowState = WindowState.Normal;
                _customerDisplay.WindowStyle = WindowStyle.None;

                _customerDisplay.Left = targetScreen.LogicalBounds.Left;
                _customerDisplay.Top = targetScreen.LogicalBounds.Top;
                _customerDisplay.Width = targetScreen.LogicalBounds.Width;
                _customerDisplay.Height = targetScreen.LogicalBounds.Height;
                
                _customerDisplay.Show();
                
                _customerDisplay.Left = targetScreen.LogicalBounds.Left;
                _customerDisplay.Top = targetScreen.LogicalBounds.Top;
                _customerDisplay.WindowState = WindowState.Maximized;

                if (adminScreen != null)
                {
                    MoveAdminToScreen(adminScreen);
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] CustomerDisplay ERROR: {ex.Message}\nStack: {ex.StackTrace}\n");
                System.Diagnostics.Debug.WriteLine($"Customer Display Error: {ex.Message}");
            }
        }

        private void MoveAdminToScreen(ScreenHelper.ScreenInfo adminScreen)
        {
            var mainWin = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is SystemeCaisse.UI.MainWindow);
            if (mainWin == null) return;

            bool wasMaximized = mainWin.WindowState == WindowState.Maximized;
            if (wasMaximized) mainWin.WindowState = WindowState.Normal;

            mainWin.Left = adminScreen.LogicalWorkingArea.Left + 50; 
            mainWin.Top = adminScreen.LogicalWorkingArea.Top + 50;

            if (wasMaximized)
            {
                mainWin.Left = adminScreen.LogicalWorkingArea.Left;
                mainWin.Top = adminScreen.LogicalWorkingArea.Top;
                mainWin.Width = adminScreen.LogicalWorkingArea.Width;
                mainWin.Height = adminScreen.LogicalWorkingArea.Height;
                mainWin.WindowState = WindowState.Maximized;
            }
        }

        public void SetupWindowOwner(Window win)
        {
            if (win == null) return;
            var mainWin = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is SystemeCaisse.UI.MainWindow);
            
            if (mainWin != null && mainWin != win)
            {
                win.Owner = mainWin;
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                
                var screens = ScreenHelper.GetScreens();
                var adminScreen = screens.FirstOrDefault(s => 
                    s.LogicalWorkingArea.Contains(new Point(mainWin.Left + 10, mainWin.Top + 10))) ?? screens[0];
                
                win.SourceInitialized += (s, e) => 
                {
                    double left = adminScreen.LogicalWorkingArea.Left + (adminScreen.LogicalWorkingArea.Width - win.ActualWidth) / 2;
                    double top = adminScreen.LogicalWorkingArea.Top + (adminScreen.LogicalWorkingArea.Height - win.ActualHeight) / 2;
                    
                    win.Left = left;
                    win.Top = top;
                };
            }
            else
            {
                win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void SetPaymentMode(object parameter)
        {
            if (parameter is string mode)
            {
                SelectedPaiementMode = mode;
            }
        }

        private bool FilterProducts(object obj)
        {
            if (obj is Produit produit)
            {
                if (!produit.Actif) return false;
                if (string.IsNullOrWhiteSpace(SearchText)) return true;
                
                var search = SearchText.Trim();
                return (produit.Nom?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (produit.CodeBarre?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (produit.Id.ToString() == search);
            }
            return false;
        }

        private async Task CalculateProductPopularity()
        {
            try
            {
                var popularity = await _caisseServices.Popularity.GetAsync();

                int topLimit = 20;
                bool showAll = false;
                try
                {
                    using var context = _contextFactory.CreateDbContext();
                    var countConfig = context.Configuration.Find("top_products_count");
                    if (countConfig != null && int.TryParse(countConfig.Valeur, out int tc)) topLimit = tc;
                    
                    var showAllConfig = context.Configuration.Find("show_all_top_products");
                    showAll = (showAllConfig != null && bool.TryParse(showAllConfig.Valeur, out bool sa)) && sa;
                }
                catch { }

                var produitsSnapshot = Produits?.ToList() ?? new List<Produit>();
                foreach (var p in produitsSnapshot)
                {
                    p.ValidatedSalesCount = popularity.Get(p.Id, p.Nom);
                }

                var top20Items = produitsSnapshot
                    .Where(p => p.Actif && p.ValidatedSalesCount > 0)
                    .OrderByDescending(p => p.ValidatedSalesCount)
                    .ThenBy(p => p.Nom)
                    .Take(showAll ? int.MaxValue : topLimit)
                    .ToList();

                if (top20Items.Count == 0)
                {
                    top20Items = produitsSnapshot
                        .Where(p => p.Actif)
                        .OrderBy(p => p.Nom)
                        .Take(showAll ? int.MaxValue : topLimit)
                        .ToList();
                }

                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => 
                {
                    bool sameTop = TopProducts.Count == top20Items.Count &&
                                   TopProducts.Zip(top20Items, (a, b) => a.Id == b.Id).All(x => x);
                    if (!sameTop)
                    {
                        TopProducts.Clear();
                        foreach (var p in top20Items) 
                        {
                            if (p != null) TopProducts.Add(p);
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex) 
            { 
                 System.Diagnostics.Debug.WriteLine($"SILENT STABILITY: Sort Error: {ex.Message}");
            }
        }

        public ICommand ScanCommand { get; }
        public ICommand EditQuantityCommand { get; }
        public ICommand SuspendSaleCommand { get; }
        public ICommand ResumeSaleCommand { get; }
        
        public ObservableCollection<SuspendedSale> SuspendedSales { get; set; }

        private void SuspendSale(object parameter)
        {
            if (Panier.Count == 0) return;

            var sale = new SuspendedSale
            {
                Id = Guid.NewGuid(),
                Date = DateTime.Now,
                Items = new List<CartItemViewModel>(Panier),
                BasketRemiseManuelle = BasketRemiseManuelle,
                Total = Total,
                Label = $"Panier du {DateTime.Now:HH:mm} ({Panier.Count} art. - {Total:C})"
            };

            SuspendedSales.Add(sale);
            ResetSale();
            MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Vente mise en attente.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ResumeSale(object parameter)
        {
            if (parameter is SuspendedSale sale)
            {
                if (Panier.Count > 0)
                {
                    if (MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Un panier est en cours. L'écraser ?", "Attention", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                        return;
                }

                Panier.Clear();
                foreach (var item in sale.Items)
                {
                    Panier.Add(item);
                }
                
                BasketRemiseManuelle = sale.BasketRemiseManuelle;
                SuspendedSales.Remove(sale);
                UpdateTotal();
            }
        }

        private async void HandleScan()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            var code = SearchText.Trim();
            var product = Produits.FirstOrDefault(p => p.Actif && p.CodeBarre == code);
            
            if (product == null)
            {
                if (int.TryParse(code, out int id) && id < 10000)
                     product = Produits.FirstOrDefault(p => p.Actif && p.Id == id);
            }

            if (product != null)
            {
                AddToCart((object)product);
                SearchText = string.Empty;
                IsSearchDropDownOpen = false;
                if (SearchSuggestions.Count > 0)
                {
                    SearchSuggestions = new List<Produit>();
                    OnPropertyChanged(nameof(SearchSuggestions));
                }
            }
            else
            {
                var dialog = new SystemeCaisse.UI.Views.ProductNotFoundWindow(code);
                SetupWindowOwner(dialog);
                if (dialog.ShowDialog() == true && dialog.AddRequested)
                {
                    var categories = Produits
                        .Select(p => p.Categorie)
                        .Where(c => !string.IsNullOrEmpty(c))
                        .Distinct()
                        .OrderBy(c => c)
                        .Cast<string>()
                        .ToList();

                    var addDialog = new SystemeCaisse.UI.Views.QuickAddProductWindow(code, categories);
                    SetupWindowOwner(addDialog);
                    if (addDialog.ShowDialog() == true && addDialog.NewProduct != null)
                    {
                        var newProd = addDialog.NewProduct;

                        _context.Produits.Add(newProd);
                        await _context.SaveChangesAsync();

                        Produits.Add(newProd);

                        _ = ProductsVM.LoadDataCommand.ExecuteAsync(null);
                        _ = StocksVM.LoadDataAsync();
                        _ = InventoryVM.LoadHistoryAsync();

                        AddToCart((object)newProd);
                    }
                }
                
                SearchText = string.Empty;
                IsSearchDropDownOpen = false;
                if (SearchSuggestions.Count > 0)
                {
                    SearchSuggestions = new List<Produit>();
                    OnPropertyChanged(nameof(SearchSuggestions));
                }
            }
        }
        
        private void EditQuantity(object parameter)
        {
            if (parameter is CartItemViewModel item)
            {
                var dialog = new SystemeCaisse.UI.Views.QuantityInputWindow(item.Quantite);
                SetupWindowOwner(dialog);
                if (dialog.ShowDialog() == true)
                {
                    item.Quantite = dialog.Quantity;
                    if (item.Quantite <= 0) Panier.Remove(item);
                    UpdateTotal();
                }
            }
        }

        private void RemoveItem(object parameter)
        {
            if (parameter is CartItemViewModel item)
            {
                Panier.Remove(item);
                UpdateTotal();
            }
        }

        private async void Checkout(object parameter)
        {
            if (Panier.Count == 0) return;

            try
            {
                var now = DateTime.Now;
                var poste = _caisseServices.Poste;
                var vente = new Vente
                {
                    CreatedAt = now,
                    ClientUid = Guid.NewGuid().ToString("D"),
                    Poste = poste.NomPoste,
                    Total = Total,
                    TotalRemise = TotalRemise,
                    NbArticles = (int)Panier.Sum(i => i.Quantite),
                    NumeroTicket = SalesService.GenerateTicketNumber(poste.CodePoste, now),
                    MoyenPaiement = SelectedPaiementMode,
                    MontantEspeces = SelectedPaiementMode == "Especes" ? MontantRecu : (SelectedPaiementMode == "Mixte" ? MontantRecu : 0),
                    MontantCB = SelectedPaiementMode == "CB" ? Total : (SelectedPaiementMode == "Mixte" ? MontantCarte : 0),
                    MonnaieRendue = MonnaieRendre,
                    Statut = "validee"
                };

                if (SelectedPaiementMode == "Especes" && Total > 0 && Math.Round(MontantRecu, 2) < Math.Round(Total, 2))
                {
                    MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Montant reçu insuffisant !", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (SettingsVM.IsTrainingMode)
                {
                    if (MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "MODE FORMATION ACTIVE.\nLa vente ne sera pas enregistrée.\nContinuer ?", "Mode Formation", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                        return;

                    if (MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Simuler l'impression du ticket ?", "Impression Formation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _printService.PrintTicket(vente, CurrentEntreprise ?? new Entreprise { Nom = "Inconnu" }, true);
                    }
                    
                    ResetSale();
                    return;
                }

                // En mode PostgreSQL Multi-Postes (ou hors-ligne), passer par CaisseServices.RecordSaleOrQueueAsync
                // pour un décrément atomique côté serveur + file d'attente locale automatique si Tailscale coupe
                if (_caisseServices.Factory.Provider == DatabaseProvider.PostgreSql || !_caisseServices.IsOnline)
                {
                    foreach (var item in Panier)
                    {
                        int productId = item.Produit.Id;
                        decimal quantity = item.Quantite;
                        var ligne = item.ToEntity();
                        ligne.Produit = null;
                        vente.Lignes.Add(ligne);

                        var productToUpdate = _context.Produits.Local.FirstOrDefault(p => p.Id == productId);
                        if (productToUpdate != null)
                        {
                            productToUpdate.StockActuel -= quantity;
                            productToUpdate.ValidatedSalesCount += Math.Max(1, (int)Math.Ceiling(quantity));
                            _context.Entry(productToUpdate).State = EntityState.Unchanged;
                        }
                    }

                    await _caisseServices.RecordSaleOrQueueAsync(vente);
                }
                else
                {
                    var addedMovements = new List<MouvementStock>(Panier.Count);
                    foreach (var item in Panier)
                    {
                        int productId = item.Produit.Id;
                        decimal quantity = item.Quantite;
                        decimal price = item.Produit.PrixVente;
                        string productNom = item.ProduitNom;
                        
                        var ligne = item.ToEntity();
                        ligne.Produit = null; 
                        vente.Lignes.Add(ligne);

                        var mvmt = new MouvementStock
                        {
                            ProduitId = productId,
                            TypeMouvement = "sortie",
                            Quantite = quantity,
                            PrixUnitaire = price, 
                            DateMouvement = now,
                            Commentaire = "Vente",
                            ProduitNomSnapshot = productNom
                        };
                        _context.MouvementsStock.Add(mvmt);
                        addedMovements.Add(mvmt);

                        var productToUpdate = _context.Produits.Local.FirstOrDefault(p => p.Id == productId)
                                              ?? _context.Produits.FirstOrDefault(p => p.Id == productId);
                        if (productToUpdate != null)
                        {
                             productToUpdate.StockActuel -= quantity;
                             productToUpdate.ValidatedSalesCount += Math.Max(1, (int)Math.Ceiling(quantity));
                        }
                    }

                    _context.Ventes.Add(vente);
                    await _context.SaveChangesAsync();

                    // Detach sale and stock movements so the long-lived _context ChangeTracker stays tiny and fast
                    foreach (var m in addedMovements)
                        _context.Entry(m).State = EntityState.Detached;
                    foreach (var l in vente.Lignes)
                        _context.Entry(l).State = EntityState.Detached;
                    _context.Entry(vente).State = EntityState.Detached;

                    _caisseServices.Popularity.Invalidate();
                }

                decimal changeToReturn = MonnaieRendre;

                // Create the summary window BEFORE resetting the sale so all data is guaranteed valid
                var summary = new SystemeCaisse.UI.Views.ReceiptSummaryWindow(vente, CurrentEntreprise ?? new Entreprise { Nom = "Inconnu" }, changeToReturn, false);
                SetupWindowOwner(summary);
                summary.ShowDialog();

                // ONLY reset the sale after the summary window is closed
                ResetSale();
                BasketRemiseManuelle = 0;

                // Trigger Thank You message on Customer Display
                if (_customerDisplay != null && _customerDisplay.IsVisible)
                {
                    ShowThankYouMessage = true;
                    _ = Task.Delay(5000).ContinueWith(_ => 
                    {
                        Application.Current.Dispatcher.Invoke(() => ShowThankYouMessage = false);
                    });
                }
                
                if (changeToReturn > 0)
                    MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Monnaie à rendre : {changeToReturn:C}", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur lors de l'enregistrement : {ex.InnerException?.Message ?? ex.Message}", "Erreur", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task SendToTPE()
        {
            if (Panier.Count == 0 || !IsTPEConnected || _tpeService == null) return;

            decimal montantTPE = SelectedPaiementMode == "CB" ? Total : MontantCarte;
            if (montantTPE <= 0)
            {
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Le montant CB est nul ou négatif.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool retry = true;
            while (retry)
            {
                retry = false;
                IsTPEPaymentInProgress = true;
                TPEStatusMessage = "⏳ En attente du TPE...";
                StatusMessage = $"Paiement TPE en cours ({montantTPE:C})...";

                try
                {
                    var result = await _tpeService.PayAsync(montantTPE);

                    if (result.Success)
                    {
                        IsTPEPaymentInProgress = false;
                        TPEStatusMessage = "✅ Paiement accepté";
                        StatusMessage = "Paiement TPE accepté — validation en cours...";

                        Checkout(null);
                    }
                    else
                    {
                        IsTPEPaymentInProgress = false;
                        TPEStatusMessage = $"❌ {result.Message}";
                        StatusMessage = "Paiement TPE refusé";

                        var errorWindow = new SystemeCaisse.UI.Views.TPEPaymentErrorWindow(result.Message, montantTPE);
                        SetupWindowOwner(errorWindow);
                        bool? dialogResult = errorWindow.ShowDialog();

                        if (dialogResult == true)
                        {
                            retry = true;
                        }
                        else
                        {
                            StatusMessage = "Paiement TPE annulé";
                        }
                    }
                }
                catch (Exception ex)
                {
                    IsTPEPaymentInProgress = false;
                    TPEStatusMessage = $"❌ Erreur : {ex.Message}";
                    StatusMessage = "Erreur paiement TPE";

                    var errorWindow = new SystemeCaisse.UI.Views.TPEPaymentErrorWindow($"Erreur de communication : {ex.Message}", montantTPE);
                    SetupWindowOwner(errorWindow);
                    bool? dialogResult = errorWindow.ShowDialog();

                    if (dialogResult == true)
                    {
                        retry = true;
                    }
                }
            }
        }

        private void PrintLastTicket()
        {
            try
            {
                using var ctx = _contextFactory.CreateDbContext();
                var lastVente = ctx.Ventes
                    .AsNoTracking()
                    .Include(v => v.Lignes)
                    .OrderByDescending(v => v.CreatedAt)
                    .FirstOrDefault();

                if (lastVente != null)
                {
                    _printService.PrintTicket(lastVente, CurrentEntreprise ?? new Entreprise { Nom = "Inconnu" });
                }
                else
                {
                    System.Windows.MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Aucune vente récente à imprimer.", "Info", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur d'impression : {ex.Message}", "Erreur", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadDisplayPromotions(AppDbContext context)
        {
            var config = context.Configuration.Find("customer_display_promotions");
            if (config != null && !string.IsNullOrWhiteSpace(config.Valeur))
            {
                var ids = config.Valeur.Split(',').Select(id => int.TryParse(id, out int parsed) ? parsed : -1).Where(id => id > 0).ToList();
                var promos = context.Promotions.Include(p => p.Produit)
                                               .Include(p => p.BundleItems).ThenInclude(b => b.Produit)
                                               .Where(p => ids.Contains(p.Id) && p.Actif).ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    DisplayPromotions.Clear();
                    foreach (var p in promos) DisplayPromotions.Add(p);
                    if (DisplayPromotions.Any()) CurrentDisplayPromotion = DisplayPromotions.First();
                });
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DisplayPromotions.Clear();
                    CurrentDisplayPromotion = null;
                });
            }
        }

        private void StartPromoCarousel()
        {
            if (_promoCarouselTimer != null)
            {
                _promoCarouselTimer.Stop();
            }

            _currentPromoIndex = 0;
            _promoCarouselTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _promoCarouselTimer.Tick += (s, e) =>
            {
                if (DisplayPromotions.Count == 0) return;
                _currentPromoIndex = (_currentPromoIndex + 1) % DisplayPromotions.Count;
                CurrentDisplayPromotion = DisplayPromotions[_currentPromoIndex];
            };
            _promoCarouselTimer.Start();
        }

        public void RefreshDisplayPromotions()
        {
            using var ctx = _contextFactory.CreateDbContext();
            LoadDisplayPromotions(ctx);
            
            if (ShowDisplayPromotions && DisplayPromotions.Count > 0)
            {
                StartPromoCarousel();
            }
            else if (_promoCarouselTimer != null)
            {
                _promoCarouselTimer.Stop();
                CurrentDisplayPromotion = null;
            }
        }

        private void AddToCart(object parameter)
        {
            if (parameter is Produit produit)
            {
                if (IsReturnMode)
                {
                    var existing = Panier.FirstOrDefault(i => i.Produit.Id == produit.Id);
                    if (existing != null)
                    {
                        existing.Quantite -= 1; 
                        if (existing.Quantite == 0)
                        {
                            Panier.Remove(existing);
                        }
                    }
                    else
                    {
                         AddLine(produit, -1);
                    }
                }
                else
                {
                    bool isWeight = string.Equals(produit.TypeVente?.Trim(), "poids", StringComparison.OrdinalIgnoreCase);

                    if (isWeight)
                    {
                        SystemeCaisse.UI.Views.CustomerWeightDisplayWindow? customerWeightWin = null;
                        try
                        {
                            var screens = ScreenHelper.GetScreens();
                            if (screens.Count >= 2 && _customerDisplay != null)
                            {
                                using var ctx = _contextFactory.CreateDbContext();
                                var screenConfig = ctx.Configuration.Find("customer_display_screen_index");
                                int savedIndex = (screenConfig != null && int.TryParse(screenConfig.Valeur, out int si)) ? si : -1;

                                ScreenHelper.ScreenInfo? targetScreen = null;
                                if (savedIndex >= 0 && savedIndex < screens.Count)
                                    targetScreen = screens[savedIndex];
                                else
                                    targetScreen = screens.FirstOrDefault(s => !s.IsPrimary) ?? (screens.Count > 1 ? screens[1] : null);

                                if (targetScreen != null)
                                {
                                    customerWeightWin = new SystemeCaisse.UI.Views.CustomerWeightDisplayWindow(produit, _scaleService);
                                    customerWeightWin.ShowOnScreen(targetScreen);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"CustomerWeightDisplay error: {ex.Message}");
                        }

                        var dialog = new SystemeCaisse.UI.Views.WeightInputWindow(produit, _scaleService);
                        SetupWindowOwner(dialog);
                        if (dialog.ShowDialog() == true)
                        {
                            AddLine(produit, dialog.PoidsSaisi);
                        }

                        try { customerWeightWin?.Close(); } catch { }
                    }
                    else
                    {
                        AddLine(produit, 1);
                    }
                }
                UpdateTotal();
            }
        }

        private void AddLine(Produit produit, decimal qty)
        {
            var existingItem = Panier.FirstOrDefault(i => i.Produit.Id == produit.Id);
            if (existingItem != null)
            {
                existingItem.Quantite += qty;
                 if (existingItem.Quantite == 0) Panier.Remove(existingItem);
            }
            else
            {
                 var ligne = new LigneVente
                {
                    Produit = produit,
                    ProduitId = produit.Id,
                    ProduitNom = produit.Nom,
                    CategorieNom = !string.IsNullOrWhiteSpace(produit.Categorie) ? produit.Categorie : "Autre",
                    PrixUnitaire = produit.PrixVente,
                    Quantite = qty,
                    TotalLigne = qty * produit.PrixVente,
                    TaxTier = produit.TaxTier
                };
                
                var cartItem = new CartItemViewModel(ligne);
                Panier.Add(cartItem);
            }
        }

        private void ResetSale()
        {
            Panier.Clear();
            BasketRemiseManuelle = 0;
            UpdateTotal();
            MontantRecu = 0;
            SelectedPaiementMode = "CB";
            RecalculateMonnaie();
        }

        private void LoadPromotions()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var now = DateTime.Now;
                _activePromotions = context.Promotions
                    .Include(p => p.Tiers)
                    .Include(p => p.BundleItems)
                    .Where(p => p.Actif && p.DateDebut <= now && (p.DateFin == null || p.DateFin >= now))
                    .AsNoTracking()
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadPromotions error: {ex.Message}");
            }
        }

        private decimal GetAvailableQty(CartItemViewModel item, Dictionary<CartItemViewModel, decimal> consumedQty)
        {
            consumedQty.TryGetValue(item, out decimal used);
            return item.Quantite - used;
        }

        private void ConsumeQty(CartItemViewModel item, decimal qty, Dictionary<CartItemViewModel, decimal> consumedQty)
        {
            consumedQty.TryGetValue(item, out decimal used);
            consumedQty[item] = used + qty;
        }

        private void ApplyAutomaticPromotions()
        {
            foreach (var item in Panier)
            {
                item.RemiseAuto = 0;
                item.PromotionAppliquee = null;
            }

            if (_activePromotions == null || !_activePromotions.Any()) return;

            var consumedQty = new Dictionary<CartItemViewModel, decimal>();

            foreach (var promo in _activePromotions.Where(p => p.TypePromotion != "remise_total" && p.TypePromotion != "seuil_panier"))
            {
                switch (promo.TypePromotion)
                {
                    case "remise_produit":
                    {
                        var targetItems = Panier.Where(i =>
                            GetAvailableQty(i, consumedQty) > 0 &&
                            ((promo.ProduitId != null && i.Produit.Id == promo.ProduitId) ||
                             (promo.Categorie != null && i.Produit.Categorie == promo.Categorie))
                        ).ToList();

                        foreach (var item in targetItems)
                        {
                            decimal available = GetAvailableQty(item, consumedQty);
                            decimal availableTotal = item.PrixUnitaire * available;
                            decimal r = promo.IsPourcentage ? (availableTotal * promo.Valeur / 100) : promo.Valeur;
                            item.RemiseAuto += r;
                            item.PromotionAppliquee = AppendPromoLabel(item.PromotionAppliquee, promo.Nom);
                            ConsumeQty(item, available, consumedQty);
                        }
                        break;
                    }

                    case "offre_combine":
                    {
                        if (promo.BundleItems == null || !promo.BundleItems.Any()) break;

                        int maxBundles = int.MaxValue;
                        foreach (var bi in promo.BundleItems)
                        {
                            decimal totalAvailable = Panier
                                .Where(i => i.ProduitId == bi.ProduitId)
                                .Sum(i => GetAvailableQty(i, consumedQty));
                            maxBundles = Math.Min(maxBundles, (int)(totalAvailable / bi.QuantiteRequise));
                        }
                        if (maxBundles <= 0) break;

                        decimal originalBundleTotal = 0;
                        foreach (var bi in promo.BundleItems)
                        {
                            var prod = Produits.FirstOrDefault(p => p.Id == bi.ProduitId);
                            originalBundleTotal += (prod?.PrixVente ?? 0) * bi.QuantiteRequise;
                        }
                        if (originalBundleTotal <= 0) break;

                        var participantIds = promo.BundleItems.Select(bi => bi.ProduitId).ToList();
                        var bundleLines = Panier
                            .Where(i => i.ProduitId.HasValue && participantIds.Contains(i.ProduitId.Value))
                            .ToList();

                        foreach (var bi in promo.BundleItems)
                        {
                            decimal remainingQtyToDiscount = maxBundles * bi.QuantiteRequise;
                            var itemsForThisProd = bundleLines
                                .Where(l => l.ProduitId == bi.ProduitId && GetAvailableQty(l, consumedQty) > 0)
                                .OrderByDescending(l => GetAvailableQty(l, consumedQty))
                                .ToList();

                            foreach (var line in itemsForThisProd)
                            {
                                if (remainingQtyToDiscount <= 0) break;
                                decimal available = GetAvailableQty(line, consumedQty);
                                decimal qtyToDiscountOnThisLine = Math.Min(available, remainingQtyToDiscount);
                                decimal originalLinePrice = line.PrixUnitaire;
                                decimal ratio = promo.Valeur / originalBundleTotal;
                                decimal discountedUnitPrice = originalLinePrice * ratio;
                                decimal discountAmount = qtyToDiscountOnThisLine * (originalLinePrice - discountedUnitPrice);
                                line.RemiseAuto += discountAmount;
                                line.PromotionAppliquee = AppendPromoLabel(line.PromotionAppliquee, promo.Nom);
                                ConsumeQty(line, qtyToDiscountOnThisLine, consumedQty);
                                remainingQtyToDiscount -= qtyToDiscountOnThisLine;
                            }
                        }
                        break;
                    }

                    case "quantite_offerte":
                    {
                        if (promo.SeuilQuantite > 0 && promo.QuantiteOfferte > 0)
                        {
                            var targetItems = Panier.Where(i =>
                                GetAvailableQty(i, consumedQty) > 0 &&
                                ((promo.ProduitId != null && i.Produit.Id == promo.ProduitId) ||
                                 (promo.Categorie != null && i.Produit.Categorie == promo.Categorie))
                            ).ToList();

                            foreach (var item in targetItems)
                            {
                                decimal available = GetAvailableQty(item, consumedQty);
                                decimal fullSetSize = promo.SeuilQuantite.Value + promo.QuantiteOfferte.Value;
                                int sets = (int)(available / fullSetSize);
                                if (sets > 0)
                                {
                                    decimal consumedByPromo = sets * fullSetSize;
                                    decimal r = sets * promo.QuantiteOfferte.Value * item.PrixUnitaire;
                                    item.RemiseAuto += r;
                                    item.PromotionAppliquee = AppendPromoLabel(item.PromotionAppliquee, $"{promo.Nom} ({sets} offert(s))");
                                    ConsumeQty(item, consumedByPromo, consumedQty);
                                }
                            }
                        }
                        break;
                    }

                    case "remise_ieme":
                    {
                        if (promo.IemeArticle > 0 && promo.RemiseSurIeme > 0)
                        {
                            var targetItems = Panier.Where(i =>
                                GetAvailableQty(i, consumedQty) > 0 &&
                                ((promo.ProduitId != null && i.Produit.Id == promo.ProduitId) ||
                                 (promo.Categorie != null && i.Produit.Categorie == promo.Categorie))
                            ).ToList();

                            foreach (var item in targetItems)
                            {
                                decimal available = GetAvailableQty(item, consumedQty);
                                int sets = (int)(available / promo.IemeArticle.Value);
                                if (sets > 0)
                                {
                                    decimal consumedByPromo = sets * promo.IemeArticle.Value;
                                    decimal r = sets * (promo.IsPourcentage 
                                        ? (item.PrixUnitaire * (promo.RemiseSurIeme.Value / 100))
                                        : promo.RemiseSurIeme.Value);
                                    item.RemiseAuto += r;
                                    string unit = promo.IsPourcentage ? "%" : "€";
                                    item.PromotionAppliquee = AppendPromoLabel(item.PromotionAppliquee, $"{promo.Nom} (-{promo.RemiseSurIeme}{unit} sur {sets})");
                                    ConsumeQty(item, consumedByPromo, consumedQty);
                                }
                            }
                        }
                        break;
                    }

                    case "prix_degressif":
                    {
                        var targetItems = Panier.Where(i =>
                            GetAvailableQty(i, consumedQty) > 0 &&
                            ((promo.ProduitId != null && i.Produit.Id == promo.ProduitId) ||
                             (promo.Categorie != null && i.Produit.Categorie == promo.Categorie))
                        ).ToList();

                        foreach (var item in targetItems)
                        {
                            decimal available = GetAvailableQty(item, consumedQty);
                            var bestTier = promo.Tiers
                                .Where(t => available >= t.QuantiteMin)
                                .OrderByDescending(t => t.QuantiteMin)
                                .FirstOrDefault();
                            if (bestTier != null && bestTier.PrixUnitaire < item.PrixUnitaire)
                            {
                                decimal tierQty = bestTier.QuantiteMin;
                                decimal standardTotal = item.PrixUnitaire * tierQty;
                                decimal degressifTotal = bestTier.PrixUnitaire * tierQty;
                                item.RemiseAuto += (standardTotal - degressifTotal);
                                item.PromotionAppliquee = AppendPromoLabel(item.PromotionAppliquee, $"{promo.Nom} ({bestTier.PrixUnitaire:C}/u x{tierQty})");
                                ConsumeQty(item, tierQty, consumedQty);
                            }
                        }
                        break;
                    }
                }
            }
        }

        private static string AppendPromoLabel(string? existing, string newLabel)
        {
            if (string.IsNullOrEmpty(existing)) return newLabel;
            return existing + " + " + newLabel;
        }
        
        private void ApplyManualDiscount()
        {
            try
            {
                var mainWindow = Services.WindowHelper.GetAdminWindow();
                var selectWindow = new SystemeCaisse.UI.Views.ManualDiscountSelectionWindow();
                SetupWindowOwner(selectWindow);
                if (selectWindow.ShowDialog() != true) return;

                var scope = selectWindow.SelectedScope;
                var type = selectWindow.SelectedType;
                CartItemViewModel? targetItem = null;

                if (scope == SystemeCaisse.UI.Views.DiscountScope.Item || scope == SystemeCaisse.UI.Views.DiscountScope.PriceOverride)
                {
                    if (Panier.Count == 0) return;
                    var itemWindow = new SystemeCaisse.UI.Views.CartItemSelectionWindow(Panier);
                    SetupWindowOwner(itemWindow);
                    if (itemWindow.ShowDialog() != true) return;
                    targetItem = itemWindow.SelectedItem;
                }

                if (scope == SystemeCaisse.UI.Views.DiscountScope.PriceOverride)
                {
                    if (targetItem == null) return;
                    string priceTitle = $"Nouveau prix de vente pour {targetItem.ProduitNom}";
                    var priceWindow = new SystemeCaisse.UI.Views.DiscountValueInputWindow(priceTitle, "€");
                    SetupWindowOwner(priceWindow);
                    if (priceWindow.ShowDialog() != true) return;

                    decimal newPrice = priceWindow.DiscountValue;
                    if (newPrice < 0) newPrice = 0;

                    decimal priceDiff = targetItem.PrixUnitaire - newPrice;
                    if (priceDiff > 0)
                    {
                        targetItem.RemiseManuelleFixed = priceDiff * targetItem.Quantite;
                        targetItem.RemiseManuellePercent = 0;
                        targetItem.PriceOverridePerUnit = priceDiff;
                    }
                    else
                    {
                        targetItem.RemiseManuelleFixed = 0;
                        targetItem.RemiseManuellePercent = 0;
                        targetItem.PriceOverridePerUnit = 0;
                    }
                    UpdateTotal();
                    return;
                }

                string title = (scope == SystemeCaisse.UI.Views.DiscountScope.Basket ? "Remise Panier" : $"Remise sur {targetItem?.ProduitNom}");
                string unit = (type == SystemeCaisse.UI.Views.DiscountType.Percentage ? "%" : "€");
                var valueWindow = new SystemeCaisse.UI.Views.DiscountValueInputWindow(title, unit);
                SetupWindowOwner(valueWindow);
                if (valueWindow.ShowDialog() != true) return;

                decimal val = valueWindow.DiscountValue;
                if (scope == SystemeCaisse.UI.Views.DiscountScope.Basket)
                {
                    if (type == SystemeCaisse.UI.Views.DiscountType.Percentage)
                    {
                        decimal baseTotal = Panier.Sum(i => i.TotalLigneStandard);
                        BasketRemiseManuelle = baseTotal * val / 100;
                    }
                    else BasketRemiseManuelle = val;
                }
                else if (targetItem != null)
                {
                    if (type == SystemeCaisse.UI.Views.DiscountType.Percentage)
                    {
                        targetItem.RemiseManuellePercent = val;
                        targetItem.RemiseManuelleFixed = 0;
                    }
                    else
                    {
                        targetItem.RemiseManuelleFixed = val;
                        targetItem.RemiseManuellePercent = 0;
                    }
                    targetItem.PriceOverridePerUnit = 0;
                }
                UpdateTotal();
            }
            catch { }
        }

        private void UpdateTotal()
        {
            decimal baseTotal = Panier.Sum(i => i.TotalLigneStandard);
            decimal totalRemiseAuto = Panier.Sum(i => i.RemiseAuto);
            decimal totalRemiseManuelleArticles = Panier.Sum(i => i.RemiseManuelle);

            decimal totalRemiseAutoPanier = 0;
            var basketPromos = _activePromotions?
                .Where(p => p.TypePromotion == "remise_total" || p.TypePromotion == "seuil_panier")
                .ToList();

            if (basketPromos != null)
            {
                foreach (var promo in basketPromos)
                {
                    if (promo.TypePromotion == "seuil_panier")
                    {
                        if (baseTotal >= promo.SeuilPanier)
                            totalRemiseAutoPanier += promo.IsPourcentage ? (baseTotal * promo.Valeur / 100) : promo.Valeur;
                    }
                    else if (promo.TypePromotion == "remise_total")
                    {
                        totalRemiseAutoPanier += promo.IsPourcentage ? (baseTotal * promo.Valeur / 100) : promo.Valeur;
                    }
                }
            }

            TotalRemise = totalRemiseAuto + totalRemiseManuelleArticles + totalRemiseAutoPanier + BasketRemiseManuelle;
            Total = baseTotal - TotalRemise;
            if (Total < 0) Total = 0;

            OnPropertyChanged(nameof(TotalHorsRemise));
            OnPropertyChanged(nameof(TotalRemises));
            OnPropertyChanged(nameof(TotalVente));
            OnPropertyChanged(nameof(TotalEuro));
            OnPropertyChanged(nameof(TotalSansRemise));
            
            RecalculateMonnaie();
        }
    }

    public class SuspendedSale
    {
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal BasketRemiseManuelle { get; set; }
        public decimal Total { get; set; }
        public string Label { get; set; } = string.Empty;
        public override string ToString() => Label;
    }
}
