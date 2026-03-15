using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.UI.ViewModels
{
    public partial class PromotionsViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        
        [ObservableProperty]
        private ObservableCollection<Promotion> _promotions = new();

        [ObservableProperty]
        private string _newNom = string.Empty;

        [ObservableProperty]
        private decimal _newValeur;

        [ObservableProperty]
        private bool _newIsPourcentage;

        [ObservableProperty]
        private Promotion _selectedPromotion;

        [ObservableProperty]
        private ObservableCollection<Produit> _availableProduits;

        [ObservableProperty]
        private ObservableCollection<string> _availableCategories;

        [ObservableProperty]
        private string[] _promotionTypes = new[] { 
            "remise_total", "remise_produit", "quantite_offerte", "remise_ieme", "prix_degressif", "seuil_panier", "offre_combine" 
        };

        [ObservableProperty]
        private string _newTypePromotion = "remise_total";

        [ObservableProperty]
        private Produit? _newSelectedProduit;

        [ObservableProperty]
        private string? _newSelectedCategorie;

        [ObservableProperty]
        private decimal? _newSeuilQuantite;

        [ObservableProperty]
        private decimal? _newQuantiteOfferte;

        [ObservableProperty]
        private int? _newIemeArticle;

        [ObservableProperty]
        private decimal? _newRemiseSurIeme;

        [ObservableProperty]
        private decimal? _newSeuilPanier;

        [ObservableProperty]
        private ObservableCollection<PromotionTier> _newTiers = new();

        [ObservableProperty]
        private DateTime _newDateDebut = DateTime.Today;

        [ObservableProperty]
        private DateTime _newDateFin = DateTime.Today.AddDays(7);

        [ObservableProperty]
        private string _productSearchText = string.Empty;

        [ObservableProperty]
        private bool _isEditing;

        [ObservableProperty]
        private bool _isProductDropDownOpen;

        [ObservableProperty]
        private decimal _bundleItemQuantity = 1;

        private int _editingPromotionId;
        private bool _isUpdatingSelection;

        public ObservableCollection<Produit> FilteredProduits { get; private set; } = new();
        public ObservableCollection<PromotionBundleItem> CurrentBundleItems { get; private set; } = new();

        partial void OnProductSearchTextChanged(string value)
        {
            if (_isUpdatingSelection) return;
            
            // Clear current selection as user is doing a new manual search
            _isUpdatingSelection = true;
            NewSelectedProduit = null;
            _isUpdatingSelection = false;

            RefreshFilteredProducts();
            
            if (FilteredProduits.Any())
            {
                IsProductDropDownOpen = true;
            }
        }

        partial void OnNewSelectedProduitChanged(Produit? value)
        {
            if (value != null && !_isUpdatingSelection)
            {
                _isUpdatingSelection = true;
                ProductSearchText = value.Nom;
                _isUpdatingSelection = false;
                IsProductDropDownOpen = false;
            }
        }

        private void RefreshFilteredProducts()
        {
            if (_isUpdatingSelection) return;

            var search = ProductSearchText?.ToLower() ?? "";
            
            // If the search matches exactly the selected product, don't re-filter
            if (NewSelectedProduit != null && NewSelectedProduit.Nom.ToLower() == search)
                return;

            var matches = AvailableProduits
                .Where(p => string.IsNullOrEmpty(search) || p.Nom.ToLower().Contains(search) || (p.CodeBarre?.Contains(search) ?? false))
                .Take(20)
                .ToList();

            // Avoid clearing if the results are already exactly the same
            if (FilteredProduits.Count == matches.Count && FilteredProduits.All(p => matches.Contains(p)))
                return;

            FilteredProduits.Clear();
            foreach (var m in matches) FilteredProduits.Add(m);
        }

        public PromotionsViewModel(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            Promotions = new ObservableCollection<Promotion>();
            AvailableProduits = new ObservableCollection<Produit>();
            AvailableCategories = new ObservableCollection<string>();
            LoadDataAsync();
        }

        [RelayCommand]
        public async Task LoadDataAsync()
        {
            await LoadPromotionsAsync();
            await LoadProduitsAndCategoriesAsync();
        }

        private async Task LoadProduitsAndCategoriesAsync()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var prods = await context.Produits.Where(p => p.Actif).ToListAsync();
                AvailableProduits.Clear();
                foreach (var p in prods) AvailableProduits.Add(p);
                RefreshFilteredProducts();

                var cats = await context.Produits.Where(p => p.Actif && p.Categorie != null)
                                        .Select(p => p.Categorie)
                                        .Distinct()
                                        .ToListAsync();
                AvailableCategories.Clear();
                AvailableCategories.Add("Toutes");
                foreach (var c in cats.OrderBy(c => c)) AvailableCategories.Add(c!);
            }
            catch { }
        }

        [RelayCommand]
        public async Task LoadPromotionsAsync()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var list = await context.Promotions.ToListAsync();
                Promotions.Clear();
                foreach (var p in list)
                {
                    Promotions.Add(p);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des promotions : {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task AddPromotionAsync()
        {
            if (string.IsNullOrWhiteSpace(NewNom)) return;
            if (NewTypePromotion == "prix_degressif" && !NewTiers.Any())
            {
                MessageBox.Show("Veuillez ajouter au moins un tiers pour le prix dégressif.");
                return;
            }

            if (NewTypePromotion == "offre_combine" && !CurrentBundleItems.Any())
            {
                MessageBox.Show("Veuillez ajouter au moins un article pour l'offre combinée.");
                return;
            }

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                Promotion? promo;

                if (IsEditing)
                {
                    promo = await context.Promotions
                        .Include(p => p.Tiers)
                        .Include(p => p.BundleItems)
                        .FirstOrDefaultAsync(p => p.Id == _editingPromotionId);
                    if (promo == null) return;
                }
                else
                {
                    promo = new Promotion();
                    context.Promotions.Add(promo);
                }

                promo.Nom = NewNom;
                promo.TypePromotion = NewTypePromotion;
                promo.Valeur = NewValeur;
                promo.IsPourcentage = NewIsPourcentage;
                promo.DateDebut = NewDateDebut;
                promo.DateFin = NewDateFin;
                promo.ProduitId = NewSelectedProduit?.Id;
                promo.Categorie = NewSelectedCategorie == "Toutes" ? null : NewSelectedCategorie;
                promo.SeuilQuantite = NewSeuilQuantite;
                promo.QuantiteOfferte = NewQuantiteOfferte;
                promo.IemeArticle = NewIemeArticle;
                promo.RemiseSurIeme = NewRemiseSurIeme;
                promo.SeuilPanier = NewSeuilPanier;
                promo.Actif = true;

                if (NewTypePromotion == "prix_degressif")
                {
                    promo.Tiers.Clear();
                    foreach (var tier in NewTiers)
                    {
                        promo.Tiers.Add(new PromotionTier 
                        { 
                            QuantiteMin = tier.QuantiteMin, 
                            PrixUnitaire = tier.PrixUnitaire 
                        });
                    }
                }
                else if (NewTypePromotion == "offre_combine")
                {
                    promo.BundleItems.Clear();
                    foreach (var bi in CurrentBundleItems)
                    {
                        promo.BundleItems.Add(new PromotionBundleItem 
                        { 
                            ProduitId = bi.ProduitId, 
                            QuantiteRequise = bi.QuantiteRequise 
                        });
                    }
                }

                await context.SaveChangesAsync();

                if (!IsEditing) Promotions.Add(promo);
                else await LoadPromotionsAsync(); // Refresh list to show changes
                
                ResetForms();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'enregistrement : {ex.Message}");
            }
        }

        private void ResetForms()
        {
            _isUpdatingSelection = true;
            IsEditing = false;
            NewNom = string.Empty;
            NewValeur = 0;
            NewSeuilQuantite = null;
            NewQuantiteOfferte = null;
            NewIemeArticle = null;
            NewRemiseSurIeme = null;
            NewSeuilPanier = null;
            NewTiers.Clear();
            NewSelectedProduit = null;
            ProductSearchText = string.Empty;
            NewSelectedCategorie = null;
            NewIsPourcentage = true;
            CurrentBundleItems.Clear();
            _isUpdatingSelection = false;
        }

        [RelayCommand]
        public void AddBundleItem()
        {
            if (NewSelectedProduit == null) return;
            if (CurrentBundleItems.Any(b => b.ProduitId == NewSelectedProduit.Id)) return;

            CurrentBundleItems.Add(new PromotionBundleItem 
            { 
                ProduitId = NewSelectedProduit.Id, 
                Produit = NewSelectedProduit,
                QuantiteRequise = BundleItemQuantity 
            });
            
            // Clear selection for next item
            _isUpdatingSelection = true;
            NewSelectedProduit = null;
            ProductSearchText = string.Empty;
            _isUpdatingSelection = false;
        }

        [RelayCommand]
        public void RemoveBundleItem(PromotionBundleItem item)
        {
            CurrentBundleItems.Remove(item);
        }

        [RelayCommand]
        public void EditPromotion(Promotion promo)
        {
            if (promo == null) return;
            _isUpdatingSelection = true;
            IsEditing = true;
            _editingPromotionId = promo.Id;
            NewNom = promo.Nom;
            NewTypePromotion = promo.TypePromotion;
            NewValeur = promo.Valeur;
            NewIsPourcentage = promo.IsPourcentage;
            NewDateDebut = promo.DateDebut;
            NewDateFin = promo.DateFin;
            NewSeuilQuantite = promo.SeuilQuantite;
            NewQuantiteOfferte = promo.QuantiteOfferte;
            NewIemeArticle = promo.IemeArticle;
            NewRemiseSurIeme = promo.RemiseSurIeme;
            NewSeuilPanier = promo.SeuilPanier;
            NewSelectedProduit = AvailableProduits.FirstOrDefault(p => p.Id == promo.ProduitId);
            ProductSearchText = NewSelectedProduit?.Nom ?? string.Empty;
            NewSelectedCategorie = promo.Categorie ?? "Toutes";
            
            NewTiers.Clear();
            if (promo.Tiers != null)
            {
                foreach (var t in promo.Tiers) NewTiers.Add(new PromotionTier { QuantiteMin = t.QuantiteMin, PrixUnitaire = t.PrixUnitaire });
            }

            CurrentBundleItems.Clear();
            if (promo.BundleItems != null)
            {
                foreach (var b in promo.BundleItems)
                {
                    b.Produit = AvailableProduits.FirstOrDefault(p => p.Id == b.ProduitId) ?? new Produit { Nom = "Inconnu" };
                    CurrentBundleItems.Add(b);
                }
            }
            _isUpdatingSelection = false;
        }

        [RelayCommand]
        public async Task DeletePromotionAsync(Promotion promo)
        {
            if (promo == null) return;
            if (MessageBox.Show("Supprimer cette promotion ?", "Confirmation", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            try
            {
                using var context = _contextFactory.CreateDbContext();
                context.Entry(promo).State = EntityState.Deleted;
                await context.SaveChangesAsync();
                Promotions.Remove(promo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur de suppression : {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task ToggleStatusAsync(Promotion promo)
        {
             if (promo == null) return;
             try
             {
                 using var context = _contextFactory.CreateDbContext();
                 var existing = await context.Promotions.FindAsync(promo.Id);
                 if (existing != null)
                 {
                     existing.Actif = promo.Actif;
                     await context.SaveChangesAsync();
                 }
             }
             catch(Exception ex)
             {
                 MessageBox.Show($"Erreur de mise à jour : {ex.Message}");
             }
        }
        [RelayCommand]
        public void AddTier()
        {
            NewTiers.Add(new PromotionTier { QuantiteMin = 1, PrixUnitaire = 0 });
        }

        [RelayCommand]
        public void RemoveTier(PromotionTier tier)
        {
            if (tier != null) NewTiers.Remove(tier);
        }
    }
}
