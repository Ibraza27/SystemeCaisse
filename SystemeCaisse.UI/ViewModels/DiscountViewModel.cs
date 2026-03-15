using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SystemeCaisse.UI.ViewModels
{
    public partial class DiscountViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _applyToCart = true;

        [ObservableProperty]
        private bool _applyToItem = false;

        [ObservableProperty]
        private bool _isPercentage = true;

        [ObservableProperty]
        private bool _isAmount = false;

        [ObservableProperty]
        private decimal _value;

        [ObservableProperty]
        private CartItemViewModel _selectedItem;

        public ObservableCollection<CartItemViewModel> AvailableItems { get; }

        public DiscountViewModel(IEnumerable<CartItemViewModel> items, CartItemViewModel preselectedItem = null)
        {
            AvailableItems = new ObservableCollection<CartItemViewModel>(items);
            if (preselectedItem != null)
            {
                SelectedItem = AvailableItems.FirstOrDefault(i => i.ProduitId == preselectedItem.ProduitId);
                ApplyToItem = true;
                ApplyToCart = false;
            }
            else if (AvailableItems.Any())
            {
                SelectedItem = AvailableItems.First();
            }
            else
            {
                // If cart is empty, we must apply to cart (though button should probably be disabled)
                ApplyToCart = true;
                ApplyToItem = false;
            }
        }

        partial void OnApplyToCartChanged(bool value)
        {
            if (value) ApplyToItem = false;
        }

        partial void OnApplyToItemChanged(bool value)
        {
            if (value) ApplyToCart = false;
        }

        partial void OnIsPercentageChanged(bool value)
        {
            if (value) IsAmount = false;
        }

        partial void OnIsAmountChanged(bool value)
        {
            if (value) IsPercentage = false;
        }

        public bool? DialogResult { get; set; }

        [RelayCommand]
        private void Apply()
        {
            DialogResult = true;
            CloseAction?.Invoke();
        }

        [RelayCommand]
        private void Cancel()
        {
            DialogResult = false;
            CloseAction?.Invoke();
        }

        public System.Action CloseAction { get; set; }
    }
}
