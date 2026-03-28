using System.Windows;

namespace SystemeCaisse.UI.Views
{
    public partial class ProductNotFoundWindow : System.Windows.Window
    {
        public bool AddRequested { get; private set; }
        public string ScannedCode { get; }

        public ProductNotFoundWindow(string scannedCode)
        {
            InitializeComponent();
            ScannedCode = scannedCode;
            TxtCode.Text = ScannedCode;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            AddRequested = true;
            DialogResult = true;
        }
    }
}
