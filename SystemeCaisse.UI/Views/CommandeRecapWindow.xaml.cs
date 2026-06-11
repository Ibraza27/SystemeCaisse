using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.Views
{
    public class RecapLine
    {
        public string ProduitNom { get; set; } = string.Empty;
        public decimal QuantiteTotale { get; set; }
    }

    public partial class CommandeRecapWindow : Window
    {
        private readonly List<RecapLine> _lines;
        private readonly PrintService _printService;
        private readonly Entreprise _entreprise;
        private readonly int _nbCommandes;

        public CommandeRecapWindow(List<Commande> commandes, PrintService printService, Entreprise entreprise)
        {
            InitializeComponent();
            _printService = printService;
            _entreprise = entreprise;
            _nbCommandes = commandes.Count;

            // Aggregate all products
            _lines = commandes
                .SelectMany(c => c.Lignes)
                .GroupBy(l => l.ProduitNom)
                .Select(g => new RecapLine
                {
                    ProduitNom = g.Key,
                    QuantiteTotale = g.Sum(l => l.Quantite)
                })
                .OrderBy(l => l.ProduitNom)
                .ToList();

            dgRecap.ItemsSource = _lines;
            tbSubtitle.Text = $"{_nbCommandes} commande(s) — {_lines.Count} produit(s) différent(s) — {_lines.Sum(l => l.QuantiteTotale):N0} articles au total";
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double width = _printService.GetTicketWidth();
                var doc = GenerateRecapDocument(width);
                var pd = new PrintDialog();
                doc.PageWidth = width;
                doc.PageHeight = double.NaN;
                IDocumentPaginatorSource idp = doc;
                pd.PrintDocument(idp.DocumentPaginator, "Récap Commandes");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Erreur d'impression : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private FlowDocument GenerateRecapDocument(double width)
        {
            var doc = new FlowDocument
            {
                PageWidth = width,
                PagePadding = new Thickness(5),
                FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
                FontSize = 11,
                Background = Brushes.White
            };

            // Header
            var headerPara = new Paragraph { TextAlignment = TextAlignment.Center };
            headerPara.Inlines.Add(new Bold(new Run(_entreprise.Nom ?? "Magasin")) { FontSize = 14 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Bold(new Run("═══ RÉCAPITULATIF ═══")) { FontSize = 14 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run($"{_nbCommandes} commande(s)"));
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run(DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)));
            headerPara.Inlines.Add(new LineBreak());
            doc.Blocks.Add(headerPara);

            doc.Blocks.Add(new Paragraph(new Run(new string('-', 35))) { Margin = new Thickness(0, 5, 0, 5) });

            // Products table
            var table = new Table { CellSpacing = 0 };
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            var rowGroup = new TableRowGroup();

            // Header row
            var headerRow = new TableRow();
            var headerCell = new TableCell();
            var headerGrid = new DockPanel();
            var hProduit = new TextBlock { Text = "PRODUIT", FontWeight = FontWeights.Bold, FontSize = 11 };
            DockPanel.SetDock(hProduit, Dock.Left);
            headerGrid.Children.Add(hProduit);
            var hQte = new TextBlock { Text = "QTÉ", FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(hQte, Dock.Right);
            headerGrid.Children.Add(hQte);
            headerCell.Blocks.Add(new BlockUIContainer(headerGrid));
            headerRow.Cells.Add(headerCell);
            rowGroup.Rows.Add(headerRow);

            foreach (var line in _lines)
            {
                var row = new TableRow();
                var cell = new TableCell();
                var panel = new DockPanel();
                var nameText = new TextBlock { Text = line.ProduitNom, FontSize = 11 };
                DockPanel.SetDock(nameText, Dock.Left);
                panel.Children.Add(nameText);
                string qtyStr = line.QuantiteTotale % 1 == 0 ? $"{line.QuantiteTotale:0}" : $"{line.QuantiteTotale:0.000}";
                var qtyText = new TextBlock { Text = qtyStr, FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
                DockPanel.SetDock(qtyText, Dock.Right);
                panel.Children.Add(qtyText);
                cell.Blocks.Add(new BlockUIContainer(panel));
                row.Cells.Add(cell);
                rowGroup.Rows.Add(row);
            }

            table.RowGroups.Add(rowGroup);
            doc.Blocks.Add(table);

            doc.Blocks.Add(new Paragraph(new Run(new string('-', 35))) { Margin = new Thickness(0, 5, 0, 5) });

            // Total
            var totalPara = new Paragraph { TextAlignment = TextAlignment.Center };
            totalPara.Inlines.Add(new Bold(new Run($"Total : {_lines.Sum(l => l.QuantiteTotale):N0} articles")));
            doc.Blocks.Add(totalPara);

            return doc;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
