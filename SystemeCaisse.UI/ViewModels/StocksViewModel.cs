using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.UI.Services;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Windows.Data;

namespace SystemeCaisse.UI.ViewModels
{
    public class StocksViewModel : INotifyPropertyChanged
    {
        private readonly CaisseServices _services;
        private bool _isLoading;

        /// <summary>Date du dernier chargement réussi.</summary>
        public DateTime LastLoaded { get; private set; } = DateTime.MinValue;

        public ObservableCollection<Produit> Produits { get; private set; }
        public ICollectionView OverviewProductsView { get; private set; }
        
        public ObservableCollection<Produit> Alertes { get; private set; }
        public ObservableCollection<MouvementStock> Mouvements { get; private set; }

        public ICommand LoadCommand { get; }
        public ICommand ValiderMouvementCommand { get; }

        // Dashboard Stats
        private decimal _valeurTotaleStock;
        public decimal ValeurTotaleStock
        {
            get => _valeurTotaleStock;
            set { _valeurTotaleStock = value; OnPropertyChanged(); }
        }

        private int _nombreAlertes;
        public int NombreAlertes
        {
            get => _nombreAlertes;
            set { _nombreAlertes = value; OnPropertyChanged(); }
        }

        private int _nombreRuptures;
        public int NombreRuptures
        {
            get => _nombreRuptures;
            set { _nombreRuptures = value; OnPropertyChanged(); }
        }

        // Filters
        private string _filterSearch;
        public string FilterSearch
        {
            get => _filterSearch;
            set 
            {
                if (_filterSearch != value)
                {
                    _filterSearch = value; 
                    OnPropertyChanged(); 
                    OverviewProductsView?.Refresh();
                }
            }
        }

        private bool _filterAlertOnly;
        public bool FilterAlertOnly
        {
            get => _filterAlertOnly;
            set 
            { 
                 if (_filterAlertOnly != value)
                 {
                    _filterAlertOnly = value; 
                    OnPropertyChanged();
                    OverviewProductsView?.Refresh();
                 }
            }
        }

        private bool _filterRuptureOnly;
        public bool FilterRuptureOnly
        {
            get => _filterRuptureOnly;
            set 
            {
                 if (_filterRuptureOnly != value)
                 {
                    _filterRuptureOnly = value; 
                    OnPropertyChanged();
                    OverviewProductsView?.Refresh();
                 }
            }
        }

        // Form Fields
        // Form Fields
        private Produit? _selectedProduitMouvement;
        public Produit? SelectedProduitMouvement
        {
            get => _selectedProduitMouvement;
            set { _selectedProduitMouvement = value; OnPropertyChanged(); }
        }

        private string _typeMouvement = "entree";
        public string TypeMouvement
        {
            get => _typeMouvement;
            set { _typeMouvement = value; OnPropertyChanged(); }
        }

        private decimal _quantiteMouvement;
        public decimal QuantiteMouvement
        {
            get => _quantiteMouvement;
            set { _quantiteMouvement = value; OnPropertyChanged(); }
        }

        private decimal _prixUnitaireMouvement;
        public decimal PrixUnitaireMouvement
        {
            get => _prixUnitaireMouvement;
            set { _prixUnitaireMouvement = value; OnPropertyChanged(); }
        }

        private string _commentaireMouvement;
        public string CommentaireMouvement
        {
            get => _commentaireMouvement;
            set { _commentaireMouvement = value; OnPropertyChanged(); }
        }

        public StocksViewModel(CaisseServices services)
        {
            _services = services;
            
            Produits = new ObservableCollection<Produit>(); // Initialize once
            Alertes = new ObservableCollection<Produit>();
            Mouvements = new ObservableCollection<MouvementStock>();

            OverviewProductsView = CollectionViewSource.GetDefaultView(Produits); // View over the stable collection
            OverviewProductsView.Filter = FilterOverview;

            LoadCommand = new BasicRelayCommand(_ => _ = LoadDataAsync());
            ValiderMouvementCommand = new BasicRelayCommand(ValiderMouvement, _ => SelectedProduitMouvement != null && QuantiteMouvement > 0);
        }

