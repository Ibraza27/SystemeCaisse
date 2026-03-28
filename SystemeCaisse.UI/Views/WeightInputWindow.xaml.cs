using System.Windows;
using SystemeCaisse.Core.Entities;

namespace SystemeCaisse.UI.Views
{
    public partial class WeightInputWindow : System.Windows.Window
    {
        public decimal PoidsSaisi { get; private set; }

        public WeightInputWindow(Produit? produit = null)
        {
            InitializeComponent();
            if (produit != null)
            {
                ProductNameTxt.Text = produit.Nom;
                ProductPriceTxt.Text = $"{produit.PrixVente:N2} €/kg";
            }
            WeightInput.Focus();
        }

        private void BtnValider_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(WeightInput.Text.Replace('.', ','), out decimal poids))
            {
                PoidsSaisi = poids;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Veuillez saisir un poids valide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
