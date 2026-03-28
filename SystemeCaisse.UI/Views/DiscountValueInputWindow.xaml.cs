using System.Windows;

namespace SystemeCaisse.UI.Views
{
    public partial class DiscountValueInputWindow : System.Windows.Window
    {
        public decimal DiscountValue { get; private set; }

        public DiscountValueInputWindow(string title, string unit)
        {
            InitializeComponent();
            TitleTxt.Text = title;
            UnitTxt.Text = unit;
        }

        private void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(ValueInput.Text.Replace(".", ","), out decimal val))
            {
                DiscountValue = val;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Valeur non valide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
