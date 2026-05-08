using System.Windows;

namespace SystemeCaisse.UI.Views
{
    /// <summary>
    /// Fenêtre modale affichée en cas d'échec du paiement TPE.
    /// DialogResult = true → Réessayer le paiement
    /// DialogResult = false → Annuler et revenir à la caisse
    /// </summary>
    public partial class TPEPaymentErrorWindow : Window
    {
        public TPEPaymentErrorWindow(string errorMessage, decimal amount)
        {
            InitializeComponent();
            ErrorMessageText.Text = errorMessage;
            AmountText.Text = amount.ToString("C");
        }

        private void Retry_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