        public async Task InitializeAsync()
        {
            await LoadDataAsync();
        }

        /// <summary>
        /// Charge les produits, les 100 derniers mouvements et la date de dernière entrée
        /// (agrégats calculés par la base, plus de chargement de tout l'historique).
        /// </summary>
        public async Task LoadDataAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            try
            {
                var productsTask = _services.Catalog.GetProductsAsync();
                var movementsTask = _services.Catalog.GetLastMovementsAsync(100);
                var entriesTask = _services.Catalog.GetLastEntryDatesAsync();
                await Task.WhenAll(productsTask, movementsTask, entriesTask);

                var entries = entriesTask.Result;
                int? selectedId = SelectedProduitMouvement?.Id;

                // Update stable collection
                Produits.Clear();
                foreach (var p in productsTask.Result)
                {
                    p.LastEntryDate = entries.TryGetValue(p.Id, out var date) ? date : null;
                    Produits.Add(p);
                }
                OverviewProductsView.Refresh();

                Mouvements.Clear();
                foreach (var m in movementsTask.Result) Mouvements.Add(m);

                if (selectedId.HasValue)
                    SelectedProduitMouvement = Produits.FirstOrDefault(p => p.Id == selectedId.Value);

                CalculateStats();
                LastLoaded = DateTime.Now;
            }
            catch (Exception ex)
            {
                AppLog.Error("Stocks", "Chargement des stocks impossible", ex);
            }
            finally
            {
                _isLoading = false;
            }
        }
        private void CalculateStats()
        {
            ValeurTotaleStock = Produits.Sum(p => p.StockActuel * p.PrixVente); // Use PrixVente for potential value, or PrixAchat for cost
            // Legacy app used PrixVente for value: `valeur = Decimal(str(produit['prix_vente'])) * produit['stock_actuel']`
            
            var alertesList = Produits.Where(p => p.StockActuel <= p.StockAlerte).ToList();
            Alertes = new ObservableCollection<Produit>(alertesList);
            NombreAlertes = alertesList.Count;
            NombreRuptures = Produits.Count(p => p.StockActuel <= 0);
            
            OnPropertyChanged(nameof(Alertes));
        }

        private bool FilterOverview(object obj)
        {
            if (obj is Produit p)
            {
                // 1. Search Text
                bool matchesSearch = string.IsNullOrWhiteSpace(FilterSearch) || 
                                     (p.Nom?.Contains(FilterSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                     (p.CodeBarre?.Contains(FilterSearch, StringComparison.OrdinalIgnoreCase) ?? false);

                if (!matchesSearch) return false;

                // 2. Alert Only
                if (FilterAlertOnly && p.StockActuel > p.StockAlerte) return false;

                // 3. Rupture Only
                if (FilterRuptureOnly && p.StockActuel > 0) return false;

                return true;
            }
            return false;
        }

        private async void ValiderMouvement(object obj)
        {
            var produit = SelectedProduitMouvement;
            if (produit == null) return;
            var mainWin = Services.WindowHelper.GetAdminWindow();

            if (QuantiteMouvement <= 0) 
            {
                MessageBox.Show(mainWin, "La quantité doit être supérieure à 0.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // "entree" / "sortie" / "inventaire" (correction relative) : variation appliquée par la base,
                // sans jamais réécrire un stock lu auparavant (les ventes des autres postes sont préservées).
                await _services.Catalog.RecordMovementAsync(produit.Id, produit.Nom, TypeMouvement, QuantiteMouvement,
                    TypeMouvement == "entree" ? PrixUnitaireMouvement : null, CommentaireMouvement);

                if (Application.Current.MainWindow?.DataContext is MainViewModel mainVM)
                    mainVM.MarkCaisseDirty(catalog: true);

                MessageBox.Show(mainWin, "Mouvement de stock enregistré avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                ResetForm();
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Stocks", "Enregistrement du mouvement impossible", ex);
                MessageBox.Show(mainWin, $"Erreur lors de l'enregistrement du mouvement : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetForm()
        {
            SelectedProduitMouvement = null;
            QuantiteMouvement = 0;
            PrixUnitaireMouvement = 0;
            CommentaireMouvement = "";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) 
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
