using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.ViewModels
{
    public class CommandesViewModel : INotifyPropertyChanged
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly PrintService _printService;
        private MainViewModel? _mainViewModel;
        private AppDbContext? _context;
        private int? _editingCommandeId = null; // Track if we're editing an existing commande

        // ─── List & Filters ───
        public ObservableCollection<Commande> Commandes { get; set; } = new();

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); _ = LoadDataAsync(); }
        }

        private string _statutFilter = "Tous";
        public string StatutFilter
        {
            get => _statutFilter;
            set { _statutFilter = value; OnPropertyChanged(); _ = LoadDataAsync(); }
        }

        private string _paiementFilter = "Tous";
        public string PaiementFilter
        {
            get => _paiementFilter;
            set { _paiementFilter = value; OnPropertyChanged(); _ = LoadDataAsync(); }
        }

        // Ville/CP multi-filter
        private List<string> _villeCPFilterList = new();
        public List<string> VilleCPFilterList
        {
            get => _villeCPFilterList;
            set { _villeCPFilterList = value; OnPropertyChanged(); OnPropertyChanged(nameof(VilleCPFilterDisplay)); _ = LoadDataAsync(); }
        }

        public string VilleCPFilterDisplay => _villeCPFilterList.Count == 0 ? "Tous" : string.Join(", ", _villeCPFilterList.Take(3)) + (_villeCPFilterList.Count > 3 ? $" (+{_villeCPFilterList.Count - 3})" : "");

        private Commande? _selectedCommande;
        public Commande? SelectedCommande
        {
            get => _selectedCommande;
            set { _selectedCommande = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDetailVisible)); }
        }

        public bool IsDetailVisible => SelectedCommande != null;

        // ─── New Commande mode ───
        private bool _isNewCommandeMode;
        public bool IsNewCommandeMode
        {
            get => _isNewCommandeMode;
            set { _isNewCommandeMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsListMode)); }
        }
        public bool IsListMode => !IsNewCommandeMode;

        // New commande panier
        public ObservableCollection<CartItemViewModel> CommandePanier { get; set; } = new();
        private List<Promotion>? _activePromotions;

        // Hold client info between modal and panier
        private string _clientNom = string.Empty;
        private string _clientPrenom = string.Empty;
        private string _clientTelephone = string.Empty;
        private string _clientAdresse = string.Empty;
        private string _clientVille = string.Empty;
        private string _clientCodePostal = string.Empty;

        private bool _avecLivraison;
        public bool AvecLivraison
        {
            get => _avecLivraison;
            set { _avecLivraison = value; OnPropertyChanged(); if (!value) MontantLivraison = 0; UpdateCommandeTotal(); }
        }

        private decimal _montantLivraison;
        public decimal MontantLivraison
        {
            get => _montantLivraison;
            set { _montantLivraison = value; OnPropertyChanged(); UpdateCommandeTotal(); }
        }

        private decimal _montantPaye;
        public decimal MontantPaye
        {
            get => _montantPaye;
            set { _montantPaye = value; OnPropertyChanged(); UpdateCommandeTotal(); }
        }

        private decimal _commandeTotal;
        public decimal CommandeTotal
        {
            get => _commandeTotal;
            set { _commandeTotal = value; OnPropertyChanged(); }
        }

        private decimal _commandeTotalAvecLivraison;
        public decimal CommandeTotalAvecLivraison
        {
            get => _commandeTotalAvecLivraison;
            set { _commandeTotalAvecLivraison = value; OnPropertyChanged(); }
        }

        private decimal _commandeRestant;
        public decimal CommandeRestant
        {
            get => _commandeRestant;
            set { _commandeRestant = value; OnPropertyChanged(); }
        }

        private decimal _commandeTotalRemise;
        public decimal CommandeTotalRemise
        {
            get => _commandeTotalRemise;
            set { _commandeTotalRemise = value; OnPropertyChanged(); }
        }

        private decimal _commandeTotalSansRemise;
        public decimal CommandeTotalSansRemise
        {
            get => _commandeTotalSansRemise;
            set { _commandeTotalSansRemise = value; OnPropertyChanged(); }
        }

        // Products list for new commande
        public ObservableCollection<Produit> Produits { get; set; } = new();

        // Suspended commandes (attente)
        public ObservableCollection<SuspendedCommande> SuspendedCommandes { get; set; } = new();
        public bool HasSuspendedCommandes => SuspendedCommandes.Count > 0;

        // Search suggestions for new commande
        public ObservableCollection<Produit> SearchSuggestions { get; set; } = new();
        private string _newCommandeSearchText = string.Empty;
        public string NewCommandeSearchText
        {
            get => _newCommandeSearchText;
            set { _newCommandeSearchText = value; OnPropertyChanged(); UpdateNewCommandeSearchSuggestions(); }
        }

        private bool _isSearchDropDownOpen;
        public bool IsSearchDropDownOpen
        {
            get => _isSearchDropDownOpen;
            set { _isSearchDropDownOpen = value; OnPropertyChanged(); }
        }

        private Produit? _selectedSearchProduct;
        public Produit? SelectedSearchProduct
        {
            get => _selectedSearchProduct;
            set 
            { 
                if (value != null)
                {
                    _selectedSearchProduct = value;
                    OnPropertyChanged();
                    AddToCommandePanier(value);
                    NewCommandeSearchText = string.Empty;
                    IsSearchDropDownOpen = false;
                    SearchSuggestions.Clear();
                    _selectedSearchProduct = null;
                }
            }
        }

        // ─── Commands ───
        public ICommand LoadCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand NewCommandeCommand { get; }
        public ICommand CancelNewCommandeCommand { get; }
        public ICommand ValidateCommandeCommand { get; }
        public ICommand ViewTicketCommand { get; }
        public ICommand PrintTicketCommand { get; }
        public ICommand ChangeStatusCommand { get; }
        public ICommand DeleteCommandeCommand { get; }
        public ICommand AddPaymentCommand { get; }
        public ICommand EditCommandeCommand { get; }
        public ICommand AddToCommandePanierCommand { get; }
        public ICommand RemoveCommandeItemCommand { get; }
        public ICommand ViderCommandePanierCommand { get; }
        public ICommand FillExactAmountCommand { get; }
        public ICommand ScanNewCommandeCommand { get; }
        public ICommand ClearNewCommandeSearchCommand { get; }
        public ICommand OpenVilleCPFilterCommand { get; }
        public ICommand ClearAllFiltersCommand { get; }
        public ICommand PrintRecapCommand { get; }
        public ICommand OpenCommandeDiscountCommand { get; }
        public ICommand EditCommandeQuantityCommand { get; }
        public ICommand SuspendCommandeCommand { get; }
        public ICommand ResumeCommandeCommand { get; }

        public CommandesViewModel(IDbContextFactory<AppDbContext> contextFactory, PrintService printService)
        {
            _contextFactory = contextFactory;
            _printService = printService;

            LoadCommand = new BasicRelayCommand(_ => _ = LoadDataAsync());
            ClearSearchCommand = new BasicRelayCommand(_ => SearchText = string.Empty);

            NewCommandeCommand = new BasicRelayCommand(_ => StartNewCommande());
            CancelNewCommandeCommand = new BasicRelayCommand(_ => CancelNewCommande());
            ValidateCommandeCommand = new BasicRelayCommand(_ => OpenClientInfoWindow(), _ => CommandePanier.Count > 0);

            AddToCommandePanierCommand = new BasicRelayCommand(p => { if (p is Produit prod) AddToCommandePanier(prod); });
            RemoveCommandeItemCommand = new BasicRelayCommand(p => { if (p is CartItemViewModel item) { CommandePanier.Remove(item); UpdateCommandeTotal(); } });
            ViderCommandePanierCommand = new BasicRelayCommand(_ => { CommandePanier.Clear(); MontantPaye = 0; AvecLivraison = false; MontantLivraison = 0; UpdateCommandeTotal(); });
            FillExactAmountCommand = new BasicRelayCommand(_ => MontantPaye = CommandeTotalAvecLivraison);
            ScanNewCommandeCommand = new BasicRelayCommand(_ => HandleNewCommandeScan());
            ClearNewCommandeSearchCommand = new BasicRelayCommand(_ => NewCommandeSearchText = string.Empty);
            OpenVilleCPFilterCommand = new BasicRelayCommand(_ => OpenVilleCPFilter());
            ClearAllFiltersCommand = new BasicRelayCommand(_ => ClearAllFilters());
            PrintRecapCommand = new BasicRelayCommand(_ => PrintRecap(), _ => Commandes.Count > 0);
            OpenCommandeDiscountCommand = new BasicRelayCommand(_ => ApplyManualDiscount(), _ => CommandePanier.Count > 0);
            EditCommandeQuantityCommand = new BasicRelayCommand(p => EditCommandeQuantity(p));
            SuspendCommandeCommand = new BasicRelayCommand(_ => SuspendCommande(), _ => CommandePanier.Count > 0);
            ResumeCommandeCommand = new BasicRelayCommand(p => ResumeCommande(p));

            ViewTicketCommand = new BasicRelayCommand(_ => ViewCommandeTicket(), _ => SelectedCommande != null);
            PrintTicketCommand = new BasicRelayCommand(_ => PrintCommandeTicket(), _ => SelectedCommande != null);

            ChangeStatusCommand = new BasicRelayCommand(p =>
            {
                if (p is string status && SelectedCommande != null) ChangeStatus(status);
            }, _ => SelectedCommande != null);

            DeleteCommandeCommand = new BasicRelayCommand(_ => DeleteCommande(), _ => SelectedCommande != null);
            AddPaymentCommand = new BasicRelayCommand(_ => AddPayment(), _ => SelectedCommande != null && SelectedCommande.Restant > 0);
            EditCommandeCommand = new BasicRelayCommand(_ => EditCommande(), _ => SelectedCommande != null && SelectedCommande.Statut != "annulee");

            CommandePanier.CollectionChanged += (s, e) =>
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
                                ApplyCommandePromotions();
                                UpdateCommandeTotal();
                            }
                        };
                    }
                }
                ApplyCommandePromotions();
                UpdateCommandeTotal();
            };
        }

        public void SetMainViewModel(MainViewModel mainVM)
        {
            _mainViewModel = mainVM;
        }

        public async Task InitializeAsync()
        {
            CommuneService.Load();
            LoadPromotions();
            await LoadDataAsync();
            LoadProducts();
        }

        // ─── TopProducts: delegate to MainViewModel ───
        public ObservableCollection<Produit> TopProducts => _mainViewModel?.TopProducts ?? new ObservableCollection<Produit>();

        // ─── Data Loading ───
        private void LoadProducts()
        {
            try
            {
                using var ctx = _contextFactory.CreateDbContext();
                var products = ctx.Produits.OrderBy(p => p.Nom).ToList();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Produits.Clear();
                    foreach (var p in products) Produits.Add(p);
                    OnPropertyChanged(nameof(TopProducts));
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CommandesVM.LoadProducts error: {ex.Message}");
            }
        }

        private void LoadPromotions()
        {
            try
            {
                using var ctx = _contextFactory.CreateDbContext();
                var now = DateTime.Now;
                _activePromotions = ctx.Promotions
                    .Include(p => p.Tiers)
                    .Include(p => p.BundleItems)
                    .Where(p => p.Actif && p.DateDebut <= now && (p.DateFin == null || p.DateFin >= now))
                    .AsNoTracking()
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CommandesVM.LoadPromotions error: {ex.Message}");
            }
        }

        public async Task LoadDataAsync()
        {
            await Task.Run(() =>
            {
                _context?.Dispose();
                _context = _contextFactory.CreateDbContext();

                var query = _context.Commandes
                    .Include(c => c.Lignes)
                    .AsQueryable();

                // Statut filter
                if (StatutFilter != "Tous")
                {
                    string statutKey = StatutFilter switch
                    {
                        "En attente" => "en_attente",
                        "Traitée" => "traitee",
                        "Annulée" => "annulee",
                        _ => ""
                    };
                    if (!string.IsNullOrEmpty(statutKey))
                        query = query.Where(c => c.Statut == statutKey);
                }

                // Paiement filter (must evaluate in memory due to computed property)
                var commandesList = query.OrderByDescending(c => c.CreatedAt).ToList();

                if (PaiementFilter != "Tous")
                {
                    commandesList = PaiementFilter switch
                    {
                        "Réglé" => commandesList.Where(c => c.StatutPaiement == "regle").ToList(),
                        "Partiel" => commandesList.Where(c => c.StatutPaiement == "partiel").ToList(),
                        "Non réglé" => commandesList.Where(c => c.StatutPaiement == "non_regle").ToList(),
                        _ => commandesList
                    };
                }

                // Ville/CP filter
                if (VilleCPFilterList.Count > 0)
                {
                    var villeCpValues = VilleCPFilterList.Select(v =>
                    {
                        var parts = v.Split('—');
                        return parts.Length >= 2 ? parts[0].Trim() : v.Trim();
                    }).ToList();

                    commandesList = commandesList.Where(c =>
                        villeCpValues.Any(v => (c.CodePostal ?? "").StartsWith(v) || (c.Ville ?? "").ToUpper().Contains(v.ToUpper()))
                    ).ToList();
                }

                // Search filter
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    string q = SearchText.Trim().ToLower();
                    commandesList = commandesList.Where(c =>
                        c.NumeroCommande.ToLower().Contains(q) ||
                        c.Nom.ToLower().Contains(q) ||
                        c.Prenom.ToLower().Contains(q) ||
                        c.Telephone.Contains(q)
                    ).ToList();
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Commandes.Clear();
                    foreach (var c in commandesList)
                        Commandes.Add(c);
                    OnPropertyChanged(nameof(Commandes));
                });
            });
        }

        // ─── New Commande ───
        private void StartNewCommande()
        {
            IsNewCommandeMode = true;
            CommandePanier.Clear();
            MontantPaye = 0;
            AvecLivraison = false;
            MontantLivraison = 0;
            _clientNom = _clientPrenom = _clientTelephone = _clientAdresse = _clientVille = _clientCodePostal = string.Empty;
            LoadPromotions();
            LoadProducts();
            UpdateCommandeTotal();
        }

        private void CancelNewCommande()
        {
            // If editing, restore original commande (it was never deleted)
            if (_editingCommandeId.HasValue)
            {
                _editingCommandeId = null;
            }
            IsNewCommandeMode = false;
            CommandePanier.Clear();
            MontantPaye = 0;
            AvecLivraison = false;
            MontantLivraison = 0;
            _ = LoadDataAsync();
        }

        private void AddToCommandePanier(Produit produit)
        {
            bool isWeight = string.Equals(produit.TypeVente?.Trim(), "poids", StringComparison.OrdinalIgnoreCase);

            if (isWeight)
            {
                // Open weight input window, same as Caisse
                var scaleService = _mainViewModel?.ScaleService;
                var dialog = new SystemeCaisse.UI.Views.WeightInputWindow(produit, scaleService);
                dialog.Owner = WindowHelper.GetAdminWindow();
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (dialog.ShowDialog() == true)
                {
                    var existing = CommandePanier.FirstOrDefault(i => i.Produit.Id == produit.Id);
                    if (existing != null)
                    {
                        existing.Quantite += dialog.PoidsSaisi;
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
                            Quantite = dialog.PoidsSaisi,
                            TotalLigne = produit.PrixVente * dialog.PoidsSaisi,
                            TaxTier = produit.TaxTier
                        };
                        CommandePanier.Add(new CartItemViewModel(ligne));
                    }
                }
            }
            else
            {
                var existing = CommandePanier.FirstOrDefault(i => i.Produit.Id == produit.Id);
                if (existing != null)
                {
                    existing.Quantite += 1;
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
                        Quantite = 1,
                        TotalLigne = produit.PrixVente,
                        TaxTier = produit.TaxTier
                    };
                    CommandePanier.Add(new CartItemViewModel(ligne));
                }
            }
            UpdateCommandeTotal();
        }

        private void HandleNewCommandeScan()
        {
            if (string.IsNullOrWhiteSpace(NewCommandeSearchText)) return;
            var code = NewCommandeSearchText.Trim();
            var product = Produits.FirstOrDefault(p => p.CodeBarre == code);
            if (product == null && int.TryParse(code, out int id) && id < 10000)
                product = Produits.FirstOrDefault(p => p.Id == id);
            if (product != null)
            {
                AddToCommandePanier(product);
                NewCommandeSearchText = string.Empty;
                IsSearchDropDownOpen = false;
                SearchSuggestions.Clear();
            }
        }

        private void UpdateNewCommandeSearchSuggestions()
        {
            if (string.IsNullOrWhiteSpace(NewCommandeSearchText) || NewCommandeSearchText.Length < 1)
            {
                SearchSuggestions.Clear();
                IsSearchDropDownOpen = false;
                return;
            }

            string q = NewCommandeSearchText.ToLower().Trim();
            var matches = Produits
                .Where(p => p.Nom.ToLower().Contains(q) || (p.CodeBarre != null && p.CodeBarre.Contains(q)))
                .Take(10)
                .ToList();

            SearchSuggestions.Clear();
            foreach (var m in matches) SearchSuggestions.Add(m);
            IsSearchDropDownOpen = matches.Any();
        }

        private void UpdateCommandeTotal()
        {
            CommandeTotalSansRemise = CommandePanier.Sum(i => i.TotalLigneStandard);
            CommandeTotalRemise = CommandePanier.Sum(i => i.RemiseAuto + i.RemiseManuelle);
            CommandeTotal = CommandeTotalSansRemise - CommandeTotalRemise;
            if (CommandeTotal < 0) CommandeTotal = 0;
            CommandeTotalAvecLivraison = CommandeTotal + (AvecLivraison ? MontantLivraison : 0);
            CommandeRestant = CommandeTotalAvecLivraison - MontantPaye;
            if (CommandeRestant < 0) CommandeRestant = 0;
        }

        // ─── Promotions (reuse MainViewModel logic) ───
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

        private static string AppendPromoLabel(string? existing, string newLabel)
        {
            if (string.IsNullOrEmpty(existing)) return newLabel;
            return existing + " + " + newLabel;
        }

        private void ApplyCommandePromotions()
        {
            foreach (var item in CommandePanier)
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
                        var targetItems = CommandePanier.Where(i =>
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
                            decimal totalAvailable = CommandePanier
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
                        var bundleLines = CommandePanier.Where(i => i.ProduitId.HasValue && participantIds.Contains(i.ProduitId.Value)).ToList();

                        foreach (var bi in promo.BundleItems)
                        {
                            decimal remainingQtyToDiscount = maxBundles * bi.QuantiteRequise;
                            var itemsForThisProd = bundleLines
                                .Where(l => l.ProduitId == bi.ProduitId && GetAvailableQty(l, consumedQty) > 0)
                                .OrderByDescending(l => GetAvailableQty(l, consumedQty)).ToList();

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
                            var targetItems = CommandePanier.Where(i =>
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
                            var targetItems = CommandePanier.Where(i =>
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
                        var targetItems = CommandePanier.Where(i =>
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

        // ─── Client Info Window ───
        private void OpenClientInfoWindow()
        {
            var win = new Views.CommandeClientInfoWindow(
                _clientNom, _clientPrenom, _clientTelephone, 
                _clientAdresse, _clientVille, _clientCodePostal);
            win.Owner = WindowHelper.GetAdminWindow();
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var result = win.ShowDialog();

            // Persist client info for re-opening
            _clientNom = win.ClientNom;
            _clientPrenom = win.ClientPrenom;
            _clientTelephone = win.ClientTelephone;
            _clientAdresse = win.ClientAdresse;
            _clientVille = win.ClientVille;
            _clientCodePostal = win.ClientCodePostal;

            if (result == true && win.Action == "confirm")
            {
                SaveCommande();
            }
            // "back" => just close the window, data is kept
            // "cancel" => clear everything
            else if (win.Action == "cancel")
            {
                CancelNewCommande();
            }
        }

        private void SaveCommande()
        {
            try
            {
                using var ctx = _contextFactory.CreateDbContext();

                if (_editingCommandeId.HasValue)
                {
                    // Update existing commande
                    var existing = ctx.Commandes.Include(c => c.Lignes).FirstOrDefault(c => c.Id == _editingCommandeId.Value);
                    if (existing != null)
                    {
                        existing.Nom = _clientNom;
                        existing.Prenom = _clientPrenom;
                        existing.Telephone = _clientTelephone;
                        existing.Adresse = string.IsNullOrWhiteSpace(_clientAdresse) ? null : _clientAdresse;
                        existing.Ville = string.IsNullOrWhiteSpace(_clientVille) ? null : _clientVille;
                        existing.CodePostal = string.IsNullOrWhiteSpace(_clientCodePostal) ? null : _clientCodePostal;
                        existing.Total = CommandeTotal;
                        existing.TotalRemise = CommandeTotalRemise;
                        existing.MontantPaye = MontantPaye;
                        existing.MontantLivraison = AvecLivraison ? MontantLivraison : 0;
                        existing.AvecLivraison = AvecLivraison;
                        existing.NbArticles = (int)CommandePanier.Sum(i => i.Quantite);
                        existing.UpdatedAt = DateTime.Now;

                        ctx.LignesCommande.RemoveRange(existing.Lignes);
                        existing.Lignes.Clear();

                        foreach (var item in CommandePanier)
                        {
                            existing.Lignes.Add(new LigneCommande
                            {
                                ProduitId = item.ProduitId,
                                ProduitNom = item.ProduitNom,
                                CategorieNom = item.Produit.Categorie ?? "Autre",
                                PrixUnitaire = item.PrixUnitaire,
                                Quantite = item.Quantite,
                                TotalLigne = item.TotalLigne,
                                Remise = item.RemiseAuto + item.RemiseManuelle,
                                PromotionAppliquee = item.PromotionAppliquee,
                                TaxTier = item.TaxTier
                            });
                        }

                        ctx.SaveChanges();

                        MessageBox.Show(WindowHelper.GetAdminWindow(),
                            $"Commande {existing.NumeroCommande} mise à jour !",
                            "Succès", MessageBoxButton.OK, MessageBoxImage.Information);

                        _editingCommandeId = null;
                        IsNewCommandeMode = false;
                        CommandePanier.Clear();
                        _ = LoadDataAsync();
                        return;
                    }
                }

                // Create new commande
                string dateStr = DateTime.Now.ToString("yyyyMMdd");
                int count = ctx.Commandes.Count(c => c.NumeroCommande.StartsWith($"CMD-{dateStr}")) + 1;
                string numero = $"CMD-{dateStr}-{count:D3}";

                var commande = new Commande
                {
                    NumeroCommande = numero,
                    Nom = _clientNom,
                    Prenom = _clientPrenom,
                    Telephone = _clientTelephone,
                    Adresse = string.IsNullOrWhiteSpace(_clientAdresse) ? null : _clientAdresse,
                    Ville = string.IsNullOrWhiteSpace(_clientVille) ? null : _clientVille,
                    CodePostal = string.IsNullOrWhiteSpace(_clientCodePostal) ? null : _clientCodePostal,
                    Total = CommandeTotal,
                    TotalRemise = CommandeTotalRemise,
                    MontantPaye = MontantPaye,
                    MontantLivraison = AvecLivraison ? MontantLivraison : 0,
                    AvecLivraison = AvecLivraison,
                    NbArticles = (int)CommandePanier.Sum(i => i.Quantite),
                    Statut = "en_attente",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                foreach (var item in CommandePanier)
                {
                    commande.Lignes.Add(new LigneCommande
                    {
                        ProduitId = item.ProduitId,
                        ProduitNom = item.ProduitNom,
                        CategorieNom = item.Produit.Categorie ?? "Autre",
                        PrixUnitaire = item.PrixUnitaire,
                        Quantite = item.Quantite,
                        TotalLigne = item.TotalLigne,
                        Remise = item.RemiseAuto + item.RemiseManuelle,
                        PromotionAppliquee = item.PromotionAppliquee,
                        TaxTier = item.TaxTier
                    });
                }

                ctx.Commandes.Add(commande);
                ctx.SaveChanges();

                MessageBox.Show(WindowHelper.GetAdminWindow(),
                    $"Commande {numero} créée avec succès !",
                    "Succès", MessageBoxButton.OK, MessageBoxImage.Information);

                IsNewCommandeMode = false;
                CommandePanier.Clear();
                _ = LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(WindowHelper.GetAdminWindow(),
                    $"Erreur lors de la sauvegarde : {ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── Actions on existing commande ───
        private void ViewCommandeTicket()
        {
            if (SelectedCommande == null) return;
            var entreprise = GetEntreprise();
            var win = new Views.CommandeReceiptWindow(SelectedCommande, entreprise, _printService);
            win.Owner = WindowHelper.GetAdminWindow();
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowDialog();
        }

        private void PrintCommandeTicket()
        {
            if (SelectedCommande == null) return;
            var entreprise = GetEntreprise();
            _printService.PrintCommandeTicket(SelectedCommande, entreprise);
        }

        private void ChangeStatus(string newStatus)
        {
            if (SelectedCommande == null) return;
            string displayStatus = newStatus switch
            {
                "traitee" => "Traitée",
                "annulee" => "Annulée",
                _ => newStatus
            };

            if (MessageBox.Show(WindowHelper.GetAdminWindow(),
                $"Passer la commande {SelectedCommande.NumeroCommande} en \"{displayStatus}\" ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                using var ctx = _contextFactory.CreateDbContext();
                var cmd = ctx.Commandes.Include(c => c.Lignes).FirstOrDefault(c => c.Id == SelectedCommande.Id);
                if (cmd == null) return;

                // Decrement stock only when changing to "traitee"
                if (newStatus == "traitee" && cmd.Statut != "traitee")
                {
                    foreach (var ligne in cmd.Lignes)
                    {
                        if (ligne.ProduitId.HasValue)
                        {
                            var product = ctx.Produits.FirstOrDefault(p => p.Id == ligne.ProduitId);
                            if (product != null)
                            {
                                product.StockActuel -= ligne.Quantite;

                                ctx.MouvementsStock.Add(new MouvementStock
                                {
                                    ProduitId = ligne.ProduitId.Value,
                                    TypeMouvement = "sortie",
                                    Quantite = ligne.Quantite,
                                    PrixUnitaire = ligne.PrixUnitaire,
                                    DateMouvement = DateTime.Now,
                                    Commentaire = $"Commande {cmd.NumeroCommande}",
                                    ProduitNomSnapshot = ligne.ProduitNom
                                });
                            }
                        }
                    }
                }

                cmd.Statut = newStatus;
                cmd.UpdatedAt = DateTime.Now;
                ctx.SaveChanges();
                _ = LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteCommande()
        {
            if (SelectedCommande == null) return;
            if (MessageBox.Show(WindowHelper.GetAdminWindow(),
                $"Supprimer définitivement la commande {SelectedCommande.NumeroCommande} ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                using var ctx = _contextFactory.CreateDbContext();
                var cmd = ctx.Commandes.Include(c => c.Lignes).FirstOrDefault(c => c.Id == SelectedCommande.Id);
                if (cmd != null)
                {
                    ctx.LignesCommande.RemoveRange(cmd.Lignes);
                    ctx.Commandes.Remove(cmd);
                    ctx.SaveChanges();
                }
                SelectedCommande = null;
                _ = LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddPayment()
        {
            if (SelectedCommande == null || SelectedCommande.Restant <= 0) return;
            var win = new Views.AddPaymentWindow(SelectedCommande.Restant);
            win.Owner = WindowHelper.GetAdminWindow();
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (win.ShowDialog() == true)
            {
                try
                {
                    using var ctx = _contextFactory.CreateDbContext();
                    var cmd = ctx.Commandes.FirstOrDefault(c => c.Id == SelectedCommande.Id);
                    if (cmd != null)
                    {
                        cmd.MontantPaye += win.MontantAjoute;
                        cmd.UpdatedAt = DateTime.Now;
                        ctx.SaveChanges();
                    }
                    _ = LoadDataAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void EditCommande()
        {
            if (SelectedCommande == null) return;

            // Store the ID for update (do NOT delete the commande)
            _editingCommandeId = SelectedCommande.Id;

            // Load into panier for editing
            IsNewCommandeMode = true;
            LoadProducts();
            LoadPromotions();

            _clientNom = SelectedCommande.Nom;
            _clientPrenom = SelectedCommande.Prenom;
            _clientTelephone = SelectedCommande.Telephone;
            _clientAdresse = SelectedCommande.Adresse ?? "";
            _clientVille = SelectedCommande.Ville ?? "";
            _clientCodePostal = SelectedCommande.CodePostal ?? "";
            AvecLivraison = SelectedCommande.AvecLivraison;
            MontantLivraison = SelectedCommande.MontantLivraison;
            MontantPaye = SelectedCommande.MontantPaye;

            CommandePanier.Clear();
            foreach (var ligne in SelectedCommande.Lignes)
            {
                var produit = Produits.FirstOrDefault(p => p.Id == ligne.ProduitId) ?? new Produit
                {
                    Id = ligne.ProduitId ?? 0,
                    Nom = ligne.ProduitNom,
                    PrixVente = ligne.PrixUnitaire,
                    Categorie = ligne.CategorieNom,
                    TaxTier = ligne.TaxTier
                };

                var lv = new LigneVente
                {
                    Produit = produit,
                    ProduitId = produit.Id,
                    ProduitNom = ligne.ProduitNom,
                    CategorieNom = ligne.CategorieNom,
                    PrixUnitaire = ligne.PrixUnitaire,
                    Quantite = ligne.Quantite,
                    TotalLigne = ligne.TotalLigne,
                    TaxTier = ligne.TaxTier
                };
                CommandePanier.Add(new CartItemViewModel(lv));
            }

            ApplyCommandePromotions();
            UpdateCommandeTotal();
        }

        // ─── New features ───
        private void OpenVilleCPFilter()
        {
            // Collect existing villes from commandes
            var existingVilles = Commandes
                .Where(c => !string.IsNullOrWhiteSpace(c.Ville) || !string.IsNullOrWhiteSpace(c.CodePostal))
                .Select(c => c.VilleCodePostal)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            var win = new Views.VilleCPFilterWindow(VilleCPFilterList, existingVilles);
            win.Owner = WindowHelper.GetAdminWindow();
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (win.ShowDialog() == true && win.Applied)
            {
                VilleCPFilterList = win.SelectedVillesCPs.ToList();
            }
        }

        private void ClearAllFilters()
        {
            _searchText = string.Empty;
            OnPropertyChanged(nameof(SearchText));
            _statutFilter = "Tous";
            OnPropertyChanged(nameof(StatutFilter));
            _paiementFilter = "Tous";
            OnPropertyChanged(nameof(PaiementFilter));
            VilleCPFilterList = new List<string>();
        }

        private void PrintRecap()
        {
            if (Commandes.Count == 0) return;
            var entreprise = GetEntreprise();
            var win = new Views.CommandeRecapWindow(Commandes.ToList(), _printService, entreprise);
            win.Owner = WindowHelper.GetAdminWindow();
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowDialog();
        }

        // ─── Fully functional remise (same logic as MainViewModel) ───
        private void ApplyManualDiscount()
        {
            try
            {
                var selectWindow = new Views.ManualDiscountSelectionWindow();
                selectWindow.Owner = WindowHelper.GetAdminWindow();
                selectWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (selectWindow.ShowDialog() != true) return;

                var scope = selectWindow.SelectedScope;
                var type = selectWindow.SelectedType;
                CartItemViewModel? targetItem = null;

                // Item-level and PriceOverride require selecting a cart item
                if (scope == Views.DiscountScope.Item || scope == Views.DiscountScope.PriceOverride)
                {
                    if (CommandePanier.Count == 0) return;
                    var itemWindow = new Views.CartItemSelectionWindow(CommandePanier);
                    itemWindow.Owner = WindowHelper.GetAdminWindow();
                    itemWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    if (itemWindow.ShowDialog() != true) return;
                    targetItem = itemWindow.SelectedItem;
                }

                if (scope == Views.DiscountScope.PriceOverride)
                {
                    if (targetItem == null) return;
                    string priceTitle = $"Nouveau prix de vente pour {targetItem.ProduitNom}";
                    var priceWindow = new Views.DiscountValueInputWindow(priceTitle, "€");
                    priceWindow.Owner = WindowHelper.GetAdminWindow();
                    priceWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
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
                    UpdateCommandeTotal();
                    return;
                }

                string title = (scope == Views.DiscountScope.Basket ? "Remise Panier" : $"Remise sur {targetItem?.ProduitNom}");
                string unit = (type == Views.DiscountType.Percentage ? "%" : "€");
                var valueWindow = new Views.DiscountValueInputWindow(title, unit);
                valueWindow.Owner = WindowHelper.GetAdminWindow();
                valueWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (valueWindow.ShowDialog() != true) return;

                decimal val = valueWindow.DiscountValue;
                if (scope == Views.DiscountScope.Basket)
                {
                    // Apply as manual discount spread across all items
                    decimal baseTotal = CommandePanier.Sum(i => i.TotalLigneStandard);
                    decimal totalDiscount = type == Views.DiscountType.Percentage ? (baseTotal * val / 100) : val;
                    // Distribute proportionally
                    if (baseTotal > 0)
                    {
                        foreach (var item in CommandePanier)
                        {
                            decimal ratio = item.TotalLigneStandard / baseTotal;
                            item.RemiseManuelleFixed = totalDiscount * ratio;
                            item.RemiseManuellePercent = 0;
                            item.PriceOverridePerUnit = 0;
                        }
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
                    targetItem.PriceOverridePerUnit = 0;
                }
                UpdateCommandeTotal();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyManualDiscount error: {ex.Message}");
            }
        }

        // ─── EditQuantity ───
        private void EditCommandeQuantity(object? parameter)
        {
            if (parameter is CartItemViewModel item)
            {
                var dialog = new Views.QuantityInputWindow(item.Quantite);
                dialog.Owner = WindowHelper.GetAdminWindow();
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (dialog.ShowDialog() == true)
                {
                    item.Quantite = dialog.Quantity;
                    if (item.Quantite <= 0) CommandePanier.Remove(item);
                    ApplyCommandePromotions();
                    UpdateCommandeTotal();
                }
            }
        }

        // ─── Suspend / Resume Commande (Attente) ───
        private void SuspendCommande()
        {
            if (CommandePanier.Count == 0) return;
            var suspended = new SuspendedCommande
            {
                Id = Guid.NewGuid(),
                Date = DateTime.Now,
                Items = CommandePanier.ToList(),
                MontantPaye = MontantPaye,
                AvecLivraison = AvecLivraison,
                MontantLivraison = MontantLivraison,
                ClientNom = _clientNom,
                ClientPrenom = _clientPrenom,
                ClientTelephone = _clientTelephone,
                ClientAdresse = _clientAdresse,
                ClientVille = _clientVille,
                ClientCodePostal = _clientCodePostal,
                Total = CommandeTotalAvecLivraison
            };
            suspended.Label = $"⏸ {suspended.Date:HH:mm} — {suspended.Items.Count} art. — {suspended.Total:C}";
            SuspendedCommandes.Add(suspended);
            OnPropertyChanged(nameof(HasSuspendedCommandes));

            // Clear panier but stay in new commande mode
            CommandePanier.Clear();
            MontantPaye = 0;
            AvecLivraison = false;
            MontantLivraison = 0;
            _clientNom = _clientPrenom = _clientTelephone = _clientAdresse = _clientVille = _clientCodePostal = string.Empty;
            UpdateCommandeTotal();
        }

        private void ResumeCommande(object? parameter)
        {
            SuspendedCommande? suspended = null;
            if (parameter is SuspendedCommande s)
                suspended = s;
            else if (SuspendedCommandes.Count == 1)
                suspended = SuspendedCommandes[0];
            else if (SuspendedCommandes.Count > 1)
            {
                // Let user pick
                var list = SuspendedCommandes.ToList();
                var msg = string.Join("\n", list.Select((x, i) => $"{i + 1}. {x.Label}"));
                // Simple: resume the first one
                suspended = list[0];
            }

            if (suspended == null) return;

            // Restore
            CommandePanier.Clear();
            foreach (var item in suspended.Items)
                CommandePanier.Add(item);
            MontantPaye = suspended.MontantPaye;
            AvecLivraison = suspended.AvecLivraison;
            MontantLivraison = suspended.MontantLivraison;
            _clientNom = suspended.ClientNom;
            _clientPrenom = suspended.ClientPrenom;
            _clientTelephone = suspended.ClientTelephone;
            _clientAdresse = suspended.ClientAdresse;
            _clientVille = suspended.ClientVille;
            _clientCodePostal = suspended.ClientCodePostal;

            SuspendedCommandes.Remove(suspended);
            OnPropertyChanged(nameof(HasSuspendedCommandes));

            ApplyCommandePromotions();
            UpdateCommandeTotal();

            if (!IsNewCommandeMode) IsNewCommandeMode = true;
        }

        private Entreprise GetEntreprise()
        {
            try
            {
                using var ctx = _contextFactory.CreateDbContext();
                return ctx.Entreprise.FirstOrDefault() ?? new Entreprise { Nom = "Inconnu" };
            }
            catch { return new Entreprise { Nom = "Inconnu" }; }
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SuspendedCommande
    {
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal MontantPaye { get; set; }
        public bool AvecLivraison { get; set; }
        public decimal MontantLivraison { get; set; }
        public string ClientNom { get; set; } = string.Empty;
        public string ClientPrenom { get; set; } = string.Empty;
        public string ClientTelephone { get; set; } = string.Empty;
        public string ClientAdresse { get; set; } = string.Empty;
        public string ClientVille { get; set; } = string.Empty;
        public string ClientCodePostal { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public string Label { get; set; } = string.Empty;
        public override string ToString() => Label;
    }
}
