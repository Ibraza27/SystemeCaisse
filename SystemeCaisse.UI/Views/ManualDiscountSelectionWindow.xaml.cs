using System.Windows;

namespace SystemeCaisse.UI.Views
{
    public enum DiscountScope { Basket, Item }
    public enum DiscountType { Percentage, Fixed }

    public partial class ManualDiscountSelectionWindow : Window
    {
        public DiscountScope SelectedScope { get; private set; }
        public DiscountType SelectedType { get; private set; }

        public ManualDiscountSelectionWindow()
        {
            InitializeComponent();
        }

        private void BtnClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == BtnBasketPercent) { SelectedScope = DiscountScope.Basket; SelectedType = DiscountType.Percentage; }
            else if (btn == BtnBasketFixed) { SelectedScope = DiscountScope.Basket; SelectedType = DiscountType.Fixed; }
            else if (btn == BtnItemPercent) { SelectedScope = DiscountScope.Item; SelectedType = DiscountType.Percentage; }
            else if (btn == BtnItemFixed) { SelectedScope = DiscountScope.Item; SelectedType = DiscountType.Fixed; }

            DialogResult = true;
            Close();
        }
    }
}
