using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Services;
using SystemeCaisse.UI.Services;
using System.IO;

namespace SystemeCaisse.UI.ViewModels
{
    /// <summary>
    /// Onglet Produits. Les produits sont chargés en lecture seule (aucun DbContext gardé ouvert) ;
    /// à l'enregistrement, seuls les produits réellement modifiés sont envoyés, et le stock est
    /// appliqué en delta pour ne jamais écraser les ventes faites entre-temps sur les autres postes.
    /// </summary>
    public partial class ProductsViewModel : ObservableObject
    {
        private readonly CaisseServices _services;

        /// <summary>Valeurs au chargement, par instance de produit (pour détecter les modifications).</summary>
        private readonly Dictionary<Produit, ProductSnapshot> _snapshots = new(ReferenceEqualityComparer.Instance);

        [ObservableProperty]
        private ObservableCollection<Produit> _products = new();

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

        [ObservableProperty]
        private bool _isBusy;

        public ICollectionView? ProductsCollectionView { get; private set; }

        public bool IsDetailVisible => SelectedProduct != null;

        /// <summary>Date du dernier chargement réussi (utilisé pour le rafraîchissement à l'ouverture de l'onglet).</summary>
        public DateTime LastLoaded { get; private set; } = DateTime.MinValue;

        public ProductsViewModel(CaisseServices services)
        {
            _services = services;
            Categories = new ObservableCollection<string>();
            EditCategories = new ObservableCollection<string>();
            Fournisseurs = new ObservableCollection<Fournisseur>();
        }

        public async Task InitializeAsync()
        {
            await LoadDataInternalAsync();
        }

        /// <summary>True si l'utilisateur a des modifications non enregistrées (on ne recharge pas sous ses pieds).</summary>
        public bool HasUnsavedChanges =>
            _pendingNewProduct != null || _snapshots.Any(kv => kv.Value.DiffersFrom(kv.Key));

        /// <summary>Recharge si aucune saisie n'est en cours.</summary>
        public async Task RefreshIfIdleAsync()
        {
            if (IsBusy || HasUnsavedChanges) return;
            await LoadDataInternalAsync();
        }

        partial void OnSearchTextChanged(string value) => ProductsCollectionView?.Refresh();
        partial void OnSelectedCategoryFilterChanged(string value) => ProductsCollectionView?.Refresh();
        partial void OnShowActiveChanged(bool value) => ProductsCollectionView?.Refresh();
        partial void OnShowInactiveChanged(bool value) => ProductsCollectionView?.Refresh();

        [RelayCommand]
        private async Task LoadData() => await LoadDataInternalAsync();

