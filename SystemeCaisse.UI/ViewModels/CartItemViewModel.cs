using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SystemeCaisse.Core.Entities;

namespace SystemeCaisse.UI.ViewModels
{
    public class CartItemViewModel : INotifyPropertyChanged
    {
        private readonly LigneVente _ligneVente;

        public ICommand IncreaseQuantityCommand { get; }
        public ICommand DecreaseQuantityCommand { get; }

        public CartItemViewModel(LigneVente ligneVente)
        {
            _ligneVente = ligneVente;
            IncreaseQuantityCommand = new BasicRelayCommand(_ => Quantite++);
            DecreaseQuantityCommand = new BasicRelayCommand(_ => { if (Quantite > 1) Quantite--; });
        }

        public Produit Produit => _ligneVente.Produit ?? new Produit();

        public string ProduitNom => _ligneVente.ProduitNom;
        public decimal PrixUnitaire => _ligneVente.PrixUnitaire;
        public int? ProduitId => _ligneVente.ProduitId;
        public int TaxTier => _ligneVente.TaxTier;

        public decimal TotalLigneStandard => _ligneVente.PrixUnitaire * _ligneVente.Quantite;

        public decimal Quantite
        {
            get => _ligneVente.Quantite;
            set
            {
                if (_ligneVente.Quantite != value)
                {
                    _ligneVente.Quantite = value;
                    // Recalculate price override discount when quantity changes
                    if (_priceOverridePerUnit > 0)
                    {
                        _remiseManuelleFixed = _priceOverridePerUnit * value;
                        OnPropertyChanged(nameof(RemiseManuelleFixed));
                        OnPropertyChanged(nameof(RemiseManuelle));
                        OnPropertyChanged(nameof(RemiseTotale));
                    }
                    SyncEntity();
                    OnPropertyChanged(nameof(Quantite));
                    OnPropertyChanged(nameof(TotalLigneStandard));
                    OnPropertyChanged(nameof(TotalLigne));
                }
            }
        }

        private decimal _remiseAuto;
        public decimal RemiseAuto
        {
            get => _remiseAuto;
            set { _remiseAuto = value; SyncEntity(); OnPropertyChanged(); OnPropertyChanged(nameof(RemiseTotale)); OnPropertyChanged(nameof(TotalLigne)); OnPropertyChanged(nameof(HasPromotion)); }
        }

        private decimal _remiseManuellePercent;
        public decimal RemiseManuellePercent
        {
            get => _remiseManuellePercent;
            set { _remiseManuellePercent = value; SyncEntity(); OnPropertyChanged(); OnPropertyChanged(nameof(RemiseTotale)); OnPropertyChanged(nameof(RemiseManuelle)); OnPropertyChanged(nameof(TotalLigne)); OnPropertyChanged(nameof(HasPromotion)); }
        }

        private decimal _remiseManuelleFixed;
        public decimal RemiseManuelleFixed
        {
            get => _remiseManuelleFixed;
            set { _remiseManuelleFixed = value; SyncEntity(); OnPropertyChanged(); OnPropertyChanged(nameof(RemiseTotale)); OnPropertyChanged(nameof(RemiseManuelle)); OnPropertyChanged(nameof(TotalLigne)); OnPropertyChanged(nameof(HasPromotion)); }
        }

        /// <summary>
        /// Per-unit price override discount. When > 0, RemiseManuelleFixed is recalculated
        /// as PriceOverridePerUnit * Quantite whenever quantity changes.
        /// This allows overriding the sale price in the current cart without affecting the product's base price.
        /// </summary>
        private decimal _priceOverridePerUnit;
        public decimal PriceOverridePerUnit
        {
            get => _priceOverridePerUnit;
            set { _priceOverridePerUnit = value; OnPropertyChanged(); }
        }

        public decimal RemiseManuelle => (TotalLigneStandard * (_remiseManuellePercent / 100)) + _remiseManuelleFixed;
        
        public decimal RemiseTotale => RemiseAuto + RemiseManuelle;

        public decimal TotalLigne => TotalLigneStandard - RemiseTotale;

        public string? PromotionAppliquee
        {
            get => _ligneVente.PromotionAppliquee;
            set { _ligneVente.PromotionAppliquee = value; OnPropertyChanged(); }
        }

        public bool HasPromotion => RemiseAuto > 0 || RemiseManuelle > 0;

        private void SyncEntity()
        {
            _ligneVente.Remise = RemiseAuto + RemiseManuelle;
            _ligneVente.TotalLigne = TotalLigne;
        }

        public LigneVente ToEntity()
        {
            SyncEntity();
            return _ligneVente;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
