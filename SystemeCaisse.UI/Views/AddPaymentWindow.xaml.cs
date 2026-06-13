using System.Windows;

namespace SystemeCaisse.UI.Views
{
    public partial class AddPaymentWindow : Window
    {
        public decimal MontantAjoute { get; private set; }
        public string ModePaiement { get; private set; } = "espece";
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
            tbMontant.Text = Math.Round(_restant, 2).ToString("N2");
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(tbMontant.Text.Replace(",", "."), 
                System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, out decimal montant) && montant > 0)
            {
                montant = Math.Round(montant, 2);
                if (montant > Math.Round(_restant, 2))
                {
                    MessageBox.Show(this, $"Le montant ne peut pas dépasser le restant ({_restant:C}).", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                MontantAjoute = montant;
                ModePaiement = rbVirement.IsChecked == true ? "virement" 
                    : rbWero.IsChecked == true ? "wero" 
                    : rbCB.IsChecked == true ? "cb" 
                    : rbEnLigne.IsChecked == true ? "en_ligne" 
                    : "espece";
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
