using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using SystemeCaisse.Core.Entities;

namespace SystemeCaisse.UI.Views
{
    public partial class QuickAddProductWindow : System.Windows.Window
    {
        public Produit NewProduct { get; private set; }

        public QuickAddProductWindow(string barcode, IEnumerable<string> categories)
        {
            InitializeComponent();
            TxtBarcode.Text = barcode;
            CbCategory.ItemsSource = categories;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNom.Text))
            {
                MessageBox.Show("Le nom du produit est obligatoire.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string rawPrix = TxtPrix.Text.Replace('.', ',');
            if (!decimal.TryParse(rawPrix, out decimal prix))
            {
                MessageBox.Show("Prix de vente invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            decimal.TryParse(TxtStock.Text.Replace('.', ','), out decimal stock);
            
            int taxTier = 1;
            if (CbTaxTier.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int val))
            {
                taxTier = val;
            }

            NewProduct = new Produit
            {
                Nom = TxtNom.Text.Trim(),
                CodeBarre = TxtBarcode.Text.Trim(),
                TypeVente = (CbType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "unite",
                PrixVente = prix,
                Categorie = CbCategory.Text.Trim(),
                StockActuel = stock,
                TaxTier = taxTier,
                Actif = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            DialogResult = true;
            Close();
        }

        private void PriceTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Decimal || e.Key == System.Windows.Input.Key.OemPeriod)
            {
                var textBox = sender as TextBox;
                if (textBox != null)
                {
                    e.Handled = true;
                    int selectionStart = textBox.SelectionStart;
                    textBox.Text = textBox.Text.Insert(selectionStart, ",");
                    textBox.SelectionStart = selectionStart + 1;
                }
            }
        }

        private void PriceTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            (sender as TextBox)?.SelectAll();
        }
    }
}
