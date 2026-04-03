using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.UI.Views;
using SystemeCaisse.UI.Services;
using SystemeCaisse.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;

namespace SystemeCaisse.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly AppDbContext _context;
        private readonly PrintService _printService;
        private readonly IDataMigrationService _migrationService;


        private List<Promotion> _activePromotions = new();
        
        public ObservableCollection<Produit> Produits { get; set; }
        public ObservableCollection<Produit> TopProducts { get; set; }
        public ICollectionView ProductsView { get; private set; }
        public ObservableCollection<Produit> SearchSuggestions { get; private set; } = new();
        public ObservableCollection<CartItemViewModel> Panier { get; set; } = new();
        
        public ProductsViewModel ProductsVM { get; private set; }
        public StocksViewModel StocksVM { get; private set; }
        public HistoryViewModel HistoryVM { get; private set; }
        public SettingsViewModel SettingsVM { get; private set; }
        public DashboardViewModel DashboardVM { get; private set; }
        public PromotionsViewModel PromotionsVM { get; private set; }
        public InventoryViewModel InventoryVM { get; private set; }
        public AnalysisViewModel AnalysisVM { get; private set; }
        
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
                            UpdateSearchSuggestions();
                            IsSearchDropDownOpen = true;
                        }
                        else
                        {
                            IsSearchDropDownOpen = false;
                            _ = Application.Current.Dispatcher.InvokeAsync(() => SearchSuggestions.Clear());
                        }
                    }
                    else
                    {
                        IsSearchDropDownOpen = false;
                        _ = Application.Current.Dispatcher.InvokeAsync(() => SearchSuggestions.Clear());
                    }
                    
                    ProductsView.Refresh();
                }
            }
        }

        private void UpdateSearchSuggestions()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => 
            {
                SearchSuggestions.Clear();
                if (string.IsNullOrWhiteSpace(SearchText) || SearchText.Length < 1) return;

                var search = SearchText.Trim().ToLower();
                var matches = Produits
                    .Where(p => p.Actif && // Only active products
                                ((p.Nom != null && p.Nom.ToLower().Contains(search)) || 
                                 (p.CodeBarre != null && p.CodeBarre.Contains(search))))
                    .Take(50) // Speed optimization: only show top 50 matches
                    .ToList();

                foreach (var m in matches) SearchSuggestions.Add(m);
            }), System.Windows.Threading.DispatcherPriority.Background);
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
        private string _selectedPaiementMode = "Especes";
        public string SelectedPaiementMode
        {
            get => _selectedPaiementMode;
            set
            {
                _selectedPaiementMode = value;
                OnPropertyChanged();
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
                // Monnaie = Recu - A Payer (Total correctly updated)
                MonnaieRendre = Math.Max(0, MontantRecu - Total);
                MontantCarte = 0;
            }
            else if (SelectedPaiementMode == "Mixte" || SelectedPaiementMode == "Espece/CB")
            {
                MonnaieRendre = 0;
                // Part paid by card is the remainder
                MontantCarte = Math.Max(0, Total - MontantRecu);
            }
            else // CB
            {
                MonnaieRendre = 0;
                MontantCarte = Total;
                // Fix: Don't set MontantRecu to 0 here to avoid triggering property changes that might crash bound textboxes
            }
        }

        private string _statusMessage = "Prêt";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
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
            _context.Entreprise.Load();
            CurrentEntreprise = _context.Entreprise.Local.FirstOrDefault();
            
            if (CurrentEntreprise == null)
            {
                // Fallback / Seed if somehow missing (though DbContext seeds it)
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
                    
                    // Refresh Display
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

        private void ReloadProductsFromDb()
        {
            try
            {
                // CRITICAL: Clear the tracker to discard cached entity state from previous tab visits.
                // This forces EF to fetch the latest values (prices, etc.) from the DB.
                _context.ChangeTracker.Clear();
                
                _context.Produits.Load();
                Produits = _context.Produits.Local.ToObservableCollection();
                
                // Refresh the CollectionView
                ProductsView = CollectionViewSource.GetDefaultView(Produits);
                ProductsView.Filter = FilterProducts;
                ProductsView.Refresh();
                
                OnPropertyChanged(nameof(Produits));
                OnPropertyChanged(nameof(ProductsView));

                // Force refresh TopProducts (with the new product objects)
                _ = CalculateProductPopularity(); 
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reload Error: {ex.Message}");
            }
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

                if (value == 5)
                {
                    // Analysis disabled temporarily due to build issues
                }

                if (value == 1)
                {
                    // Price Sync: Reload latest products from DB when entering Caisse
                    ReloadProductsFromDb();
                    
                    LoadPromotions();
                    ApplyAutomaticPromotions();
                    UpdateTotal();
                }

                if (value == 4)
                {
                    // Auto-refresh history data when switching to Ventes tab
                    _ = HistoryVM?.LoadDataAsync();
                }
            }
        }

        public MainViewModel(IDbContextFactory<AppDbContext> contextFactory, PrintService printService, IDataMigrationService migrationService)
        {
            _context = contextFactory.CreateDbContext();
            _contextFactory = contextFactory;
            _printService = printService;
            _migrationService = migrationService;
            
            AvailablePromotions = new ObservableCollection<Promotion>();
            Panier = new ObservableCollection<CartItemViewModel>();
            TopProducts = new ObservableCollection<Produit>();
            AddToCartCommand = new BasicRelayCommand(AddToCart);

            // Initialize Child ViewModels (instantiation only, no DB load)
            ProductsVM = new ProductsViewModel(contextFactory);
            StocksVM = new StocksViewModel(contextFactory);
            HistoryVM = new HistoryViewModel(contextFactory, _printService);
            SettingsVM = new SettingsViewModel(contextFactory, migrationService);
            DashboardVM = new DashboardViewModel(contextFactory);
            PromotionsVM = new PromotionsViewModel(contextFactory);
            InventoryVM = new InventoryViewModel(contextFactory);
            AnalysisVM = new AnalysisViewModel(contextFactory);
            RemoveItemCommand = new BasicRelayCommand(RemoveItem);
            CheckoutCommand = new BasicRelayCommand(Checkout, _ => Panier.Count > 0);
            PaymentModeCommand = new BasicRelayCommand(SetPaymentMode);
            CancelCommand = new BasicRelayCommand(_ => ResetSale());
            ViderPanierCommand = new BasicRelayCommand(_ => ResetSale());
            ScanCommand = new BasicRelayCommand(_ => HandleScan());
            EditQuantityCommand = new BasicRelayCommand(EditQuantity);
            CalculateProductPopularityCommand = new BasicRelayCommand(async _ => await CalculateProductPopularity());
            ClearSearchCommand = new BasicRelayCommand(_ => SearchText = string.Empty);
            
            OpenWeightDialogCommand = new BasicRelayCommand(_ => 
            {
                var weightProducts = Produits.Where(p => string.Equals(p.TypeVente, "poids", StringComparison.OrdinalIgnoreCase)).ToList();
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
                            if (args.PropertyName == nameof(CartItemViewModel.Quantite) || 
                                args.PropertyName == nameof(CartItemViewModel.TotalLigneStandard))
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
            
            // Final refresh of TopProducts to be sure
            Task.Delay(1000).ContinueWith(_ => CalculateProductPopularity());

            // NOTE: Customer Display initialization is triggered by MainWindow.Loaded event (in MainWindow.xaml.cs)
            // This ensures it runs on the UI thread with a fully visible window.

            // Start Live Clock Timer
            _clockTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => { OnPropertyChanged(nameof(CurrentDateTime)); OnPropertyChanged(nameof(CurrentDate)); OnPropertyChanged(nameof(CurrentTime)); };
            _clockTimer.Start();
        }

        public async Task InitializeAsync()
        {
            await Task.Run(async () => 
            {
                try 
                {
                    // 1. Initialize self data
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] _context.Produits.Load()\n");
                    _context.Produits.Load();
                    
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Dispatcher.Invoke (Produits list)\n");
                    await Application.Current.Dispatcher.InvokeAsync(() => 
                    {
                        Produits = _context.Produits.Local.ToObservableCollection();
                        ProductsView = CollectionViewSource.GetDefaultView(Produits);
                        ProductsView.Filter = FilterProducts;
                        ProductsView.SortDescriptions.Clear();
                        ProductsView.SortDescriptions.Add(new SortDescription("ValidatedSalesCount", ListSortDirection.Descending));
                        ProductsView.SortDescriptions.Add(new SortDescription("Nom", ListSortDirection.Ascending));
                    });

                    _ = CalculateProductPopularity(); 
                    LoadEntrepriseInfo();
                    LoadPromotions();
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] MAIN INIT ERROR: {ex.Message}\nStack: {ex.StackTrace}\n");
                }

                // 2. Initialize children — each wrapped in try/catch so one failure doesn't block others
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Sub-VM execution start\n");
                
                await SafeInitAsync("ProductsVM", () => ProductsVM.InitializeAsync());
                await SafeInitAsync("StocksVM", () => StocksVM.InitializeAsync());
                await SafeInitAsync("HistoryVM", () => HistoryVM.InitializeAsync());
                await SafeInitAsync("SettingsVM", () => SettingsVM.InitializeAsync());
                await SafeInitAsync("DashboardVM", () => DashboardVM.InitializeAsync());
                await SafeInitAsync("PromotionsVM", () => PromotionsVM.InitializeAsync());
                await SafeInitAsync("InventoryVM", () => InventoryVM.InitializeAsync());
                await SafeInitAsync("AnalysisVM", () => AnalysisVM.InitializeAsync());
                
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Sub-VM execution end\n");

                // 3. Initialize Scale Service
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

                // Load display promotions
                LoadDisplayPromotions(context);

                // Start or restart carousel timer
                if (ShowDisplayPromotions && DisplayPromotions.Count > 0)
                {
                    StartPromoCarousel();
                }

                // Determine which screen to use
                var screenConfig = context.Configuration.Find("customer_display_screen_index");
                int savedScreenIndex = (screenConfig != null && int.TryParse(screenConfig.Valeur, out int si)) ? si : -1;
                
                var screens = ScreenHelper.GetScreens();
                
                // Detailed logging for monitor discovery
                string screenLog = $"[{DateTime.Now}] Screen Discovery: Found {screens.Count} screens.\n";
                for (int i = 0; i < screens.Count; i++)
                {
                    var s = screens[i];
                    screenLog += $"  - Screen {i}: Primary={s.IsPrimary}, Bounds={s.Bounds.Width}x{s.Bounds.Height}, LogBounds={s.LogicalBounds.Width}x{s.LogicalBounds.Height}\n";
                }
                System.IO.File.AppendAllText("startup_log_v2.txt", screenLog);

                // PROJECT_GUIDELINES §1: Customer display requires at least 2 screens
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
                    // Default: first non-primary screen or second screen
                    targetScreen = screens.FirstOrDefault(s => !s.IsPrimary) ?? (screens.Count > 1 ? screens[1] : null);
                }
                
                if (targetScreen == null)
                {
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] CustomerDisplay: No valid target screen found. Skipping.\n");
                    return;
                }

                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Decision: Target Screen Index={screens.IndexOf(targetScreen)}, IsPrimary={targetScreen.IsPrimary}\n");

                // Identify Admin screen (MUST be different from targetScreen)
                var adminScreen = screens.FirstOrDefault(s => s != targetScreen) ?? screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
                
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Screen Assignment: Client=Screen {screens.IndexOf(targetScreen)}, Admin=Screen {screens.IndexOf(adminScreen)}\n");

                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Attempting SHOW on monitor {screens.IndexOf(targetScreen)} at {targetScreen.LogicalBounds.Left},{targetScreen.LogicalBounds.Top}\n");

                _customerDisplay = new SystemeCaisse.UI.Views.CustomerDisplayWindow(this);

                _customerDisplay.WindowStartupLocation = WindowStartupLocation.Manual;
                _customerDisplay.Topmost = true; // Stay on top
                _customerDisplay.WindowState = WindowState.Normal;
                _customerDisplay.WindowStyle = WindowStyle.None;

                // Use LOGICAL coordinates for WPF
                _customerDisplay.Left = targetScreen.LogicalBounds.Left;
                _customerDisplay.Top = targetScreen.LogicalBounds.Top;
                _customerDisplay.Width = targetScreen.LogicalBounds.Width;
                _customerDisplay.Height = targetScreen.LogicalBounds.Height;
                
                _customerDisplay.Show();
                
                // Re-set and maximize AFTER show to ensure it respects the monitor
                _customerDisplay.Left = targetScreen.LogicalBounds.Left;
                _customerDisplay.Top = targetScreen.LogicalBounds.Top;
                _customerDisplay.WindowState = WindowState.Maximized;
                
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] CustomerDisplay: Window SHOWN successfully.\n");

                // Ensure Admin window is moved to the designated admin screen
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

            // CRITICAL: Must set to Normal before moving if maximized
            bool wasMaximized = mainWin.WindowState == WindowState.Maximized;
            if (wasMaximized) mainWin.WindowState = WindowState.Normal;

            // Move the admin window using LOGICAL coordinates
            mainWin.Left = adminScreen.LogicalWorkingArea.Left + 50; 
            mainWin.Top = adminScreen.LogicalWorkingArea.Top + 50;

            if (wasMaximized)
            {
                // Re-position accurately and re-maximize
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
                
                // Instead of relying on CenterOwner (which can be unreliable with DPI),
                // we manually center the window on the Admin Monitor.
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                
                // Load screens to find where the Admin is
                var screens = ScreenHelper.GetScreens();
                var adminScreen = screens.FirstOrDefault(s => 
                    s.LogicalWorkingArea.Contains(new Point(mainWin.Left + 10, mainWin.Top + 10))) ?? screens[0];
                
                // Wait for the window to load its size or use default
                win.SourceInitialized += (s, e) => 
                {
                    // Center calculation
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
                if (!produit.Actif) return false; // Hide inactive products
                if (string.IsNullOrWhiteSpace(SearchText)) return true;
                
                var search = SearchText.Trim();
                // Enhanced Search: Name, Barcode, OR ID
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
                 using var context = _contextFactory.CreateDbContext();
                 
                // Fix for SQLite Decimal Sum Error: Fetch minimal data and aggregate in memory
                var allLines = await context.LignesVente.AsNoTracking()
                    .Select(l => new { l.ProduitId, l.ProduitNom, l.Quantite })
                    .ToListAsync();

                // Grouping logic: ID preferred, Name as fallback for old data
                var salesStats = allLines
                    .GroupBy(l => l.ProduitId > 0 ? l.ProduitId.ToString() : l.ProduitNom ?? "Inconnu")
                    .Select(g => new { Key = g.Key, Count = g.Sum(x => x.Quantite) })
                    .OrderByDescending(x => x.Count)
                    .ToList();

                // Update local instances for sorting in main grid
                foreach (var p in Produits)
                {
                    var stat = salesStats.FirstOrDefault(s => s.Key == p.Id.ToString() || s.Key == p.Nom);
                    p.ValidatedSalesCount = stat?.Count ?? 0;
                }

                // Update Top 20 (Active products only)
                var top20Items = new List<Produit>();
                foreach (var stat in salesStats) 
                {
                    var matchingProd = Produits.FirstOrDefault(p => p.Actif && (p.Id.ToString() == stat.Key || p.Nom == stat.Key));
                    if (matchingProd != null && !top20Items.Contains(matchingProd))
                    {
                        top20Items.Add(matchingProd);
                        if (top20Items.Count >= 20) break;
                    }
                }

                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => 
                {
                    TopProducts.Clear();
                    foreach (var p in top20Items) 
                    {
                        if (p != null) TopProducts.Add(p);
                    }
                    
                    // Force refresh the collection view
                    ProductsView?.Refresh();
                    System.Diagnostics.Debug.WriteLine($"POS DEBUG: TopProducts count={TopProducts.Count}, total active prods={Produits.Count}");
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
            // Exact match priority
            var product = Produits.FirstOrDefault(p => p.CodeBarre == code);
            
            if (product == null)
            {
                // If numeric, try ID?
                if (int.TryParse(code, out int id) && id < 10000) // Assumption: IDs are small, barcodes large
                     product = Produits.FirstOrDefault(p => p.Id == id);
            }

            if (product != null)
            {
                AddToCart((object)product);
                SearchText = string.Empty; // Clear after successful scan
                IsSearchDropDownOpen = false;
                SearchSuggestions.Clear();
            }
            else
            {
                // Product Not Found logic
                var dialog = new SystemeCaisse.UI.Views.ProductNotFoundWindow(code);
                SetupWindowOwner(dialog);
                if (dialog.ShowDialog() == true && dialog.AddRequested)
                {
                    // Fetch existing categories for the autocomplete
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

                        // Save to database using the main context to keep tracking in sync
                        _context.Produits.Add(newProd);
                        await _context.SaveChangesAsync();

                        // Add to local observable collection so it appears in UI
                        Produits.Add(newProd);

                        _ = ProductsVM.LoadDataCommand.ExecuteAsync(null);
                        _ = StocksVM.LoadDataAsync();
                        _ = InventoryVM.LoadHistoryAsync();

                        // Automatically add to cart
                        AddToCart((object)newProd);
                        
                        // Force refresh of the TopProducts and Search suggestions if needed
                        UpdateSearchSuggestions();
                    }
                }
                
                // CRITICAL: Always clear SearchText after the flow, even if cancelled
                SearchText = string.Empty;
                IsSearchDropDownOpen = false;
                SearchSuggestions.Clear();
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
                var vente = new Vente
                {
                    CreatedAt = DateTime.Now,
                    Total = Total,
                    TotalRemise = TotalRemise,
                    NbArticles = (int)Panier.Sum(i => i.Quantite),
                    NumeroTicket = DateTime.Now.Ticks.ToString().Substring(10),
                    MoyenPaiement = SelectedPaiementMode,
                    MontantEspeces = SelectedPaiementMode == "Especes" ? MontantRecu : (SelectedPaiementMode == "Mixte" ? MontantRecu : 0),
                    MontantCB = SelectedPaiementMode == "CB" ? Total : (SelectedPaiementMode == "Mixte" ? MontantCarte : 0),
                    MonnaieRendue = MonnaieRendre,
                    Statut = "validee"
                };

                if (SelectedPaiementMode == "Especes" && MontantRecu < Total && Total > 0)
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
                        // PrintTicket uses WPF controls (FlowDocument, PrintDialog) which require STA thread
                        // Must run on UI dispatcher, not Task.Run
                        _printService.PrintTicket(vente, CurrentEntreprise ?? new Entreprise { Nom = "Inconnu" }, true);
                    }
                    
                    ResetSale();
                    return;
                }

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
                        DateMouvement = DateTime.Now,
                        Commentaire = "Vente",
                        ProduitNomSnapshot = productNom
                    };
                    _context.MouvementsStock.Add(mvmt);

                    var productToUpdate = _context.Produits.FirstOrDefault(p => p.Id == productId);
                    if (productToUpdate != null)
                    {
                         productToUpdate.StockActuel -= quantity;
                    }
                }

                _context.Ventes.Add(vente);
                await _context.SaveChangesAsync();

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
                    // Auto-hide after 5 seconds
                    _ = Task.Delay(5000).ContinueWith(_ => 
                    {
                        Application.Current.Dispatcher.Invoke(() => ShowThankYouMessage = false);
                    });
                }
                
                if (changeToReturn > 0)
                    MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Monnaie à rendre : {changeToReturn:C}", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);

                _ = Task.Run(async () => 
                {
                    await CalculateProductPopularity(); 
                    _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => 
                    {
                        _ = DashboardVM.LoadDashboardDataInternalAsync();
                        ProductsView.Refresh();
                        _ = StocksVM.LoadDataAsync();
                        _ = ProductsVM.LoadDataCommand.ExecuteAsync(null);
                        _ = InventoryVM.LoadHistoryAsync();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur lors de l'enregistrement : {ex.Message}", "Erreur", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void PrintLastTicket()
        {
            try
            {
                var lastVente = _context.Ventes
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
                        var dialog = new SystemeCaisse.UI.Views.WeightInputWindow(produit, _scaleService);
                        SetupWindowOwner(dialog);
                        if (dialog.ShowDialog() == true)
                        {
                            AddLine(produit, dialog.PoidsSaisi);
                        }
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
            // IMPORTANT: Do NOT manually reset item properties (RemiseManuellePercent/Fixed) 
            // before clearing, because SuspendSale keeps REFERENCES to these items.
            // Just clearing the Panier is enough for a fresh start.
            
            Panier.Clear();
            BasketRemiseManuelle = 0;
            UpdateTotal();
            MontantRecu = 0;
            SelectedPaiementMode = "Especes";
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

        private void ApplyAutomaticPromotions()
        {
            foreach (var item in Panier)
            {
                item.RemiseAuto = 0;
                item.PromotionAppliquee = null;
            }

            if (_activePromotions == null || !_activePromotions.Any()) return;

            foreach (var promo in _activePromotions.Where(p => p.TypePromotion != "remise_total" && p.TypePromotion != "seuil_panier"))
            {
                var targetItems = promo.TypePromotion == "offre_combine" 
                    ? Panier.ToList() 
                    : Panier.Where(i => 
                        (promo.ProduitId != null && i.Produit.Id == promo.ProduitId) ||
                        (promo.Categorie != null && i.Produit.Categorie == promo.Categorie)
                    ).ToList();

                if (!targetItems.Any() && promo.TypePromotion != "offre_combine") continue;

                switch (promo.TypePromotion)
                {
                    case "remise_produit":
                        foreach (var item in targetItems)
                        {
                            decimal r = promo.IsPourcentage ? (item.TotalLigneStandard * promo.Valeur / 100) : promo.Valeur;
                            item.RemiseAuto += r;
                            item.PromotionAppliquee = promo.Nom;
                        }
                        break;

                    case "offre_combine":
                        if (promo.BundleItems == null || !promo.BundleItems.Any()) break;
                        int maxBundles = int.MaxValue;
                        foreach (var bi in promo.BundleItems)
                        {
                            var cartItems = Panier.Where(i => i.ProduitId == bi.ProduitId).ToList();
                            decimal totalQty = cartItems.Sum(i => i.Quantite);
                            maxBundles = Math.Min(maxBundles, (int)(totalQty / bi.QuantiteRequise));
                        }
                        if (maxBundles <= 0) break;
                        var participantIds = promo.BundleItems.Select(bi => bi.ProduitId).ToList();
                        var bundleLines = Panier.Where(i => i.ProduitId.HasValue && participantIds.Contains(i.ProduitId.Value)).ToList();
                        decimal originalBundleTotal = 0;
                        foreach (var bi in promo.BundleItems)
                        {
                            var prod = Produits.FirstOrDefault(p => p.Id == bi.ProduitId);
                            originalBundleTotal += (prod?.PrixVente ?? 0) * bi.QuantiteRequise;
                        }
                        if (originalBundleTotal <= 0) break;
                        foreach (var bi in promo.BundleItems)
                        {
                            decimal remainingQtyToDiscount = maxBundles * bi.QuantiteRequise;
                            var itemsForThisProd = bundleLines.Where(l => l.ProduitId == bi.ProduitId).OrderByDescending(l => l.Quantite).ToList();
                            foreach (var line in itemsForThisProd)
                            {
                                if (remainingQtyToDiscount <= 0) break;
                                decimal qtyToDiscountOnThisLine = Math.Min(line.Quantite, remainingQtyToDiscount);
                                decimal originalLinePrice = line.PrixUnitaire;
                                decimal ratio = promo.Valeur / originalBundleTotal;
                                decimal discountedUnitPrice = originalLinePrice * ratio;
                                decimal discountAmount = qtyToDiscountOnThisLine * (originalLinePrice - discountedUnitPrice);
                                line.RemiseAuto += discountAmount;
                                line.PromotionAppliquee = string.IsNullOrEmpty(line.PromotionAppliquee) ? promo.Nom : line.PromotionAppliquee + " + " + promo.Nom;
                                remainingQtyToDiscount -= qtyToDiscountOnThisLine;
                            }
                        }
                        break;

                    case "quantite_offerte":
                        if (promo.SeuilQuantite > 0 && promo.QuantiteOfferte > 0)
                        {
                            foreach (var item in targetItems)
                            {
                                decimal fullSetSize = promo.SeuilQuantite.Value + promo.QuantiteOfferte.Value;
                                int sets = (int)(item.Quantite / fullSetSize);
                                if (sets > 0)
                                {
                                    decimal r = sets * promo.QuantiteOfferte.Value * item.PrixUnitaire;
                                    item.RemiseAuto += r;
                                    item.PromotionAppliquee = $"{promo.Nom} ({sets} offert(s))";
                                }
                            }
                        }
                        break;

                    case "remise_ieme":
                        if (promo.IemeArticle > 0 && promo.RemiseSurIeme > 0)
                        {
                            foreach (var item in targetItems)
                            {
                                int sets = (int)(item.Quantite / promo.IemeArticle.Value);
                                if (sets > 0)
                                {
                                    decimal r = sets * (promo.IsPourcentage 
                                        ? (item.PrixUnitaire * (promo.RemiseSurIeme.Value / 100))
                                        : promo.RemiseSurIeme.Value);
                                    item.RemiseAuto += r;
                                    string unit = promo.IsPourcentage ? "%" : "€";
                                    item.PromotionAppliquee = $"{promo.Nom} (-{promo.RemiseSurIeme}{unit} sur {sets})";
                                }
                            }
                        }
                        break;

                    case "prix_degressif":
                        foreach (var item in targetItems)
                        {
                            var bestTier = promo.Tiers
                                .Where(t => item.Quantite >= t.QuantiteMin)
                                .OrderByDescending(t => t.QuantiteMin)
                                .FirstOrDefault();
                            if (bestTier != null && bestTier.PrixUnitaire < item.PrixUnitaire)
                            {
                                decimal standardTotal = item.TotalLigneStandard;
                                decimal degressifTotal = bestTier.PrixUnitaire * item.Quantite;
                                item.RemiseAuto += (standardTotal - degressifTotal);
                                item.PromotionAppliquee = $"{promo.Nom} ({bestTier.PrixUnitaire:C}/u)";
                            }
                        }
                        break;
                }
            }
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

                if (scope == SystemeCaisse.UI.Views.DiscountScope.Item)
                {
                    if (Panier.Count == 0) return;
                    var itemWindow = new SystemeCaisse.UI.Views.CartItemSelectionWindow(Panier);
                    SetupWindowOwner(itemWindow);
                    if (itemWindow.ShowDialog() != true) return;
                    targetItem = itemWindow.SelectedItem;
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

            // Notify UI summaries
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
