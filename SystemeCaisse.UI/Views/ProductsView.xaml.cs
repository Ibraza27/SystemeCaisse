using System.Windows.Controls;
using System.Windows.Input;

namespace SystemeCaisse.UI.Views
{
    public partial class ProductsView : System.Windows.Controls.UserControl
    {
        public ProductsView()
        {
            InitializeComponent();
        }

        private void PriceTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Intercept Dot (from numeric pad or main keyboard) and replace it with Comma
            if (e.Key == Key.Decimal || e.Key == Key.OemPeriod)
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

        private void PriceTextBox_GotFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                textBox.SelectAll();
            }
        }
    }
}
