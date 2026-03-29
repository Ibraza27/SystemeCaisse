using System.Windows;

namespace SystemeCaisse.UI.Views
{
    public partial class QuantityInputWindow : System.Windows.Window
    {
        public decimal Quantity { get; private set; }

        public QuantityInputWindow(decimal currentQty)
        {
            InitializeComponent();
            QtyInput.Text = currentQty.ToString("0.###");
            QtyInput.SelectAll();
            QtyInput.Focus();
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(QtyInput.Text.Replace('.', ','), out decimal qty))
            {
                Quantity = qty;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(this, "Quantité invalide", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
