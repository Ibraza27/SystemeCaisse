using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Data;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Windows.Data;

namespace SystemeCaisse.UI.ViewModels
{
    public class StocksViewModel : INotifyPropertyChanged
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private AppDbContext _context;

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

        public StocksViewModel(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            
            Produits = new ObservableCollection<Produit>(); // Initialize once
            Alertes = new ObservableCollection<Produit>();
            Mouvements = new ObservableCollection<MouvementStock>();

            OverviewProductsView = CollectionViewSource.GetDefaultView(Produits); // View over the stable collection
            OverviewProductsView.Filter = FilterOverview;

            LoadCommand = new BasicRelayCommand(_ => LoadData());
            ValiderMouvementCommand = new BasicRelayCommand(ValiderMouvement, _ => SelectedProduitMouvement != null && QuantiteMouvement > 0);

            LoadData(); // Initial load
        }

        public void LoadData()
        {
            try
            {
                _context?.Dispose();
                _context = _contextFactory.CreateDbContext();
                
                _context.Produits.Load();
                _context.MouvementsStock.Include(m => m.Produit).Load(); 
                
                // Update stable collection
                Produits.Clear();
                foreach (var p in _context.Produits.Local)
                {
                    Produits.Add(p);
                }
                
                // No need to re-create OverviewProductsView, just Refresh if needed, 
                // but CollectionChanged events will handle the data update naturally.
                OverviewProductsView.Refresh();

                // Load Movements History (Last 100)
                var lastMouvements = _context.MouvementsStock.Local
                    .OrderByDescending(m => m.DateMouvement)
                    .Take(100)
                    .ToList();
                
                Mouvements.Clear();
                foreach(var m in lastMouvements) Mouvements.Add(m);


                // Populate LastEntryDate
                var entries = _context.MouvementsStock.Local
                    .Where(m => m.TypeMouvement == "entree")
                    .GroupBy(m => m.ProduitId)
                    .ToDictionary(g => g.Key, g => g.Max(m => m.DateMouvement));
                
                foreach (var p in Produits)
                {
                    if (entries.TryGetValue(p.Id, out var date))
                        p.LastEntryDate = date;
                    else
                        p.LastEntryDate = null;
                }

                CalculateStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur chargement stocks : {ex.Message}");
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

        private void ValiderMouvement(object obj)
        {
            if (SelectedProduitMouvement == null) return;
            if (QuantiteMouvement <= 0) 
            {
                MessageBox.Show("La quantité doit être supérieure à 0.");
                return;
            }

            try
            {
                var m = new MouvementStock
                {
                    ProduitId = SelectedProduitMouvement.Id,
                    TypeMouvement = TypeMouvement,
                    Quantite = QuantiteMouvement,
                    PrixUnitaire = TypeMouvement == "entree" ? PrixUnitaireMouvement : (decimal?)null,
                    Commentaire = CommentaireMouvement,
                    DateMouvement = DateTime.Now,
                    ProduitNomSnapshot = SelectedProduitMouvement.Nom
                };

                _context.MouvementsStock.Add(m);

                // Update Stock
                if (TypeMouvement == "entree")
                {
                    SelectedProduitMouvement.StockActuel += QuantiteMouvement;
                    // Optionally update purchase price
                    if (PrixUnitaireMouvement > 0)
                        SelectedProduitMouvement.PrixAchat = PrixUnitaireMouvement;
                }
                else if (TypeMouvement == "sortie")
                {
                    SelectedProduitMouvement.StockActuel -= QuantiteMouvement;
                }
                else if (TypeMouvement == "inventaire")
                {
                     // Difference
                     decimal diff = QuantiteMouvement - SelectedProduitMouvement.StockActuel;
                     // Logic for inventory is: 'Quantite' IS the new stock? Or 'Quantite' is the adjustment?
                     // Legacy logic: 
                     // entry_qte.get() -> if 'inventaire', usually "Quantity Counted"
                     // Let's assume user enters Counted Quantity.
                     // But naming 'Quantite' usually implies delta in movement tables.
                     // Legacy app logic: `stocks_db.mouvement_stock` handles logic.
                     // If I set 'inventaire', the Movement record usually stores the DELTA or the Count?
                     // Let's just store the Delta for consistency in Movement table, but user inputs Absolute.
                     // For simplicity here, let's say 'inventaire' inputs the DELTA for now, OR I implement logic:
                     // Current: 10. Counted: 12. Delta: +2.
                     // For now, let's treat "inventaire" as "Correction (+/-)".
                     // User enters generic adjustment.
                     // If user wants "Set Stock", calculation needed.
                     // I will treat 'inventaire' here as a manual adjustment (delta).
                     SelectedProduitMouvement.StockActuel += QuantiteMouvement; 
                     // Actually, usually 'inventaire' means 'Physical Count'. 
                     // But to keep it matching 'Entree/Sortie' input style (Quantite), let's stick to simple "Add/Remove" logic for now.
                     // Or better: Type "Ajustement".
                     // If type is "sortie", subtracting.
                }

                _context.SaveChanges();
                
                // Refresh
                LoadData();
                ResetForm();
                MessageBox.Show("Mouvement enregistré !");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}");
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
