using System.Windows.Controls;

namespace SystemeCaisse.UI.Views
{
    public partial class CommandesView : System.Windows.Controls.UserControl
    {
        public CommandesView()
        {
            InitializeComponent();
        }

        // Same anti-autoselect fix as SalesView
        private void searchComboCmd_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                var textBox = comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox;
                if (textBox != null)
                {
                    textBox.SelectionChanged += (s, ev) =>
                    {
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