        [RelayCommand]
        private async Task RefreshProducts()
        {
            if (HasUnsavedChanges &&
                MessageBox.Show(WindowHelper.GetAdminWindow(), "Des modifications ne sont pas enregistrées. Les abandonner et recharger ?",
                    "Actualiser", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            await LoadDataInternalAsync(showErrorDialog: true);
        }

        private async Task LoadDataInternalAsync(bool showErrorDialog = false)
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                int? selectedId = SelectedProduct?.Id;

                // Lectures en parallèle, hors thread UI, sans suivi EF.
                var productsTask = _services.Catalog.GetProductsAsync(includeFournisseur: true);
                var fournisseursTask = _services.Catalog.GetFournisseursAsync();
                var popularityTask = _services.Popularity.GetAsync();
                await Task.WhenAll(productsTask, fournisseursTask, popularityTask);

                var products = productsTask.Result;
                var popularity = popularityTask.Result;
                foreach (var p in products)
                {
                    if (string.IsNullOrWhiteSpace(p.Categorie)) p.Categorie = "Autre";
                    p.ValidatedSalesCount = popularity.Get(p.Id, p.Nom);
                }

                _pendingNewProduct = null;
                _snapshots.Clear();
                foreach (var p in products) _snapshots[p] = ProductSnapshot.Of(p);

                Fournisseurs = new ObservableCollection<Fournisseur>(fournisseursTask.Result);
                Products = new ObservableCollection<Produit>(products);

                ProductsCollectionView = CollectionViewSource.GetDefaultView(Products);
                ProductsCollectionView.Filter = FilterProducts;
                // Sort by Popularity (Descending) then Name
                ProductsCollectionView.SortDescriptions.Clear();
                ProductsCollectionView.SortDescriptions.Add(new SortDescription(nameof(Produit.ValidatedSalesCount), ListSortDirection.Descending));
                ProductsCollectionView.SortDescriptions.Add(new SortDescription(nameof(Produit.Nom), ListSortDirection.Ascending));
                OnPropertyChanged(nameof(ProductsCollectionView));

                RebuildCategories(products);
                SelectedProduct = selectedId.HasValue ? Products.FirstOrDefault(p => p.Id == selectedId.Value) : null;
                LastLoaded = DateTime.Now;
            }
            catch (Exception ex)
            {
                AppLog.Error("Produits", "Chargement des produits impossible", ex);
                if (showErrorDialog)
                {
                    MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur chargement : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RebuildCategories(IEnumerable<Produit> products)
        {
            var cats = products.Select(p => p.Categorie).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
            var currentFilter = SelectedCategoryFilter;

            Categories.Clear();
            Categories.Add("Toutes");
            foreach (var c in cats) Categories.Add(c!);
            SelectedCategoryFilter = Categories.Contains(currentFilter) ? currentFilter : "Toutes";

            EditCategories.Clear();
            foreach (var c in cats) EditCategories.Add(c!);
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
            // Not sent to the database until Save
            _pendingNewProduct = newProduct;
            Products.Add(newProduct);
            SelectedProduct = newProduct;
        }

        [RelayCommand]
        private async Task Delete()
        {
            var product = SelectedProduct;
            if (product == null) return;
            var owner = WindowHelper.GetAdminWindow();

            if (ReferenceEquals(product, _pendingNewProduct))
            {
                Products.Remove(product);
                _pendingNewProduct = null;
                SelectedProduct = null;
                return;
            }

            try
            {
                bool hasSales = await _services.Catalog.HasSalesAsync(product.Id);
                if (hasSales)
                {
                    var result = MessageBox.Show(owner, $"Ce produit a déjà été vendu et ne peut pas être supprimé pour préserver l'historique.\n\nVoulez-vous le DÉSACTIVER ? (Il ne sera plus visible en caisse)", 
                                               "Historique existant", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                    {
                        product.Actif = false;
                        await Save();
                    }
                    return;
                }

                if (MessageBox.Show(owner, $"Supprimer '{product.Nom}' ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    await _services.Catalog.DeleteProductAsync(product.Id);
                    _snapshots.Remove(product);
                    Products.Remove(product);
                    SelectedProduct = null;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Produits", "Suppression impossible", ex);
                MessageBox.Show(owner, $"Erreur lors de la suppression : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task Save()
        {
            var owner = WindowHelper.GetAdminWindow();
            try
            {
                var changes = new List<ProductChange>();
                foreach (var p in Products)
                {
                    if (string.IsNullOrWhiteSpace(p.Categorie)) p.Categorie = "Autre";
                    p.Nom = p.Nom?.ToUpper() ?? string.Empty;

                    if (ReferenceEquals(p, _pendingNewProduct) || p.Id == 0)
                        changes.Add(new ProductChange(p, null));
                    else if (_snapshots.TryGetValue(p, out var original) && original.DiffersFrom(p))
                        changes.Add(new ProductChange(p, original));
                }

                if (changes.Count == 0)
                {
                    MessageBox.Show(owner, "Aucune modification à enregistrer.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                await _services.Catalog.SaveProductsAsync(changes);
                _pendingNewProduct = null;
                MessageBox.Show(owner, "Enregistré avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);

                // Relire : récupère les Id des nouveaux produits et le stock réel (ventes des autres postes incluses).
                await LoadDataInternalAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Produits", "Enregistrement impossible", ex);
                MessageBox.Show(owner, $"Erreur lors de l'enregistrement : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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

        /// <summary>
        /// L'image est enregistrée EN BASE (visible immédiatement par tous les postes) et dans le cache local.
        /// Le produit garde la clé de l'image dans ImagePath (enregistrée au prochain « Enregistrer »).
        /// </summary>
        [RelayCommand]
        private async Task SelectProductImage()
        {
            var product = SelectedProduct;
            if (product == null) return;
            var owner = WindowHelper.GetAdminWindow();

            var openDlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp",
                Title = "Sélectionner une image pour le produit"
            };
            if (openDlg.ShowDialog(owner) != true) return;

            try
            {
                var info = new FileInfo(openDlg.FileName);
                if (info.Length > 5 * 1024 * 1024)
                {
                    MessageBox.Show(owner, "Image trop lourde (5 Mo maximum).", "Image", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var ext = Path.GetExtension(openDlg.FileName).ToLowerInvariant();
                var name = product.Id > 0 ? $"produit_{product.Id}_{DateTime.Now.Ticks}{ext}" : $"produit_new_{DateTime.Now.Ticks}{ext}";
                var key = await _services.Images.SaveFileAsync($"Images/Produits/{name}", openDlg.FileName);

                product.ImagePath = key;
                OnPropertyChanged(nameof(SelectedProduct));
                ProductsCollectionView?.Refresh();
            }
            catch (Exception ex)
            {
                AppLog.Error("Produits", "Enregistrement de l'image impossible", ex);
                MessageBox.Show(owner, $"Erreur lors de l'enregistrement de l'image : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void RemoveProductImage()
        {
            if (SelectedProduct == null) return;
            // L'image reste en base (elle peut être référencée par une sauvegarde) ; le produit ne la référence plus.
            SelectedProduct.ImagePath = null;
            OnPropertyChanged(nameof(SelectedProduct));
            ProductsCollectionView?.Refresh();
        }

        /// <summary>Rafraîchit l'affichage quand de nouvelles images arrivent dans le cache.</summary>
        public void RefreshImages()
        {
            ProductsCollectionView?.Refresh();
            OnPropertyChanged(nameof(SelectedProduct));
        }
    }
}
