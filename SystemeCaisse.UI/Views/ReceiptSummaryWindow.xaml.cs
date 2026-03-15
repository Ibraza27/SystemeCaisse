using System.Windows;
using System.Windows.Documents;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.Views
{
    public partial class ReceiptSummaryWindow : Window
    {
        private readonly Vente _vente;
        private readonly Entreprise _entreprise;
        private readonly PrintService _printService;
        private readonly bool _isTraining;

        public ReceiptSummaryWindow(Vente vente, Entreprise entreprise, decimal change, bool isTraining)
        {
            InitializeComponent();
            _vente = vente;
            _entreprise = entreprise;
            _printService = new PrintService();
            _isTraining = isTraining;

            ChangeText.Text = $"{change:N2} €";
            
            // Load Preview
            var doc = _printService.GenerateTicketDocument(vente, entreprise, isTraining);
            DocReader.Document = doc;
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            _printService.PrintTicket(_vente, _entreprise, _isTraining);
            this.Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Document XPS (*.xps)|*.xps",
                FileName = $"Ticket_{_vente.NumeroTicket}.xps"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var xpsDoc = new System.Windows.Xps.Packaging.XpsDocument(sfd.FileName, System.IO.FileAccess.Write);
                    var writer = System.Windows.Xps.Packaging.XpsDocument.CreateXpsDocumentWriter(xpsDoc);
                    writer.Write(((IDocumentPaginatorSource)DocReader.Document).DocumentPaginator);
                    xpsDoc.Close();
                    MessageBox.Show("Ticket enregistré avec succès !");
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Erreur lors de l'enregistrement : {ex.Message}");
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
