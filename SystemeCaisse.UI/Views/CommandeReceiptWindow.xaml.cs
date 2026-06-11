using System.Windows;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.Views
{
    public partial class CommandeReceiptWindow : Window
    {
        private readonly Commande _commande;
        private readonly Entreprise _entreprise;
        private readonly PrintService _printService;

        public CommandeReceiptWindow(Commande commande, Entreprise entreprise, PrintService printService)
        {
            InitializeComponent();
            _commande = commande;
            _entreprise = entreprise;
            _printService = printService;

            double width = _printService.GetTicketWidth();
            var doc = _printService.GenerateCommandeTicketDocument(commande, entreprise, width);
            doc.PageWidth = width;
            DocReader.Document = doc;
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            _printService.PrintCommandeTicket(_commande, _entreprise);
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
