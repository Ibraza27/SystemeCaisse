using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using SystemeCaisse.Core.Entities;

namespace SystemeCaisse.UI.Views
{
    public partial class QuickAddProductWindow : Window
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

            if (!decimal.TryParse(TxtPrix.Text.Replace('.', ','), out decimal prix))
            {
                MessageBox.Show("Prix de vente invalide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            decimal.TryParse(TxtStock.Text.Replace('.', ','), out decimal stock);

            NewProduct = new Produit
            {
                Nom = TxtNom.Text.Trim(),
                CodeBarre = TxtBarcode.Text.Trim(),
                TypeVente = (CbType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "unite",
                PrixVente = prix,
                Categorie = CbCategory.Text.Trim(),
                StockActuel = stock,
                Actif = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            DialogResult = true;
            Close();
        }
    }
}
