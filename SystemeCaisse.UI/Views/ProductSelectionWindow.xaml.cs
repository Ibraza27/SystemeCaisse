using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SystemeCaisse.Core.Entities;

namespace SystemeCaisse.UI.Views
{
    public partial class ProductSelectionWindow : Window
    {
        public Produit? SelectedProduct { get; private set; }

        public ProductSelectionWindow(IEnumerable<Produit> products)
        {
            InitializeComponent();
            ProductGrid.ItemsSource = products.ToList();
        }

        private void BtnValider_Click(object sender, RoutedEventArgs e)
        {
            SelectedProduct = ProductGrid.SelectedItem as Produit;
            if (SelectedProduct != null)
            {
                DialogResult = true;
                Close();
            }
        }

        private void ProductGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            BtnValider_Click(sender, e);
        }
    }
}
