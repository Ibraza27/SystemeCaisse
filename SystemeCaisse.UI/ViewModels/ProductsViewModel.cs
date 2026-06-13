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
using System.IO;

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
        }

        public async Task InitializeAsync()
        {
            await LoadDataInternalAsync();
        }

        partial void OnSearchTextChanged(string value) => ProductsCollectionView?.Refresh();
        partial void OnSelectedCategoryFilterChanged(string value) => ProductsCollectionView?.Refresh();
        partial void OnShowActiveChanged(bool value) => ProductsCollectionView?.Refresh();
        partial void OnShowInactiveChanged(bool value) => ProductsCollectionView?.Refresh();

        [RelayCommand]
        private async Task LoadData() => await LoadDataInternalAsync();

        [RelayCommand]
        private async Task RefreshProducts() => await LoadDataInternalAsync();

        private async Task LoadDataInternalAsync()
        {
            try
            {
                _pendingNewProduct = null; // Clear any unsaved new product
                _context?.Dispose();
                _context = await _contextFactory.CreateDbContextAsync();
                
                // Load Fournisseurs
                var fournisseurs = await _context.Fournisseurs.ToListAsync();
                await Application.Current.Dispatcher.InvokeAsync(() => 
                {
                    Fournisseurs = new ObservableCollection<Fournisseur>(fournisseurs);
                });

                // Load Products
                await _context.Produits.Include(p => p.Fournisseur).LoadAsync();
                await Application.Current.Dispatcher.InvokeAsync(() => 
                {
                    Products = _context.Produits.Local.ToObservableCollection();
                });

                // Ensure all products have a category
                foreach (var p in Products)
                {
                    if (string.IsNullOrWhiteSpace(p.Categorie))
                    {
                        p.Categorie = "Autre";
                    }
                }

                // Calculate Popularity
                await CalculateProductPopularity();

                // Setup CollectionView
                await Application.Current.Dispatcher.InvokeAsync(() => 
                {
                    ProductsCollectionView = CollectionViewSource.GetDefaultView(Products);
                    ProductsCollectionView.Filter = FilterProducts;
                    // Sort by Popularity (Descending) then Name
                    ProductsCollectionView.SortDescriptions.Add(new SortDescription("ValidatedSalesCount", ListSortDirection.Descending));
                    ProductsCollectionView.SortDescriptions.Add(new SortDescription("Nom", ListSortDirection.Ascending));
                    OnPropertyChanged(nameof(ProductsCollectionView));
                });

                await LoadCategories();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur chargement : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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

            await Application.Current.Dispatcher.InvokeAsync(() => 
            {
                Categories.Clear();
                Categories.Add("Toutes");
                foreach(var c in cats.OrderBy(c => c)) Categories.Add(c!);
                SelectedCategoryFilter = "Toutes";

                EditCategories.Clear();
                foreach(var c in cats.OrderBy(c => c)) EditCategories.Add(c!);
            });
        }

        [RelayCommand]
        private void ClearSearch() => SearchText = string.Empty;

        private bool FilterProducts(object obj)
        {
            if (obj is not Produit produit) return false;

            // Search Text
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var s = SearchText.Trim().ToLower().Replace(',', '.');
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

        private Produit? _pendingNewProduct;

        [RelayCommand]
        private void Add()
        {
            var newProduct = new Produit 
            { 
                Nom = "NOUVEAU PRODUIT", 
                CodeBarre = string.Empty, // User must scan/enter barcode
                PrixVente = 0,
                StockActuel = 0,
                Actif = true,
                Categorie = "Autre",
                TaxTier = 1
            };
            // Don't add to context yet — only on Save
            _pendingNewProduct = newProduct;
            Products.Add(newProduct);
            SelectedProduct = newProduct;
        }

        [RelayCommand]
        private void Delete()
        {
            if (SelectedProduct == null) return;
            var mainWin = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is SystemeCaisse.UI.MainWindow);
            
            // Check if product has sales
            bool hasSales = _context.LignesVente.Any(l => l.ProduitId == SelectedProduct.Id);

            if (hasSales)
            {
                var result = MessageBox.Show(mainWin, $"Ce produit a déjà été vendu et ne peut pas être supprimé pour préserver l'historique.\n\nVoulez-vous le DÉSACTIVER ? (Il ne sera plus visible en caisse)", 
                                           "Historique existant", MessageBoxButton.YesNo, MessageBoxImage.Information);
                
                if (result == MessageBoxResult.Yes)
                {
                    SelectedProduct.Actif = false;
                    Save();
                }
                return;
            }

            if (MessageBox.Show(mainWin, $"Supprimer '{SelectedProduct.Nom}' ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
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
                // Final safety check
                foreach (var p in Products)
                {
                    if (string.IsNullOrWhiteSpace(p.Categorie)) p.Categorie = "Autre";
                    // Ensure uppercase for all (migration of old data)
                    p.Nom = p.Nom?.ToUpper() ?? string.Empty;
                }
                
                // Add pending new product to context only on explicit Save
                if (_pendingNewProduct != null)
                {
                    _context.Produits.Add(_pendingNewProduct);
                    _pendingNewProduct = null;
                }
                
                _context.SaveChanges();
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Enregistré avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                _ = LoadCategories(); // Refresh categories in case new ones were added
            }
            catch (Exception ex)
            {
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur lors de l'enregistrement : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void GenerateBarcode()
        {
            if (SelectedProduct != null)
            {
                SelectedProduct.CodeBarre = GenerateEan13();
                // Instead of a full null reset which can clear multi-step bindings,
                // we keep it surgical.
                OnPropertyChanged(nameof(SelectedProduct));
            }
        }

        [RelayCommand]
        private void ClearCategory()
        {
            if (SelectedProduct != null)
            {
                SelectedProduct.Categorie = "Autre";
                OnPropertyChanged(nameof(SelectedProduct));
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

        [RelayCommand]
        private void SelectProductImage()
        {
            if (SelectedProduct == null) return;

            var openDlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp",
                Title = "Sélectionner une image pour le produit"
            };

            if (openDlg.ShowDialog(Services.WindowHelper.GetAdminWindow()) == true)
            {
                try
                {
                    // Remember old path to delete
                    var oldPath = SelectedProduct.FullImagePath;

                    // Create Images/Produits folder
                    var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Produits");
                    Directory.CreateDirectory(imagesDir);

                    // Copy with product-based name with timestamp to avoid cache issues
                    var ext = Path.GetExtension(openDlg.FileName);
                    var timestamp = DateTime.Now.Ticks;
                    
                    var fileName = SelectedProduct.Id > 0 
                        ? $"produit_{SelectedProduct.Id}_{timestamp}{ext}" 
                        : $"produit_new_{timestamp}{ext}";

                    var destPath = Path.Combine(imagesDir, fileName);
                    var relativePath = Path.Combine("Images", "Produits", fileName);

                    // Clear the image path first to release WPF file lock
                    SelectedProduct.ImagePath = null;
                    OnPropertyChanged(nameof(SelectedProduct));

                    // Small delay to let WPF release the file handle
                    System.Threading.Thread.Sleep(50);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    // Delete old image if exists
                    if (!string.IsNullOrEmpty(oldPath) && File.Exists(oldPath))
                    {
                        try { File.Delete(oldPath); } catch { /* Ignore locked files */ }
                    }

                    // Copy using FileStream to handle potential locks
                    using (var source = new FileStream(openDlg.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        source.CopyTo(dest);
                    }

                    // Set the new path
                    SelectedProduct.ImagePath = relativePath;
                    OnPropertyChanged(nameof(SelectedProduct));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur lors de la copie de l'image : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void RemoveProductImage()
        {
            if (SelectedProduct == null) return;
            
            var oldPath = SelectedProduct.FullImagePath;
            
            SelectedProduct.ImagePath = null;
            OnPropertyChanged(nameof(SelectedProduct));

            System.Threading.Thread.Sleep(50);
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (!string.IsNullOrEmpty(oldPath) && File.Exists(oldPath))
            {
                try { File.Delete(oldPath); } catch { /* Ignore locked files */ }
            }
        }
    }
}
