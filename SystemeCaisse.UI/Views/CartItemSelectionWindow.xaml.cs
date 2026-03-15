using System.Collections.Generic;
using System.Windows;
using SystemeCaisse.UI.ViewModels;

namespace SystemeCaisse.UI.Views
{
    public partial class CartItemSelectionWindow : Window
    {
        public CartItemViewModel SelectedItem { get; private set; }

        public CartItemSelectionWindow(IEnumerable<CartItemViewModel> items)
        {
            InitializeComponent();
            ItemsList.ItemsSource = items;
        }

        private void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            SelectedItem = ItemsList.SelectedItem as CartItemViewModel;
            if (SelectedItem == null)
            {
                MessageBox.Show("Veuillez sélectionner un article.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }
    }
}
