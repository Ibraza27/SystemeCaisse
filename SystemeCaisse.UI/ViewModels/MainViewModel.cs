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
using Views = SystemeCaisse.UI.Views;
using SystemeCaisse.UI.Services;
using SystemeCaisse.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemeCaisse.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly AppDbContext _context;

        private List<Promotion> _activePromotions = new();
        
        public ObservableCollection<Produit> Produits { get; set; }
        public ObservableCollection<Produit> TopProducts { get; set; }
        public ICollectionView ProductsView { get; private set; }
        public ObservableCollection<CartItemViewModel> Panier { get; set; }
        
        public ProductsViewModel ProductsVM { get; private set; }
        public StocksViewModel StocksVM { get; private set; }
        public HistoryViewModel HistoryVM { get; private set; }
        public SettingsViewModel SettingsVM { get; private set; }
        public DashboardViewModel DashboardVM { get; private set; }
        public PromotionsViewModel PromotionsVM { get; private set; }
        public InventoryViewModel InventoryVM { get; private set; }
        public AnalysisViewModel AnalysisVM { get; private set; }
        
        public ObservableCollection<Produit> SearchSuggestions { get; } = new ObservableCollection<Produit>();

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
                        AddToCart(_selectedSearchProduct);
                        // Clear search after selection
                        _selectedSearchProduct = null;
                        OnPropertyChanged(nameof(SelectedSearchProduct));
                        SearchText = string.Empty;
                    }
                }
            }
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
                    UpdateSearchSuggestions();
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
            });
        }

        private decimal _total;
        public decimal Total
        {
            get => _total;
            set
            {
                _total = value;
                OnPropertyChanged();
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
                MonnaieRendre = Math.Max(0, MontantRecu - Total);
                MontantCarte = 0;
            }
            else if (SelectedPaiementMode == "Mixte")
            {
                MonnaieRendre = 0;
                // If paid more in cash than total, it's just a cash payment with change, but Mixte implies specific intent.
                // Let's assume user inputs exact cash they have.
                MontantCarte = Math.Max(0, Total - MontantRecu);
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

        public ICommand AddToCartCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand CheckoutCommand { get; }
        public ICommand PaymentModeCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand OpenWeightDialogCommand { get; }
        public ICommand OpenDiscountDialogCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ViderPanierCommand { get; }

        private readonly PrintService _printService;
        private readonly IDataMigrationService _migrationService;

        private int _selectedTabIndex = 1;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    var previousIndex = _selectedTabIndex;
                    _selectedTabIndex = value;
                    OnPropertyChanged(nameof(SelectedTabIndex));

                    try 
                    {
                        // 1. If leaving Analysis tab, notify VM immediately (Synchronous signal is CRITICAL to avoid deadlocks)
                        if (previousIndex == 5 && _selectedTabIndex != 5)
                        {
                            AnalysisVM.Cleanup();
                        }

                        // 2. If switching to Analysis tab (5), refresh data
                        if (_selectedTabIndex == 5)
                        {
                            _ = AnalysisVM.LoadAnalysis();
                        }

                        // 3. If switching back to Caisse tab (1), refresh promotions
                        if (_selectedTabIndex == 1)
                        {
                            LoadPromotions();
                            ApplyAutomaticPromotions();
                            UpdateTotal();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"TabSwitch Error: {ex.Message}");
                    }
                }
            }
        }

        public MainViewModel(IDbContextFactory<AppDbContext> contextFactory, PrintService printService, IDataMigrationService migrationService)
        {
            _context = contextFactory.CreateDbContext();
            _contextFactory = contextFactory;
            _printService = printService;
            _migrationService = migrationService;
            
            // Load products from DB
            _context.Produits.Load();
            Produits = _context.Produits.Local.ToObservableCollection();
            
            // Calculate Sales Counts & Sort (and refresh view when done)
            Task.Run(async () => 
            {
                await CalculateProductPopularity();
                Application.Current.Dispatcher.BeginInvoke(new Action(() => ProductsView.Refresh()), System.Windows.Threading.DispatcherPriority.Background);
            });

            ProductsView = CollectionViewSource.GetDefaultView(Produits);
            ProductsView.Filter = FilterProducts;
            ProductsView.SortDescriptions.Clear();
            ProductsView.SortDescriptions.Add(new SortDescription("ValidatedSalesCount", ListSortDirection.Descending));
            ProductsView.SortDescriptions.Add(new SortDescription("Nom", ListSortDirection.Ascending));

            // Load Entreprise Info
            LoadEntrepriseInfo();
            
            // Initialize Child ViewModels
            ProductsVM = new ProductsViewModel(contextFactory);
            StocksVM = new StocksViewModel(contextFactory);
            HistoryVM = new HistoryViewModel(contextFactory, _printService);
            SettingsVM = new SettingsViewModel(contextFactory, migrationService);
            DashboardVM = new DashboardViewModel(contextFactory);
            PromotionsVM = new PromotionsViewModel(contextFactory);
            InventoryVM = new InventoryViewModel(contextFactory);
            AnalysisVM = new AnalysisViewModel(contextFactory);

            AvailablePromotions = new ObservableCollection<Promotion>();
            LoadPromotions();
            
            Panier = new ObservableCollection<CartItemViewModel>();
            TopProducts = new ObservableCollection<Produit>();
            AddToCartCommand = new BasicRelayCommand(AddToCart);
            RemoveItemCommand = new BasicRelayCommand(RemoveItem);
            CheckoutCommand = new BasicRelayCommand(Checkout, _ => Panier.Count > 0);
            PaymentModeCommand = new BasicRelayCommand(SetPaymentMode);
            CancelCommand = new BasicRelayCommand(_ => ResetSale());
            ViderPanierCommand = new BasicRelayCommand(_ => ResetSale());
            ScanCommand = new BasicRelayCommand(_ => HandleScan());
            EditQuantityCommand = new BasicRelayCommand(EditQuantity);
            ClearSearchCommand = new BasicRelayCommand(_ => SearchText = string.Empty);
            
            OpenWeightDialogCommand = new BasicRelayCommand(_ => 
            {
                var weightProducts = Produits.Where(p => string.Equals(p.TypeVente, "poids", StringComparison.OrdinalIgnoreCase)).ToList();
                var selectDialog = new Views.ProductSelectionWindow(weightProducts);
                if (selectDialog.ShowDialog() == true && selectDialog.SelectedProduct != null)
                {
                    AddToCart(selectDialog.SelectedProduct);
                }
            });



            OpenDiscountDialogCommand = new BasicRelayCommand(_ => ApplyManualDiscount());
            
            EditQuantityCommand = new BasicRelayCommand(EditQuantity);
            
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
                    .Select(l => new { l.ProduitId, l.Quantite })
                    .ToListAsync();

                var salesStats = allLines
                    .GroupBy(l => l.ProduitId)
                    .Select(g => new { Id = g.Key, Count = g.Sum(x => x.Quantite) })
                    .ToList();
                
                // Update local instances
                foreach (var p in Produits)
                {
                    var stat = salesStats.FirstOrDefault(s => s.Id == p.Id);
                    p.ValidatedSalesCount = stat?.Count ?? 0;
                }

                // Update Top 20
                var top20 = salesStats
                    .OrderByDescending(s => s.Count)
                    .Select(s => Produits.FirstOrDefault(p => p.Id == s.Id))
                    .Where(p => p != null && p.Actif) // Only active products
                    .Take(20)
                    .ToList();

                Application.Current.Dispatcher.BeginInvoke(new Action(() => 
                {
                    TopProducts.Clear();
                    foreach (var p in top20) TopProducts.Add(p!);
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
                Total = Total,
                Label = $"Panier du {DateTime.Now:HH:mm} ({Panier.Count} art. - {Total:C})"
            };

            SuspendedSales.Add(sale);
            ResetSale();
            MessageBox.Show("Vente mise en attente.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ResumeSale(object parameter)
        {
            if (parameter is SuspendedSale sale)
            {
                if (Panier.Count > 0)
                {
                    if (MessageBox.Show("Un panier est en cours. L'écraser ?", "Attention", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                        return;
                }

                Panier.Clear();
                foreach (var item in sale.Items)
                {
                    Panier.Add(item);
                }
                SuspendedSales.Remove(sale);
                UpdateTotal();
            }
        }

        private void HandleScan()
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
                AddToCart(product);
                SearchText = string.Empty; // Clear after successful scan
            }
            else
            {
                // Product Not Found logic
                var dialog = new Views.ProductNotFoundWindow(code);
                if (dialog.ShowDialog() == true && dialog.AddRequested)
                {
                    // Fetch existing categories for the autocomplete
                    var categories = Produits
                        .Select(p => p.Categorie)
                        .Where(c => !string.IsNullOrEmpty(c))
                        .Distinct()
                        .OrderBy(c => c)
                        .ToList();

                    var addDialog = new Views.QuickAddProductWindow(code, categories);
                    if (addDialog.ShowDialog() == true && addDialog.NewProduct != null)
                    {
                        var newProd = addDialog.NewProduct;

                        // Save to database
                        using (var context = _contextFactory.CreateDbContext())
                        {
                            context.Produits.Add(newProd);
                            context.SaveChanges();
                        }

                        // Add to local observable collection so it appears in UI
                        Produits.Add(newProd);

                        // Automatically add to cart
                        AddToCart(newProd);
                        SearchText = string.Empty;
                        
                        // Force refresh of the TopProducts and Search suggestions if needed
                        UpdateSearchSuggestions();
                    }
                }
            }
        }
        
        private void EditQuantity(object parameter)
        {
            if (parameter is CartItemViewModel item)
            {
                // DEBUG: Confirm command execution
                // MessageBox.Show($"Modification quantité pour : {item.ProduitNom}", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                
                var dialog = new Views.QuantityInputWindow(item.Quantite);
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

        private void Checkout(object parameter)
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
                    NumeroTicket = DateTime.Now.Ticks.ToString().Substring(10), // Shorter ticket number
                    MoyenPaiement = SelectedPaiementMode?.ToLower(),
                    MontantEspeces = SelectedPaiementMode == "Especes" ? MontantRecu : (SelectedPaiementMode == "Mixte" ? MontantRecu : 0),
                    MontantCB = SelectedPaiementMode == "CB" ? Total : (SelectedPaiementMode == "Mixte" ? MontantCarte : 0),
                    MonnaieRendue = MonnaieRendre,
                    Statut = "validee"
                };

                // Add Payment Details check
                if (SelectedPaiementMode == "Especes" && MontantRecu < Total && Total > 0)
                {
                    MessageBox.Show("Montant reçu insuffisant !", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Training Mode Check
                if (SettingsVM.IsTrainingMode)
                {
                    if (MessageBox.Show("MODE FORMATION ACTIVE.\nLa vente ne sera pas enregistrée.\nContinuer ?", "Mode Formation", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                        return;

                     // Printing (Always ask or force?)
                    if (MessageBox.Show("Simuler l'impression du ticket ?", "Impression Formation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _ = Task.Run(() => _printService.PrintTicket(vente, CurrentEntreprise ?? new Entreprise { Nom = "Inconnu" }, true));
                    }
                    
                    ResetSale();
                    return;
                }

                // ... (Existing LigneVente logic) ...

                foreach (var item in Panier)
                {
                    // Capture data BEFORE modifying the entity (because item.Produit delegates to the entity)
                    int productId = item.Produit.Id;
                    decimal quantity = item.Quantite;
                    decimal price = item.Produit.PrixVente;
                    string productNom = item.ProduitNom;
                    
                    var ligne = item.ToEntity();
                    ligne.Produit = null; // Now safer to nullify for EF
                    vente.Lignes.Add(ligne);

                    // 1. Stock Movement Logic
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

                    // 2. Update Stock
                    var productToUpdate = _context.Produits.FirstOrDefault(p => p.Id == productId);
                    if (productToUpdate != null)
                    {
                         productToUpdate.StockActuel -= quantity;
                    }
                }

                _context.Ventes.Add(vente);
                _context.SaveChanges();

                BasketRemiseManuelle = 0; // Reset manual discount after success
                // Capture change
                decimal changeToReturn = MonnaieRendre;

                // Open Summary Window (Preview, Print, Save)
                var summary = new Views.ReceiptSummaryWindow(vente, CurrentEntreprise ?? new Entreprise { Nom = "Inconnu" }, changeToReturn, false);
                summary.ShowDialog();

                // Reset
                ResetSale();
                
                if (changeToReturn > 0)
                    MessageBox.Show($"Monnaie à rendre : {changeToReturn:C}", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);

                // Update Dashboard & Products Stats in background
                _ = Task.Run(async () => 
                {
                    using var context = _contextFactory.CreateDbContext();
                    await CalculateProductPopularity(); 
                    
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => 
                    {
                        DashboardVM.LoadDashboardDataAsync().ConfigureAwait(false);
                        ProductsView.Refresh();
                        StocksVM.LoadData();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Erreur lors de l'enregistrement : {ex.Message}", "Erreur", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void AddToCart(object parameter)
        {
            if (parameter is Produit produit)
            {
                // DEBUG: Check correct product identification
                // System.Diagnostics.Debug.WriteLine($"AddToCart: {produit.Nom}, Type: {produit.TypeVente}, ReturnMode: {IsReturnMode}");

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
                    // Case-insensitive check and trim
                    // Case-insensitive check and trim
                    bool isWeight = string.Equals(produit.TypeVente?.Trim(), "poids", StringComparison.OrdinalIgnoreCase);

                    if (isWeight)
                    {
                        var dialog = new Views.WeightInputWindow(produit);
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
            // Only merge if unit product. Weight products might be distinct lines depending on requirement.
            // Usually weight products are merged too if same product.
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
                    CategorieNom = produit.Categorie ?? "Divers",
                    PrixUnitaire = produit.PrixVente,
                    Quantite = qty,
                    TotalLigne = qty * produit.PrixVente
                };
                
                var cartItem = new CartItemViewModel(ligne);
                // Standard collection changed logic will handle property changes
                Panier.Add(cartItem);
            }
        }

        private void ResetSale()
        {
            foreach (var item in Panier)
            {
                item.RemiseManuellePercent = 0;
                item.RemiseManuelleFixed = 0;
            }
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
                    .Where(p => p.Actif && p.DateDebut <= now && p.DateFin >= now)
                    .AsNoTracking() // Better performance for POS read-only promotions
                    .ToList();
            }
            catch { }
        }

        private void ApplyAutomaticPromotions()
        {
            foreach (var item in Panier)
            {
                item.RemiseAuto = 0;
                item.PromotionAppliquee = null;
            }

            if (_activePromotions == null || !_activePromotions.Any()) return;

            // Sort non-cumulative last or per your logic? For now, simple run
            foreach (var promo in _activePromotions.Where(p => p.TypePromotion != "remise_total" && p.TypePromotion != "seuil_panier"))
            {
                // Bundles don't necessarily have a single ProductId or Category
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

                        // 1. Calculate how many full bundles we have
                        int maxBundles = int.MaxValue;
                        foreach (var bi in promo.BundleItems)
                        {
                            var cartItems = Panier.Where(i => i.ProduitId == bi.ProduitId).ToList();
                            decimal totalQty = cartItems.Sum(i => i.Quantite);
                            maxBundles = Math.Min(maxBundles, (int)(totalQty / bi.QuantiteRequise));
                        }

                        if (maxBundles <= 0) break;

                        // 2. Identify all participating lines
                        var participantIds = promo.BundleItems.Select(bi => bi.ProduitId).ToList();
                        var bundleLines = Panier.Where(i => i.ProduitId.HasValue && participantIds.Contains(i.ProduitId.Value)).ToList();

                        // 3. Calculate original price for a single bundle
                        decimal originalBundleTotal = 0;
                        foreach (var bi in promo.BundleItems)
                        {
                            var prod = _context.Produits.FirstOrDefault(p => p.Id == bi.ProduitId); // Assuming _context is available or passed
                            originalBundleTotal += (prod?.PrixVente ?? 0) * bi.QuantiteRequise;
                        }

                        if (originalBundleTotal <= 0) break;

                        // 4. Calculate total target price and total original price for ALL groups
                        decimal targetTotalForAll = maxBundles * promo.Valeur;
                        decimal originalTotalForAll = maxBundles * originalBundleTotal;
                        decimal totalDiscount = originalTotalForAll - targetTotalForAll;

                        if (totalDiscount <= 0) break;

                        // 5. Apply pro-rata discount to participating items (up to required quantity)
                        foreach (var bi in promo.BundleItems)
                        {
                            decimal remainingQtyToDiscount = maxBundles * bi.QuantiteRequise;
                            var itemsForThisProd = bundleLines.Where(l => l.ProduitId == bi.ProduitId).OrderByDescending(l => l.Quantite).ToList();
                            
                            foreach (var line in itemsForThisProd)
                            {
                                if (remainingQtyToDiscount <= 0) break;

                                decimal qtyToDiscountOnThisLine = Math.Min(line.Quantite, remainingQtyToDiscount);
                                decimal originalLinePrice = line.PrixUnitaire; // Base price
                                
                                // Discount proportion for this specific product based on its contribution to bundle total
                                // decimal productBundleWeight = (originalLinePrice * bi.QuantiteRequise) / originalBundleTotal;
                                // decimal totalDiscountForThisProductInAllBundles = totalDiscount * (bi.QuantiteRequise * maxBundles / (bi.QuantiteRequise * maxBundles)) ; // simplify later

                                // Let's use simpler pro-rata: newPrice = originalPrice * (targetBundleTotal / originalBundleTotal)
                                decimal ratio = promo.Valeur / originalBundleTotal;
                                decimal discountedUnitPrice = originalLinePrice * ratio;

                                // We split the line if only partial quantity is discounted? 
                                // Simplification: apply partially to the total line value
                                // decimal currentTotal = line.TotalLigne; // This is TotalLigneStandard
                                decimal discountAmount = qtyToDiscountOnThisLine * (originalLinePrice - discountedUnitPrice);
                                
                                line.RemiseAuto += discountAmount; // Apply as automatic discount
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
                var mainWindow = Application.Current.MainWindow;

                // Step 1: Selection Window (Scope and Type)
                var selectWindow = new Views.ManualDiscountSelectionWindow();
                if (mainWindow != null && selectWindow != mainWindow) selectWindow.Owner = mainWindow;
                if (selectWindow.ShowDialog() != true) return;

                var scope = selectWindow.SelectedScope;
                var type = selectWindow.SelectedType;

                CartItemViewModel? targetItem = null;

                // Step 2: Item Selection (optional)
                if (scope == Views.DiscountScope.Item)
                {
                    if (Panier.Count == 0)
                    {
                        MessageBox.Show("Le panier est vide.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var itemWindow = new Views.CartItemSelectionWindow(Panier);
                    if (mainWindow != null && itemWindow != mainWindow) itemWindow.Owner = mainWindow;
                    if (itemWindow.ShowDialog() != true) return;
                    targetItem = itemWindow.SelectedItem;
                }

                // Step 3: Value Input
                string title = (scope == Views.DiscountScope.Basket ? "Remise Panier" : $"Remise sur {targetItem?.ProduitNom}");
                string unit = (type == Views.DiscountType.Percentage ? "%" : "€");
                
                var valueWindow = new Views.DiscountValueInputWindow(title, unit);
                if (mainWindow != null && valueWindow != mainWindow) valueWindow.Owner = mainWindow;
                if (valueWindow.ShowDialog() != true) return;

                decimal val = valueWindow.DiscountValue;

                // Step 4: Apply
                if (scope == Views.DiscountScope.Basket)
                {
                    if (type == Views.DiscountType.Percentage)
                    {
                        decimal baseTotal = Panier.Sum(i => i.TotalLigneStandard);
                        BasketRemiseManuelle = baseTotal * val / 100;
                    }
                    else
                    {
                        BasketRemiseManuelle = val;
                    }
                }
                else if (targetItem != null)
                {
                    if (type == Views.DiscountType.Percentage)
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
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'application de la remise : {ex.Message}\n{ex.StackTrace}", "Erreur Critique", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(TotalRemise));
            OnPropertyChanged(nameof(TotalSansRemise));
            OnPropertyChanged(nameof(MonnaieRendre));
        }
    }


    public class SuspendedSale
    {
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal Total { get; set; }
        public string Label { get; set; } = string.Empty;
        
        public override string ToString() => Label;
    }
}
