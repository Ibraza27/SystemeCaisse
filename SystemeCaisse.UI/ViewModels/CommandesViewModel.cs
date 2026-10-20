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
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Services;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.ViewModels
{
    public class CommandesViewModel : INotifyPropertyChanged
    {
        private readonly CaisseServices _services;
        private readonly PrintService _printService;
        private MainViewModel? _mainViewModel;
        private int? _editingCommandeId = null; // Track if we're editing an existing commande
        private int _loadVersion;
        private Entreprise? _entreprise;
        private bool _commandesDirty = true;
        private bool _catalogDirty = true;
        private bool _suppressPanierEvents = false;

        /// <summary>Date du dernier chargement réussi de la liste.</summary>
        public DateTime LastLoaded { get; private set; } = DateTime.MinValue;

        // ─── List & Filters ───
        public ObservableCollection<Commande> Commandes { get; set; } = new();

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); _ = LoadDataAsync(debounce: true); }
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

        // ── Filtre par période (date début → date fin) ──
        private DateTime? _dateDebut;
        public DateTime? DateDebut
        {
            get => _dateDebut;
            set { _dateDebut = value; OnPropertyChanged(); _ = LoadDataAsync(); }
        }

        private DateTime? _dateFin;
        public DateTime? DateFin
        {
            get => _dateFin;
            set { _dateFin = value; OnPropertyChanged(); _ = LoadDataAsync(); }
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

        // ─── Multi-select ───
        public bool HasSelection => Commandes.Any(c => c.IsSelected);
        public int SelectionCount => Commandes.Count(c => c.IsSelected);
        public List<Commande> SelectedCommandes => Commandes.Where(c => c.IsSelected).ToList();

        private bool _isAllSelected;
        public bool IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                _isAllSelected = value;
                foreach (var c in Commandes) c.IsSelected = value;
                OnPropertyChanged();
                NotifySelectionChanged();
            }
        }

        // ─── Recap Summary (bottom bar) ───
        private int _commandesCount;
        public int CommandesCount { get => _commandesCount; set { _commandesCount = value; OnPropertyChanged(); } }
        private int _commandesRegleCount;
        public int CommandesRegleCount { get => _commandesRegleCount; set { _commandesRegleCount = value; OnPropertyChanged(); } }
        private int _commandesPartielCount;
        public int CommandesPartielCount { get => _commandesPartielCount; set { _commandesPartielCount = value; OnPropertyChanged(); } }
        private int _commandesNonRegleCount;
        public int CommandesNonRegleCount { get => _commandesNonRegleCount; set { _commandesNonRegleCount = value; OnPropertyChanged(); } }
        private decimal _totalCB;
        public decimal TotalCB { get => _totalCB; set { _totalCB = value; OnPropertyChanged(); } }
        private decimal _totalEspece;
        public decimal TotalEspece { get => _totalEspece; set { _totalEspece = value; OnPropertyChanged(); } }
        private decimal _totalEnLigne;
        public decimal TotalEnLigne { get => _totalEnLigne; set { _totalEnLigne = value; OnPropertyChanged(); } }
        private decimal _totalWero;
        public decimal TotalWero { get => _totalWero; set { _totalWero = value; OnPropertyChanged(); } }
        private decimal _totalVirement;
        public decimal TotalVirement { get => _totalVirement; set { _totalVirement = value; OnPropertyChanged(); } }
        private decimal _totalSurPlace;
        public decimal TotalSurPlace { get => _totalSurPlace; set { _totalSurPlace = value; OnPropertyChanged(); } }
        private decimal _totalGeneral;
        public decimal TotalGeneral { get => _totalGeneral; set { _totalGeneral = value; OnPropertyChanged(); } }

        // ─── Corbeille (Trash) mode ───
        private bool _isTrashMode;
        public bool IsTrashMode
        {
            get => _isTrashMode;
            set
            {
                if (_isTrashMode != value)
                {
                    _isTrashMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNormalListMode));
                }
            }
        }
        public bool IsNormalListMode => !IsTrashMode;

        // ─── New Commande mode ───
        private bool _isNewCommandeMode;
        public bool IsNewCommandeMode
        {
            get => _isNewCommandeMode;
            set { _isNewCommandeMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsListMode)); }
        }
        public bool IsListMode => !IsNewCommandeMode;

        // ─── Commandes regroupées (Multi-clients) ───
        private bool _isGroupedCommandeMode;
        public bool IsGroupedCommandeMode
        {
            get => _isGroupedCommandeMode;
            set
            {
                _isGroupedCommandeMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsClassicCommandeMode));
            }
        }
        public bool IsClassicCommandeMode => !IsGroupedCommandeMode;

        public ObservableCollection<SubClientGroup> SubClients { get; } = new();

        private SubClientGroup? _selectedSubClient;
        public SubClientGroup? SelectedSubClient
        {
            get => _selectedSubClient;
            set
            {
                if (_selectedSubClient != null) _selectedSubClient.IsSelected = false;
                _selectedSubClient = value;
                if (_selectedSubClient != null) _selectedSubClient.IsSelected = true;
                OnPropertyChanged();
            }
        }

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
        private string _clientCommentaire = string.Empty;

        private string _commandeModePaiement = "en_ligne";
        public string CommandeModePaiement
        {
            get => _commandeModePaiement;
            set { _commandeModePaiement = value; OnPropertyChanged(); }
        }

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
        public List<Produit> Produits { get; private set; } = new();

        // Suspended commandes (attente)
        public ObservableCollection<SuspendedCommande> SuspendedCommandes { get; set; } = new();
        public bool HasSuspendedCommandes => SuspendedCommandes.Count > 0;

        // Search suggestions for new commande
        public List<Produit> SearchSuggestions { get; private set; } = new();
        private string _newCommandeSearchText = string.Empty;
        public string NewCommandeSearchText
        {
            get => _newCommandeSearchText;
            set
            {
                if (_newCommandeSearchText != value)
                {
                    _newCommandeSearchText = value;
                    OnPropertyChanged();

                    if (!string.IsNullOrWhiteSpace(_newCommandeSearchText))
                    {
                        string search = _newCommandeSearchText.Trim();
                        bool isNumeric = search.All(char.IsDigit);
                        if (!isNumeric)
                        {
                            UpdateNewCommandeSearchSuggestions(search);
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

        private bool _isSearchDropDownOpen;
        public bool IsSearchDropDownOpen
        {
            get => _isSearchDropDownOpen;
            set
            {
                if (_isSearchDropDownOpen != value)
                {
                    _isSearchDropDownOpen = value;
                    OnPropertyChanged();
                }
            }
        }

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
                        var prod = _selectedSearchProduct;
                        _selectedSearchProduct = null;
                        OnPropertyChanged(nameof(SelectedSearchProduct));
                        NewCommandeSearchText = string.Empty;
                        IsSearchDropDownOpen = false;
                        if (SearchSuggestions.Count > 0)
                        {
                            SearchSuggestions = new List<Produit>();
                            OnPropertyChanged(nameof(SearchSuggestions));
                        }
                        AddToCommandePanier(prod);
                    }
                }
            }
        }

        // ─── Commands ───
        public ICommand LoadCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand NewCommandeCommand { get; }
        public ICommand NewGroupedCommandeCommand { get; }
        public ICommand EnableGroupedModeCommand { get; }
        public ICommand AddSubClientCommand { get; }
        public ICommand SelectSubClientCommand { get; }
        public ICommand RemoveSubClientCommand { get; }
        public ICommand CancelNewCommandeCommand { get; }
        public ICommand ValidateCommandeCommand { get; }
        public ICommand ViewTicketCommand { get; }
        public ICommand PrintTicketCommand { get; }
        public ICommand ChangeStatusCommand { get; }
        public ICommand QuickValidateCommand { get; }
        public ICommand DeleteCommandeCommand { get; }
        public ICommand RestoreCommandeCommand { get; }
        public ICommand PermanentDeleteCommandeCommand { get; }
        public ICommand ToggleTrashModeCommand { get; }
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

        // Batch selection commands
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand BatchMarkValideeCommand { get; }
        public ICommand BatchMarkTraiteeCommand { get; }
        public ICommand BatchMarkEnAttenteCommand { get; }
        public ICommand BatchAnnulerCommand { get; }
        public ICommand BatchDeleteCommand { get; }
        public ICommand BatchRestoreCommand { get; }
        public ICommand BatchPermanentDeleteCommand { get; }
        public ICommand BatchPrintCommand { get; }
        public ICommand BatchRecapCommand { get; }
        public ICommand PrintClientRecapCommand { get; }

        public CommandesViewModel(CaisseServices services, PrintService printService)
        {
            _services = services;
            _printService = printService;

            _services.Versions.Changed += domains =>
            {
                if (domains.Contains(DataDomains.Commandes))
                    _commandesDirty = true;
                if (domains.Contains(DataDomains.Produits) || domains.Contains(DataDomains.Promotions) || domains.Contains(DataDomains.Stocks))
                    _catalogDirty = true;
            };

            LoadCommand = new BasicRelayCommand(_ => _ = LoadDataAsync());
            ClearSearchCommand = new BasicRelayCommand(_ => SearchText = string.Empty);

            NewCommandeCommand = new BasicRelayCommand(_ => StartNewCommande(grouped: false));
            NewGroupedCommandeCommand = new BasicRelayCommand(_ => StartNewCommande(grouped: true));
            EnableGroupedModeCommand = new BasicRelayCommand(_ => EnableGroupedMode());
            AddSubClientCommand = new BasicRelayCommand(_ => AddSubClient());
            SelectSubClientCommand = new BasicRelayCommand(p => { if (p is SubClientGroup sc) SelectedSubClient = sc; });
            RemoveSubClientCommand = new BasicRelayCommand(p => { if (p is SubClientGroup sc) RemoveSubClient(sc); });
            CancelNewCommandeCommand = new BasicRelayCommand(_ => CancelNewCommande());
            ValidateCommandeCommand = new BasicRelayCommand(_ => OpenClientInfoWindow(), _ => CommandePanier.Count > 0);

            AddToCommandePanierCommand = new BasicRelayCommand(p => { if (p is Produit prod) AddToCommandePanier(prod); });
            RemoveCommandeItemCommand = new BasicRelayCommand(p => { if (p is CartItemViewModel item) { CommandePanier.Remove(item); UpdateCommandeTotal(); } });
            ViderCommandePanierCommand = new BasicRelayCommand(_ => { CommandePanier.Clear(); MontantPaye = 0; AvecLivraison = false; MontantLivraison = 0; UpdateCommandeTotal(); });
            FillExactAmountCommand = new BasicRelayCommand(_ => MontantPaye = Math.Round(CommandeTotalAvecLivraison, 2));
            ScanNewCommandeCommand = new BasicRelayCommand(_ => HandleNewCommandeScan());
            ClearNewCommandeSearchCommand = new BasicRelayCommand(_ => NewCommandeSearchText = string.Empty);
            OpenVilleCPFilterCommand = new BasicRelayCommand(_ => OpenVilleCPFilter());
            
            ClearAllFiltersCommand = new BasicRelayCommand(_ => ClearAllFilters());
            PrintRecapCommand = new BasicRelayCommand(_ => PrintRecap(), _ => Commandes.Count > 0);
            OpenCommandeDiscountCommand = new BasicRelayCommand(_ => ApplyManualDiscount(), _ => CommandePanier.Count > 0);
            EditCommandeQuantityCommand = new BasicRelayCommand(p => EditCommandeQuantity(p));
            SuspendCommandeCommand = new BasicRelayCommand(_ => SuspendCommande(), _ => CommandePanier.Count > 0);
            ResumeCommandeCommand = new BasicRelayCommand(p => ResumeCommande(p));

            // Batch selection commands
            SelectAllCommand = new BasicRelayCommand(_ => { foreach (var c in Commandes) c.IsSelected = true; NotifySelectionChanged(); });
            DeselectAllCommand = new BasicRelayCommand(_ => { foreach (var c in Commandes) c.IsSelected = false; NotifySelectionChanged(); });
            BatchMarkValideeCommand = new BasicRelayCommand(_ => BatchChangeStatus("validee"), _ => HasSelection);
            BatchMarkTraiteeCommand = new BasicRelayCommand(_ => BatchChangeStatus("traitee"), _ => HasSelection);
            BatchMarkEnAttenteCommand = new BasicRelayCommand(_ => BatchChangeStatus("en_attente"), _ => HasSelection);
            BatchAnnulerCommand = new BasicRelayCommand(_ => BatchChangeStatus("annulee"), _ => HasSelection);
            BatchDeleteCommand = new BasicRelayCommand(_ => BatchDelete(), _ => HasSelection);
            BatchRestoreCommand = new BasicRelayCommand(_ => BatchRestore(), _ => HasSelection);
            BatchPermanentDeleteCommand = new BasicRelayCommand(_ => BatchPermanentDelete(), _ => HasSelection);
            BatchPrintCommand = new BasicRelayCommand(_ => BatchPrint(), _ => HasSelection);
            BatchRecapCommand = new BasicRelayCommand(_ => BatchRecap(), _ => HasSelection);
            PrintClientRecapCommand = new BasicRelayCommand(_ => PrintClientRecap(), _ => Commandes.Count > 0);

            ViewTicketCommand = new BasicRelayCommand(_ => ViewCommandeTicket(), _ => SelectedCommande != null);
            PrintTicketCommand = new BasicRelayCommand(_ => PrintCommandeTicket(), _ => SelectedCommande != null);

            ChangeStatusCommand = new BasicRelayCommand(p =>
            {
                if (p is string status && SelectedCommande != null) ChangeStatus(status);
            }, _ => SelectedCommande != null);

            QuickValidateCommand = new BasicRelayCommand(_ => QuickValidate(), _ => SelectedCommande != null && SelectedCommande.Statut != "validee");
            DeleteCommandeCommand = new BasicRelayCommand(_ => DeleteCommande(), _ => SelectedCommande != null);
            RestoreCommandeCommand = new BasicRelayCommand(_ => RestoreCommande(), _ => SelectedCommande != null);
            PermanentDeleteCommandeCommand = new BasicRelayCommand(_ => PermanentDeleteCommande(), _ => SelectedCommande != null);
            ToggleTrashModeCommand = new BasicRelayCommand(_ => ToggleTrashMode());
            AddPaymentCommand = new BasicRelayCommand(_ => AddPayment(), _ => SelectedCommande != null && SelectedCommande.Restant > 0);
            EditCommandeCommand = new BasicRelayCommand(_ => EditCommande(), _ => SelectedCommande != null && SelectedCommande.Statut != "annulee" && SelectedCommande.Statut != "supprimee");

            CommandePanier.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (CartItemViewModel item in e.NewItems)
                    {
                        item.PropertyChanged += (sender, args) =>
                        {
                            if (args.PropertyName == nameof(CartItemViewModel.Quantite))
                            {
                                ApplyCommandePromotions();
                                UpdateCommandeTotal();
                            }
                        };
                    }
                }
                if (!_suppressPanierEvents)
                {
                    ApplyCommandePromotions();
                    UpdateCommandeTotal();
                }
            };
        }

        public void SetMainViewModel(MainViewModel mainVM)
        {
            _mainViewModel = mainVM;
            OnPropertyChanged(nameof(TopProducts));
        }

        public async Task InitializeAsync()
        {
            CommuneService.Load();
            await LoadCatalogAsync();
            await LoadDataAsync();
        }

        /// <summary>
        /// Appelé lors du passage sur l'onglet Commandes : instantané (0ms) si déjà chargé et à jour.
        /// </summary>
        public async Task RefreshIfNeededAsync()
        {
            if (LastLoaded == DateTime.MinValue || _commandesDirty)
            {
                await LoadDataAsync();
            }
            if (_catalogDirty || Produits.Count == 0)
            {
                _ = LoadCatalogAsync();
            }
        }

        // ═══════════════════════════════════════════════
        //  TICKET & IMPRESSION
        // ═══════════════════════════════════════════════

        // ─── TopProducts: delegate to MainViewModel ───
        public ObservableCollection<Produit> TopProducts => _mainViewModel?.TopProducts ?? new ObservableCollection<Produit>();

        // ─── Data Loading ───

        /// <summary>Produits, promotions actives et entreprise (lecture seule, hors thread UI).</summary>
        private async Task LoadCatalogAsync()
        {
            try
            {
                var productsTask = _services.Catalog.GetProductsAsync();
                var promosTask = _services.Catalog.GetActivePromotionsAsync();
                var entrepriseTask = _services.Catalog.GetEntrepriseAsync();
                await Task.WhenAll(productsTask, promosTask, entrepriseTask);

                Produits = productsTask.Result;
                _activePromotions = promosTask.Result;
                _entreprise = entrepriseTask.Result;
                _catalogDirty = false;

                if (IsNewCommandeMode && CommandePanier.Count > 0)
                {
                    ApplyCommandePromotions();
                    UpdateCommandeTotal();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Commandes", $"Chargement du catalogue impossible : {ex.Message}");
            }
        }

        private IEnumerable<Produit> GetCatalogProducts()
        {
            if (Produits.Count > 0) return Produits;
            if (_mainViewModel?.Produits != null) return _mainViewModel.Produits;
            return Array.Empty<Produit>();
        }

        /// <summary>
        /// Recharge la liste. Statut, dates et recherche sont filtrés PAR LA BASE ; paiement et ville
        /// (propriétés calculées) en mémoire. Seul le résultat de la dernière demande est affiché.
        /// </summary>
        public async Task LoadDataAsync(bool debounce = false)
        {
            int version = Interlocked.Increment(ref _loadVersion);
            if (debounce) await Task.Delay(250);
            if (version != _loadVersion) return;

            var filter = new CommandeFilter
            {
                Statut = IsTrashMode
                    ? "supprimee"
                    : (StatutFilter switch
                    {
                        "En attente" => "en_attente",
                        "Validée" => "validee",
                        "Traitée" => "traitee",
                        "Annulée" => "annulee",
                        _ => null
                    }),
                DateDebut = DateDebut,
                DateFin = DateFin,
                Recherche = SearchText,
                Max = 2000
            };
            var paiementFilter = PaiementFilter;
            var villeFilters = VilleCPFilterList.ToList();

            try
            {
                var commandesList = await Task.Run(async () =>
                {
                    var list = await _services.Commandes.SearchAsync(filter);

                    if (paiementFilter != "Tous")
                    {
                        list = paiementFilter switch
                        {
                            "Réglé" => list.Where(c => c.StatutPaiement == "regle").ToList(),
                            "Partiel" => list.Where(c => c.StatutPaiement == "partiel").ToList(),
                            "Non réglé" => list.Where(c => c.StatutPaiement == "non_regle").ToList(),
                            _ => list
                        };
                    }

                    if (villeFilters.Count > 0)
                    {
                        list = list.Where(c =>
                        {
                            string cp = (c.CodePostal ?? "").Trim().ToUpper();
                            string ville = (c.Ville ?? "").Trim().ToUpper();
                            string villeCP = (c.VilleCodePostal ?? "").Trim().ToUpper();

                            return villeFilters.Any(fRaw =>
                            {
                                string f = fRaw.Trim().ToUpper();
                                if (villeCP == f) return true;
                                if (!string.IsNullOrEmpty(cp) && f.Contains(cp)) return true;
                                if (!string.IsNullOrEmpty(ville) && f.Contains(ville)) return true;
                                if (!string.IsNullOrEmpty(villeCP) && villeCP.Contains(f)) return true;
                                return false;
                            });
                        }).ToList();
                    }

                    foreach (var c in list)
                    {
                        c.PropertyChanged += Commande_IsSelectedChanged;
                    }

                    return list;
                });

                if (version != _loadVersion) return;

                int? selectedId = SelectedCommande?.Id;
                foreach (var old in Commandes) old.PropertyChanged -= Commande_IsSelectedChanged;

                Commandes = new ObservableCollection<Commande>(commandesList);
                OnPropertyChanged(nameof(Commandes));
                UpdateRecapSummary();
                NotifySelectionChanged();
                if (selectedId.HasValue) SelectedCommande = Commandes.FirstOrDefault(c => c.Id == selectedId.Value);
                LastLoaded = DateTime.Now;
                _commandesDirty = false;
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Chargement des commandes impossible", ex);
            }
        }

        private void ToggleTrashMode()
        {
            IsTrashMode = !IsTrashMode;
            SelectedCommande = null;
            _ = LoadDataAsync();
        }

        // ─── New Commande & Commandes regroupées ───
        private SubClientGroup CreateSubClientGroup(string nom)
        {
            var group = new SubClientGroup(nom);
            string prevNom = nom;
            group.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SubClientGroup.Nom))
                {
                    string newNom = group.Nom;
                    if (!string.IsNullOrWhiteSpace(newNom) && newNom != prevNom)
                    {
                        foreach (var item in CommandePanier.Where(i => i.ClientNom == prevNom))
                        {
                            item.ClientNom = newNom;
                        }
                        prevNom = newNom;
                        UpdateCommandeTotal();
                    }
                }
            };
            return group;
        }

        private void StartNewCommande(bool grouped = false)
        {
            _editingCommandeId = null;
            _suppressPanierEvents = true;
            CommandePanier.Clear();
            _suppressPanierEvents = false;

            IsGroupedCommandeMode = grouped;
            SubClients.Clear();
            if (grouped)
            {
                var c1 = CreateSubClientGroup("Client 1");
                var c2 = CreateSubClientGroup("Client 2");
                SubClients.Add(c1);
                SubClients.Add(c2);
                SelectedSubClient = c1;
            }
            else
            {
                SelectedSubClient = null;
            }

            NewCommandeSearchText = string.Empty;
            IsSearchDropDownOpen = false;
            MontantPaye = 0;
            AvecLivraison = false;
            MontantLivraison = 0;
            _clientNom = _clientPrenom = _clientTelephone = _clientAdresse = _clientVille = _clientCodePostal = _clientCommentaire = string.Empty;
            UpdateCommandeTotal();

            IsNewCommandeMode = true;

            if (_catalogDirty || Produits.Count == 0)
            {
                _ = LoadCatalogAsync();
            }
        }

        private void EnableGroupedMode()
        {
            if (IsGroupedCommandeMode) return;
            IsGroupedCommandeMode = true;
            SubClients.Clear();
            var c1 = CreateSubClientGroup("Client 1");
            var c2 = CreateSubClientGroup("Client 2");
            SubClients.Add(c1);
            SubClients.Add(c2);
            SelectedSubClient = c1;

            foreach (var item in CommandePanier)
            {
                if (string.IsNullOrWhiteSpace(item.ClientNom))
                    item.ClientNom = c1.Nom;
            }
            UpdateCommandeTotal();
        }

        private void AddSubClient()
        {
            if (!IsGroupedCommandeMode)
            {
                EnableGroupedMode();
                return;
            }

            int nextNum = SubClients.Count + 1;
            while (SubClients.Any(c => string.Equals(c.Nom, $"Client {nextNum}", StringComparison.OrdinalIgnoreCase)))
                nextNum++;

            var nextClient = CreateSubClientGroup($"Client {nextNum}");
            SubClients.Add(nextClient);
            SelectedSubClient = nextClient;
            UpdateCommandeTotal();
        }

        private void RemoveSubClient(SubClientGroup? group)
        {
            if (group == null || SubClients.Count <= 1) return;

            var toRemove = CommandePanier.Where(i => i.ClientNom == group.Nom).ToList();
            _suppressPanierEvents = true;
            foreach (var item in toRemove)
                CommandePanier.Remove(item);
            _suppressPanierEvents = false;

            SubClients.Remove(group);
            if (SelectedSubClient == group)
                SelectedSubClient = SubClients.FirstOrDefault();

            ApplyCommandePromotions();
            UpdateCommandeTotal();
        }

        private void CancelNewCommande()
        {
            _editingCommandeId = null;
            IsNewCommandeMode = false;
            IsGroupedCommandeMode = false;
            SubClients.Clear();
            SelectedSubClient = null;

            _suppressPanierEvents = true;
            CommandePanier.Clear();
            _suppressPanierEvents = false;

            NewCommandeSearchText = string.Empty;
            IsSearchDropDownOpen = false;
            MontantPaye = 0;
            AvecLivraison = false;
            MontantLivraison = 0;
            _clientCommentaire = string.Empty;
            CommandeModePaiement = "en_ligne";
            UpdateCommandeTotal();
        }

        private void InsertCartItemGrouped(CartItemViewModel newItem, string? clientTag)
        {
            if (!IsGroupedCommandeMode || string.IsNullOrWhiteSpace(clientTag))
            {
                CommandePanier.Add(newItem);
                return;
            }

            // Insérer juste après le dernier article du même sous-client pour garder Client 1 puis Client 2, etc.
            int lastIdx = -1;
            for (int i = 0; i < CommandePanier.Count; i++)
            {
                if (CommandePanier[i].ClientNom == clientTag)
                    lastIdx = i;
            }

            if (lastIdx >= 0 && lastIdx < CommandePanier.Count - 1)
                CommandePanier.Insert(lastIdx + 1, newItem);
            else
                CommandePanier.Add(newItem);
        }

        private void AddToCommandePanier(Produit produit)
        {
            bool isWeight = string.Equals(produit.TypeVente?.Trim(), "poids", StringComparison.OrdinalIgnoreCase);
            string? clientTag = IsGroupedCommandeMode ? (SelectedSubClient?.Nom ?? "Client 1") : null;
            string unite = isWeight ? "kg" : "pce";

            if (isWeight)
            {
                // Open weight input window, same as Caisse
                var scaleService = _mainViewModel?.ScaleService;
                var dialog = new SystemeCaisse.UI.Views.WeightInputWindow(produit, scaleService);
                dialog.Owner = WindowHelper.GetAdminWindow();
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (dialog.ShowDialog() == true)
                {
                    var existing = CommandePanier.FirstOrDefault(i => i.Produit.Id == produit.Id && i.ClientNom == clientTag);
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
                        var cartItem = new CartItemViewModel(ligne) { ClientNom = clientTag, Unite = unite };
                        InsertCartItemGrouped(cartItem, clientTag);
                    }
                }
            }
            else
            {
                var existing = CommandePanier.FirstOrDefault(i => i.Produit.Id == produit.Id && i.ClientNom == clientTag);
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
                    var cartItem = new CartItemViewModel(ligne) { ClientNom = clientTag, Unite = unite };
                    InsertCartItemGrouped(cartItem, clientTag);
                }
            }
            UpdateCommandeTotal();
        }

        private void HandleNewCommandeScan()
        {
            if (string.IsNullOrWhiteSpace(NewCommandeSearchText)) return;
            var code = NewCommandeSearchText.Trim();
            var catalog = GetCatalogProducts();
            var product = catalog.FirstOrDefault(p => p.Actif && p.CodeBarre == code);
            if (product == null && int.TryParse(code, out int id) && id < 10000)
                product = catalog.FirstOrDefault(p => p.Actif && p.Id == id);
            if (product != null)
            {
                AddToCommandePanier(product);
                NewCommandeSearchText = string.Empty;
                IsSearchDropDownOpen = false;
                if (SearchSuggestions.Count > 0)
                {
                    SearchSuggestions = new List<Produit>();
                    OnPropertyChanged(nameof(SearchSuggestions));
                }
            }
        }

        private void UpdateNewCommandeSearchSuggestions(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                IsSearchDropDownOpen = false;
                if (SearchSuggestions.Count > 0)
                {
                    SearchSuggestions = new List<Produit>();
                    OnPropertyChanged(nameof(SearchSuggestions));
                }
                return;
            }

            var catalog = GetCatalogProducts();
            var startsWithMatches = new List<Produit>(35);
            var containsMatches = new List<Produit>(35);

            foreach (var p in catalog)
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

        private void UpdateCommandeTotal()
        {
            CommandeTotalSansRemise = CommandePanier.Sum(i => i.TotalLigneStandard);
            CommandeTotalRemise = CommandePanier.Sum(i => i.RemiseAuto + i.RemiseManuelle);
            CommandeTotal = CommandeTotalSansRemise - CommandeTotalRemise;
            if (CommandeTotal < 0) CommandeTotal = 0;
            CommandeTotalAvecLivraison = Math.Round(CommandeTotal + (AvecLivraison ? MontantLivraison : 0), 2);
            CommandeRestant = Math.Round(CommandeTotalAvecLivraison - MontantPaye, 2);
            if (CommandeRestant < 0) CommandeRestant = 0;

            if (IsGroupedCommandeMode)
            {
                foreach (var sc in SubClients)
                {
                    var clientItems = CommandePanier.Where(i => i.ClientNom == sc.Nom).ToList();
                    sc.Total = clientItems.Sum(i => i.TotalLigne);
                    sc.NbArticles = (int)Math.Ceiling(clientItems.Sum(i => i.Quantite));
                }
            }
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
                _clientAdresse, _clientVille, _clientCodePostal,
                _clientCommentaire);
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
            _clientCommentaire = win.ClientCommentaire;

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

        private List<LigneCommande> BuildLignesFromPanier() => CommandePanier.Select(item => new LigneCommande
        {
            ProduitId = item.ProduitId,
            ProduitNom = item.ProduitNom,
            CategorieNom = item.Produit.Categorie ?? "Autre",
            ClientNom = string.IsNullOrWhiteSpace(item.ClientNom) ? null : item.ClientNom.Trim(),
            Unite = item.Unite,
            PrixUnitaire = item.PrixUnitaire,
            Quantite = item.Quantite,
            TotalLigne = item.TotalLigne,
            Remise = item.RemiseAuto + item.RemiseManuelle,
            PromotionAppliquee = item.PromotionAppliquee,
            TaxTier = item.TaxTier
        }).ToList();

        private async void SaveCommande()
        {
            var owner = WindowHelper.GetAdminWindow();
            try
            {
                var lignes = BuildLignesFromPanier();
                var nom = _clientNom;
                var prenom = _clientPrenom;
                var telephone = _clientTelephone;
                var adresse = string.IsNullOrWhiteSpace(_clientAdresse) ? null : _clientAdresse;
                var ville = string.IsNullOrWhiteSpace(_clientVille) ? null : _clientVille;
                var codePostal = string.IsNullOrWhiteSpace(_clientCodePostal) ? null : _clientCodePostal;
                var commentaire = string.IsNullOrWhiteSpace(_clientCommentaire) ? null : _clientCommentaire.Trim();
                var total = CommandeTotal;
                var totalRemise = CommandeTotalRemise;
                var montantPaye = MontantPaye;
                var avecLivraison = AvecLivraison;
                var montantLivraison = AvecLivraison ? MontantLivraison : 0;
                var nbArticles = (int)Math.Ceiling(CommandePanier.Sum(i => i.Quantite));
                var modePaiement = CommandeModePaiement;

                if (_editingCommandeId.HasValue)
                {
                    int editId = _editingCommandeId.Value;
                    // Update existing commande
                    var updated = await _services.Commandes.UpdateAsync(editId, existing =>
                    {
                        existing.Nom = nom;
                        existing.Prenom = prenom;
                        existing.Telephone = telephone;
                        existing.Adresse = adresse;
                        existing.Ville = ville;
                        existing.CodePostal = codePostal;
                        existing.Commentaire = commentaire;
                        existing.Total = total;
                        existing.TotalRemise = totalRemise;
                        existing.MontantPaye = montantPaye;
                        existing.MontantLivraison = montantLivraison;
                        existing.AvecLivraison = avecLivraison;
                        existing.NbArticles = nbArticles;
                        existing.ModePaiement = modePaiement;
                    }, lignes);

                    _editingCommandeId = null;
                    _suppressPanierEvents = true;
                    CommandePanier.Clear();
                    _suppressPanierEvents = false;
                    IsGroupedCommandeMode = false;
                    SubClients.Clear();
                    SelectedSubClient = null;
                    IsNewCommandeMode = false;

                    if (updated != null)
                    {
                        _ = LoadDataAsync();
                        MessageBox.Show(owner,
                            $"Commande {updated.NumeroCommande} mise à jour !",
                            "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        _ = LoadDataAsync();
                        MessageBox.Show(owner,
                            "Cette commande a été supprimée depuis un autre poste : la modification n'a pas pu être enregistrée.",
                            "Commande introuvable", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    return;
                }

                // Create new commande (numéro CMD-yyyyMMdd-NNN attribué par le service, unique entre postes)
                var now = DateTime.Now;
                var commande = new Commande
                {
                    SyncId = Guid.NewGuid().ToString(),
                    Source = "caisse",
                    Nom = nom,
                    Prenom = prenom,
                    Telephone = telephone,
                    Adresse = adresse,
                    Ville = ville,
                    CodePostal = codePostal,
                    Commentaire = commentaire,
                    Total = total,
                    TotalRemise = totalRemise,
                    MontantPaye = montantPaye,
                    MontantLivraison = montantLivraison,
                    AvecLivraison = avecLivraison,
                    NbArticles = nbArticles,
                    Statut = "en_attente",
                    ModePaiement = modePaiement,
                    Poste = _services.Poste.NomPoste,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Lignes = lignes
                };

                var created = await _services.Commandes.CreateAsync(commande);

                _suppressPanierEvents = true;
                CommandePanier.Clear();
                _suppressPanierEvents = false;
                IsGroupedCommandeMode = false;
                SubClients.Clear();
                SelectedSubClient = null;
                IsNewCommandeMode = false;

                // Mise à jour immédiate en mémoire pour une transition instantanée
                if (!IsTrashMode)
                {
                    created.PropertyChanged += Commande_IsSelectedChanged;
                    Commandes.Insert(0, created);
                    SelectedCommande = created;
                    UpdateRecapSummary();
                    _commandesDirty = false;
                }
                else
                {
                    _ = LoadDataAsync();
                }

                MessageBox.Show(owner,
                    $"Commande {created.NumeroCommande} créée avec succès !",
                    "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Sauvegarde de la commande impossible", ex);
                MessageBox.Show(owner,
                    $"Erreur lors de la sauvegarde : {ex.InnerException?.Message ?? ex.Message}",
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

        /// <summary>Bouton rapide Validé en 1 clic dans le détail de commande.</summary>
        private async void QuickValidate()
        {
            var selected = SelectedCommande;
            if (selected == null) return;
            var owner = WindowHelper.GetAdminWindow();

            try
            {
                await _services.Commandes.ChangeStatusAsync(new[] { selected.Id }, "validee");
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Validation rapide impossible", ex);
                MessageBox.Show(owner, $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ChangeStatus(string newStatus)
        {
            var selected = SelectedCommande;
            if (selected == null) return;
            var owner = WindowHelper.GetAdminWindow();
            string displayStatus = newStatus switch
            {
                "validee" => "Validée",
                "traitee" => "Traitée",
                "en_attente" => "En attente",
                "annulee" => "Annulée",
                _ => newStatus
            };

            if (MessageBox.Show(owner,
                $"Passer la commande {selected.NumeroCommande} en \"{displayStatus}\" ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                // Le passage en "traitee" décrémente le stock (atomique, une seule fois) dans la même transaction.
                await _services.Commandes.ChangeStatusAsync(new[] { selected.Id }, newStatus);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Changement de statut impossible", ex);
                MessageBox.Show(owner, $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteCommande()
        {
            if (IsTrashMode)
            {
                PermanentDeleteCommande();
                return;
            }

            var selected = SelectedCommande;
            if (selected == null) return;
            var owner = WindowHelper.GetAdminWindow();

            string displayName = string.IsNullOrWhiteSpace(selected.NumeroCommande)
                ? $"(commande sans données, ID={selected.Id})"
                : selected.NumeroCommande;

            if (MessageBox.Show(owner,
                $"Mettre la commande {displayName} dans la corbeille ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                await _services.Commandes.ChangeStatusAsync(new[] { selected.Id }, "supprimee");
                SelectedCommande = null;
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Mise à la corbeille impossible", ex);
                MessageBox.Show(owner, $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RestoreCommande()
        {
            var selected = SelectedCommande;
            if (selected == null) return;
            var owner = WindowHelper.GetAdminWindow();

            try
            {
                await _services.Commandes.ChangeStatusAsync(new[] { selected.Id }, "en_attente");
                SelectedCommande = null;
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Restauration de la commande impossible", ex);
                MessageBox.Show(owner, $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void PermanentDeleteCommande()
        {
            var selected = SelectedCommande;
            if (selected == null) return;
            var owner = WindowHelper.GetAdminWindow();

            string displayName = string.IsNullOrWhiteSpace(selected.NumeroCommande)
                ? $"(commande sans données, ID={selected.Id})"
                : selected.NumeroCommande;

            if (MessageBox.Show(owner,
                $"Supprimer définitivement la commande {displayName} ?\nCette action est irréversible.",
                "Suppression définitive", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                await _services.Commandes.DeleteAsync(new[] { selected.Id });
                SelectedCommande = null;
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Suppression définitive de la commande impossible", ex);
                MessageBox.Show(owner, $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddPayment()
        {
            var selected = SelectedCommande;
            if (selected == null || selected.Restant <= 0) return;
            var owner = WindowHelper.GetAdminWindow();
            var win = new Views.AddPaymentWindow(selected.Restant);
            win.Owner = owner;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (win.ShowDialog() == true)
            {
                try
                {
                    // Addition faite par la base : deux postes qui encaissent en même temps ne s'écrasent pas.
                    await _services.Commandes.AddPaymentAsync(selected.Id, Math.Round(win.MontantAjoute, 2), win.ModePaiement);
                    await LoadDataAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Commandes", "Ajout du paiement impossible", ex);
                    MessageBox.Show(owner, $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void EditCommande()
        {
            if (SelectedCommande == null) return;

            // Store the ID for update (do NOT delete the commande)
            _editingCommandeId = SelectedCommande.Id;

            if (_catalogDirty || Produits.Count == 0)
            {
                await LoadCatalogAsync();
                if (SelectedCommande == null) return;
            }

            _clientNom = SelectedCommande.Nom;
            _clientPrenom = SelectedCommande.Prenom;
            _clientTelephone = SelectedCommande.Telephone;
            _clientAdresse = SelectedCommande.Adresse ?? "";
            _clientVille = SelectedCommande.Ville ?? "";
            _clientCodePostal = SelectedCommande.CodePostal ?? "";
            _clientCommentaire = SelectedCommande.Commentaire ?? "";
            AvecLivraison = SelectedCommande.AvecLivraison;
            MontantLivraison = SelectedCommande.MontantLivraison;
            MontantPaye = SelectedCommande.MontantPaye;
            CommandeModePaiement = SelectedCommande.ModePaiement ?? "espece";

            var catalog = GetCatalogProducts();
            bool isGrouped = SelectedCommande.IsGroupedCommande;
            IsGroupedCommandeMode = isGrouped;
            SubClients.Clear();

            if (isGrouped)
            {
                var distinctClients = SelectedCommande.Lignes
                    .Select(l => string.IsNullOrWhiteSpace(l.ClientNom) ? "Client 1" : l.ClientNom.Trim())
                    .Distinct()
                    .ToList();

                foreach (var cName in distinctClients)
                {
                    SubClients.Add(CreateSubClientGroup(cName));
                }
                SelectedSubClient = SubClients.FirstOrDefault();
            }
            else
            {
                SelectedSubClient = null;
            }

            _suppressPanierEvents = true;
            CommandePanier.Clear();
            foreach (var ligne in SelectedCommande.Lignes)
            {
                var produit = catalog.FirstOrDefault(p => p.Id == ligne.ProduitId) ?? new Produit
                {
                    Id = ligne.ProduitId ?? 0,
                    Nom = ligne.ProduitNom,
                    PrixVente = ligne.PrixUnitaire,
                    Categorie = ligne.CategorieNom,
                    TypeVente = string.Equals(ligne.Unite, "kg", StringComparison.OrdinalIgnoreCase) ? "poids" : "unite",
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
                string? clientTag = isGrouped
                    ? (string.IsNullOrWhiteSpace(ligne.ClientNom) ? "Client 1" : ligne.ClientNom.Trim())
                    : null;
                CommandePanier.Add(new CartItemViewModel(lv)
                {
                    ClientNom = clientTag,
                    Unite = ligne.UniteDisplay
                });
            }
            _suppressPanierEvents = false;

            ApplyCommandePromotions();
            UpdateCommandeTotal();

            // Switch to edit view after items are prepared
            IsNewCommandeMode = true;
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
            _dateDebut = null;
            OnPropertyChanged(nameof(DateDebut));
            _dateFin = null;
            OnPropertyChanged(nameof(DateFin));
            VilleCPFilterList = new List<string>();
        }

        private void PrintRecap()
        {
            if (Commandes.Count == 0) return;
            var entreprise = GetEntreprise();
            var win = new Views.CommandeRecapWindow(Commandes.ToList(), _printService, entreprise, GetCatalogProducts());
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
                IsGrouped = IsGroupedCommandeMode,
                SubClientNames = SubClients.Select(s => s.Nom).ToList(),
                MontantPaye = MontantPaye,
                AvecLivraison = AvecLivraison,
                MontantLivraison = MontantLivraison,
                ClientNom = _clientNom,
                ClientPrenom = _clientPrenom,
                ClientTelephone = _clientTelephone,
                ClientAdresse = _clientAdresse,
                ClientVille = _clientVille,
                ClientCodePostal = _clientCodePostal,
                ClientCommentaire = _clientCommentaire,
                ModePaiement = _commandeModePaiement,
                Total = CommandeTotalAvecLivraison
            };
            suspended.Label = $"⏸ {suspended.Date:HH:mm} — {suspended.Items.Count} art. — {suspended.Total:C}";
            SuspendedCommandes.Add(suspended);
            OnPropertyChanged(nameof(HasSuspendedCommandes));

            // Clear panier but stay in new commande mode
            _suppressPanierEvents = true;
            CommandePanier.Clear();
            _suppressPanierEvents = false;
            MontantPaye = 0;
            AvecLivraison = false;
            MontantLivraison = 0;
            _clientNom = _clientPrenom = _clientTelephone = _clientAdresse = _clientVille = _clientCodePostal = _clientCommentaire = string.Empty;
            CommandeModePaiement = "en_ligne";
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
                var list = SuspendedCommandes.ToList();
                suspended = list[0];
            }

            if (suspended == null) return;

            IsGroupedCommandeMode = suspended.IsGrouped;
            SubClients.Clear();
            if (suspended.IsGrouped && suspended.SubClientNames.Count > 0)
            {
                foreach (var name in suspended.SubClientNames)
                    SubClients.Add(CreateSubClientGroup(name));
                SelectedSubClient = SubClients.FirstOrDefault();
            }
            else
            {
                SelectedSubClient = null;
            }

            // Restore
            _suppressPanierEvents = true;
            CommandePanier.Clear();
            foreach (var item in suspended.Items)
                CommandePanier.Add(item);
            _suppressPanierEvents = false;

            MontantPaye = suspended.MontantPaye;
            AvecLivraison = suspended.AvecLivraison;
            MontantLivraison = suspended.MontantLivraison;
            _clientNom = suspended.ClientNom;
            _clientPrenom = suspended.ClientPrenom;
            _clientTelephone = suspended.ClientTelephone;
            _clientAdresse = suspended.ClientAdresse;
            _clientVille = suspended.ClientVille;
            _clientCodePostal = suspended.ClientCodePostal;
            _clientCommentaire = suspended.ClientCommentaire;
            CommandeModePaiement = suspended.ModePaiement;

            SuspendedCommandes.Remove(suspended);
            OnPropertyChanged(nameof(HasSuspendedCommandes));

            ApplyCommandePromotions();
            UpdateCommandeTotal();

            if (!IsNewCommandeMode) IsNewCommandeMode = true;
        }

        /// <summary>Entreprise chargée avec le catalogue (sans accès base sur le thread UI), sinon cache local.</summary>
        private Entreprise GetEntreprise()
            => _entreprise ?? _services.Cache.Load()?.Entreprise ?? new Entreprise { Nom = "Inconnu" };

        // ─── Multi-select helpers ───
        private void Commande_IsSelectedChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Commande.IsSelected))
                NotifySelectionChanged();
        }

        private void NotifySelectionChanged()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionCount));
            OnPropertyChanged(nameof(SelectedCommandes));
            // Update IsAllSelected without triggering the setter loop
            _isAllSelected = Commandes.Count > 0 && Commandes.All(c => c.IsSelected);
            OnPropertyChanged(nameof(IsAllSelected));
        }

        private async void BatchChangeStatus(string newStatus)
        {
            var selected = SelectedCommandes;
            if (selected.Count == 0) return;

            string displayStatus = newStatus switch
            {
                "validee" => "Validée",
                "traitee" => "Traitée",
                "en_attente" => "En attente",
                "annulee" => "Annulée",
                _ => newStatus
            };

            if (MessageBox.Show(WindowHelper.GetAdminWindow(),
                $"Passer {selected.Count} commande(s) en \"{displayStatus}\" ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                // Une seule transaction : stock décrémenté une seule fois par commande passée en "traitee".
                await _services.Commandes.ChangeStatusAsync(selected.Select(c => c.Id).ToList(), newStatus);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Changement de statut groupé impossible", ex);
                MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BatchDelete()
        {
            if (IsTrashMode)
            {
                BatchPermanentDelete();
                return;
            }

            var selected = SelectedCommandes;
            if (selected.Count == 0) return;

            if (MessageBox.Show(WindowHelper.GetAdminWindow(),
                $"Mettre {selected.Count} commande(s) dans la corbeille ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                await _services.Commandes.ChangeStatusAsync(selected.Select(c => c.Id).ToList(), "supprimee");
                SelectedCommande = null;
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Mise à la corbeille groupée impossible", ex);
                MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BatchRestore()
        {
            var selected = SelectedCommandes;
            if (selected.Count == 0) return;

            try
            {
                await _services.Commandes.ChangeStatusAsync(selected.Select(c => c.Id).ToList(), "en_attente");
                SelectedCommande = null;
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Restauration groupée impossible", ex);
                MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BatchPermanentDelete()
        {
            var selected = SelectedCommandes;
            if (selected.Count == 0) return;

            if (MessageBox.Show(WindowHelper.GetAdminWindow(),
                $"Supprimer définitivement {selected.Count} commande(s) ?\nCette action est irréversible.",
                "Suppression définitive", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                await _services.Commandes.DeleteAsync(selected.Select(c => c.Id).ToList());
                SelectedCommande = null;
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Commandes", "Suppression définitive groupée impossible", ex);
                MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BatchPrint()
        {
            var selected = SelectedCommandes;
            if (selected.Count == 0) return;
            var entreprise = GetEntreprise();
            foreach (var cmd in selected)
            {
                _printService.PrintCommandeTicket(cmd, entreprise);
            }
        }

        private void BatchRecap()
        {
            var selected = SelectedCommandes;
            if (selected.Count == 0) return;
            var entreprise = GetEntreprise();
            var win = new Views.CommandeRecapWindow(selected, _printService, entreprise, GetCatalogProducts());
            win.Owner = WindowHelper.GetAdminWindow();
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowDialog();
        }

        // ─── Recap Summary (bottom bar) ───
        private void UpdateRecapSummary()
        {
            int regle = 0, partiel = 0, nonRegle = 0;
            decimal cb = 0, espece = 0, enLigne = 0, wero = 0, virement = 0, surPlace = 0, general = 0;

            foreach (var c in Commandes)
            {
                switch (c.StatutPaiement)
                {
                    case "regle": regle++; break;
                    case "partiel": partiel++; break;
                    case "non_regle": nonRegle++; break;
                }

                switch (c.ModePaiement)
                {
                    case "cb": cb += c.MontantPaye; break;
                    case "espece": espece += c.MontantPaye; break;
                    case "en_ligne": enLigne += c.MontantPaye; break;
                    case "wero": wero += c.MontantPaye; break;
                    case "virement": virement += c.MontantPaye; break;
                    case "sur_place": surPlace += c.MontantPaye; break;
                }

                general += c.TotalAvecLivraison;
            }

            CommandesCount = Commandes.Count;
            CommandesRegleCount = regle;
            CommandesPartielCount = partiel;
            CommandesNonRegleCount = nonRegle;
            TotalCB = cb;
            TotalEspece = espece;
            TotalEnLigne = enLigne;
            TotalWero = wero;
            TotalVirement = virement;
            TotalSurPlace = surPlace;
            TotalGeneral = general;
        }

        // ─── Client Recap ───
        private void PrintClientRecap()
        {
            if (Commandes.Count == 0) return;
            var entreprise = GetEntreprise();
            var win = new Views.CommandeClientRecapWindow(Commandes.ToList(), _printService, entreprise);
            win.Owner = WindowHelper.GetAdminWindow();
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowDialog();
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Représente un sous-client dans une commande regroupée (ex: Client 1, Client 2...).</summary>
    public class SubClientGroup : INotifyPropertyChanged
    {
        private string _nom;
        public string Nom
        {
            get => _nom;
            set
            {
                if (_nom != value)
                {
                    _nom = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayBadge));
                }
            }
        }

        private decimal _total;
        public decimal Total
        {
            get => _total;
            set
            {
                if (_total != value)
                {
                    _total = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayBadge));
                }
            }
        }

        private int _nbArticles;
        public int NbArticles
        {
            get => _nbArticles;
            set
            {
                if (_nbArticles != value)
                {
                    _nbArticles = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayBadge));
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayBadge => $"{Nom} ({Total:0.00} €)";

        public SubClientGroup(string nom)
        {
            _nom = nom;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SuspendedCommande
    {
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
        public List<CartItemViewModel> Items { get; set; } = new();
        public bool IsGrouped { get; set; }
        public List<string> SubClientNames { get; set; } = new();
        public decimal MontantPaye { get; set; }
        public bool AvecLivraison { get; set; }
        public decimal MontantLivraison { get; set; }
        public string ClientNom { get; set; } = string.Empty;
        public string ClientPrenom { get; set; } = string.Empty;
        public string ClientTelephone { get; set; } = string.Empty;
        public string ClientAdresse { get; set; } = string.Empty;
        public string ClientVille { get; set; } = string.Empty;
        public string ClientCodePostal { get; set; } = string.Empty;
        public string ClientCommentaire { get; set; } = string.Empty;
        public string ModePaiement { get; set; } = "en_ligne";
        public decimal Total { get; set; }
        public string Label { get; set; } = string.Empty;
        public override string ToString() => Label;
    }
}
