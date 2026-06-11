using System.Windows;

namespace SystemeCaisse.UI.Views
{
    public partial class AddPaymentWindow : Window
    {
        public decimal MontantAjoute { get; private set; }
        private readonly decimal _restant;

        public AddPaymentWindow(decimal restant)
        {
            InitializeComponent();
            _restant = restant;
            tbRestant.Text = $"{restant:C}";
            tbMontant.Text = "0.00";
            tbMontant.Focus();
            tbMontant.SelectAll();
        }

        private void FillExact_Click(object sender, RoutedEventArgs e)
        {
            tbMontant.Text = _restant.ToString("N2");
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(tbMontant.Text.Replace(",", "."), 
                System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, out decimal montant) && montant > 0)
            {
                if (montant > _restant)
                {
                    MessageBox.Show(this, $"Le montant ne peut pas dépasser le restant ({_restant:C}).", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                MontantAjoute = montant;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(this, "Veuillez saisir un montant valide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
