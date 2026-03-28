using System.Windows.Controls;

namespace SystemeCaisse.UI.Views
{
    public partial class SalesView : System.Windows.Controls.UserControl
    {
        public SalesView()
        {
            InitializeComponent();
        }

        private void searchCombo_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                var textBox = comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox;
                if (textBox != null)
                {
                    textBox.SelectionChanged += (s, ev) => 
                    {
                        // If selection length > 0 and it's just the first character being auto-selected
                        if (textBox.SelectionLength > 0 && textBox.SelectionStart == 0 && textBox.Text.Length == 1)
                        {
                            textBox.SelectionLength = 0;
                            textBox.CaretIndex = 1;
                        }
                    };
                }
            }
        }
    }
}
