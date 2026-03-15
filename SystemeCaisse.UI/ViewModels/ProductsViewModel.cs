using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.UI.ViewModels
{
    public partial class ProductsViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private AppDbContext _context;

        [ObservableProperty]
        private ObservableCollection<Produit> _products;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
        private Produit? _selectedProduct;

        [ObservableProperty]
        private ObservableCollection<Fournisseur> _fournisseurs;

        [ObservableProperty]
        private ObservableCollection<string> _categories; // For Filter (includes "Toutes")

        [ObservableProperty]
        private ObservableCollection<string> _editCategories; // For Edit (distinct list)

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedCategoryFilter = "Toutes";

        [ObservableProperty]
        private bool _showActive = true;

        [ObservableProperty]
        private bool _showInactive = true;

        public ICollectionView ProductsCollectionView { get; private set; }

        public bool IsDetailVisible => SelectedProduct != null;

        public ProductsViewModel(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            Categories = new ObservableCollection<string>();
            EditCategories = new ObservableCollection<string>();
            Fournisseurs = new ObservableCollection<Fournisseur>();
            
            LoadDataCommand.Execute(null);
        }

        partial void OnSearchTextChanged(string value) => ProductsCollectionView?.Refresh();
        partial void OnSelectedCategoryFilterChanged(string value) => ProductsCollectionView?.Refresh();
        partial void OnShowActiveChanged(bool value) => ProductsCollectionView?.Refresh();
        partial void OnShowInactiveChanged(bool value) => ProductsCollectionView?.Refresh();

        [RelayCommand]
        private async Task LoadData()
        {
            try
            {
                _context?.Dispose();
                _context = await _contextFactory.CreateDbContextAsync();
                
                // Load Fournisseurs
                var fournisseurs = await _context.Fournisseurs.ToListAsync();
                Fournisseurs = new ObservableCollection<Fournisseur>(fournisseurs);

                // Load Products
                await _context.Produits.Include(p => p.Fournisseur).LoadAsync();
                Products = _context.Produits.Local.ToObservableCollection();

                // Calculate Popularity
                await CalculateProductPopularity();

                // Setup CollectionView
                ProductsCollectionView = CollectionViewSource.GetDefaultView(Products);
                ProductsCollectionView.Filter = FilterProducts;
                // Sort by Popularity (Descending) then Name
                ProductsCollectionView.SortDescriptions.Add(new SortDescription("ValidatedSalesCount", ListSortDirection.Descending));
                ProductsCollectionView.SortDescriptions.Add(new SortDescription("Nom", ListSortDirection.Ascending));
                OnPropertyChanged(nameof(ProductsCollectionView));

                await LoadCategories();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur chargement : {ex.Message}");
            }
        }

        private async Task CalculateProductPopularity()
        {
            try
            {
                // Ensure db context is alive
                if (_context == null) _context = await _contextFactory.CreateDbContextAsync();

                var allLines = await _context.LignesVente.AsNoTracking()
                    .Select(l => new { l.ProduitId, l.Quantite })
                    .ToListAsync();

                var salesStats = allLines
                    .GroupBy(l => l.ProduitId)
                    .Select(g => new { Id = g.Key, Count = g.Sum(x => x.Quantite) })
                    .ToList();

                foreach (var p in Products)
                {
                    var stat = salesStats.FirstOrDefault(s => s.Id == p.Id);
                    p.ValidatedSalesCount = stat?.Count ?? 0;
                }
            }
            catch 
            {
                 // System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private async Task LoadCategories()
        {
            var cats = await _context.Produits
                .Where(p => !string.IsNullOrEmpty(p.Categorie))
                .Select(p => p.Categorie)
                .Distinct()
                .ToListAsync();

            Categories.Clear();
            Categories.Add("Toutes");
            foreach(var c in cats.OrderBy(c => c)) Categories.Add(c!);
            SelectedCategoryFilter = "Toutes";

            EditCategories.Clear();
            foreach(var c in cats.OrderBy(c => c)) EditCategories.Add(c!);
        }

        [RelayCommand]
        private void ClearSearch() => SearchText = string.Empty;

        private bool FilterProducts(object obj)
        {
            if (obj is not Produit produit) return false;

            // Search Text
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var s = SearchText.Trim().ToLower();
                bool match = (produit.Nom != null && produit.Nom.ToLower().Contains(s)) ||
                             (produit.CodeBarre != null && produit.CodeBarre.ToLower().Contains(s));
                if (!match) return false;
            }

            // Category Filter
            if (!string.IsNullOrEmpty(SelectedCategoryFilter) && SelectedCategoryFilter != "Toutes")
            {
                if (produit.Categorie != SelectedCategoryFilter) return false;
            }

            // Active Filter
            if (produit.Actif && !ShowActive) return false;
            if (!produit.Actif && !ShowInactive) return false;

            return true;
        }

        [RelayCommand]
        private void Add()
        {
            var newProduct = new Produit 
            { 
                Nom = "Nouveau Produit", 
                CodeBarre = GenerateEan13(),
                PrixVente = 0,
                StockActuel = 0,
                Actif = true,
                Categorie = "Divers"
            };
            _context.Produits.Add(newProduct);
            SelectedProduct = newProduct;
        }

        [RelayCommand]
        private void Delete()
        {
            if (SelectedProduct == null) return;
            
            // Check if product has sales
            bool hasSales = _context.LignesVente.Any(l => l.ProduitId == SelectedProduct.Id);

            if (hasSales)
            {
                var result = MessageBox.Show($"Ce produit a déjà été vendu et ne peut pas être supprimé pour préserver l'historique.\n\nVoulez-vous le DÉSACTIVER ? (Il ne sera plus visible en caisse)", 
                                           "Historique existant", MessageBoxButton.YesNo, MessageBoxImage.Information);
                
                if (result == MessageBoxResult.Yes)
                {
                    SelectedProduct.Actif = false;
                    Save();
                }
                return;
            }

            if (MessageBox.Show($"Supprimer '{SelectedProduct.Nom}' ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _context.Produits.Remove(SelectedProduct);
                SelectedProduct = null;
                Save();
            }
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                _context.SaveChanges();
                MessageBox.Show("Enregistré avec succès !");
                _ = LoadCategories(); // Refresh categories in case new ones were added
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur enregistrement : {ex.Message}");
            }
        }

        [RelayCommand]
        private void GenerateBarcode()
        {
            if (SelectedProduct != null)
            {
                SelectedProduct.CodeBarre = GenerateEan13();
            }
        }

        [RelayCommand]
        private void ClearCategory()
        {
            if (SelectedProduct != null)
            {
                SelectedProduct.Categorie = null;
            }
        }

        private string GenerateEan13()
        {
            var random = new Random();
            string code = "200" + random.Next(100000000, 999999999).ToString();
            int sum = 0;
            for (int i = 0; i < 12; i++)
            {
                int digit = int.Parse(code[i].ToString());
                sum += (i % 2 == 0) ? digit : digit * 3;
            }
            int checksum = (10 - (sum % 10)) % 10;
            return code + checksum;
        }
    }
}
