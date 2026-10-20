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
        public string Unite { get; set; } = "pce";
        public string QuantiteDisplay => QuantiteTotale % 1 == 0
            ? $"{QuantiteTotale:0} {Unite}"
            : $"{QuantiteTotale:0.###} {Unite}";
    }

    public partial class CommandeRecapWindow : Window
    {
        private readonly List<RecapLine> _lines;
        private readonly PrintService _printService;
        private readonly Entreprise _entreprise;
        private readonly int _nbCommandes;

        public CommandeRecapWindow(List<Commande> commandes, PrintService printService, Entreprise entreprise, IEnumerable<Produit>? catalog = null)
        {
            InitializeComponent();
            _printService = printService;
            _entreprise = entreprise;
            _nbCommandes = commandes.Count;

            var weightProductIds = new HashSet<int>();
            var weightProductNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (catalog != null)
            {
                foreach (var p in catalog)
                {
                    if (string.Equals(p.TypeVente?.Trim(), "poids", StringComparison.OrdinalIgnoreCase))
                    {
                        weightProductIds.Add(p.Id);
                        if (!string.IsNullOrWhiteSpace(p.Nom))
                            weightProductNames.Add(p.Nom.Trim());
                    }
                }
            }

            // Aggregate all products
            _lines = commandes
                .Where(c => c.Lignes != null)
                .SelectMany(c => c.Lignes)
                .Where(l => !string.IsNullOrWhiteSpace(l.ProduitNom))
                .GroupBy(l => l.ProduitNom!.Trim())
                .Select(g =>
                {
                    bool isKg = g.Any(l =>
                        string.Equals(l.Unite, "kg", StringComparison.OrdinalIgnoreCase) ||
                        (l.ProduitId.HasValue && weightProductIds.Contains(l.ProduitId.Value)) ||
                        weightProductNames.Contains((l.ProduitNom ?? "").Trim()) ||
                        l.Quantite % 1 != 0);

                    return new RecapLine
                    {
                        ProduitNom = g.Key,
                        QuantiteTotale = g.Sum(l => l.Quantite),
                        Unite = isKg ? "kg" : "pce"
                    };
                })
                .OrderBy(l => l.ProduitNom)
                .ToList();

            dgRecap.ItemsSource = _lines;
            tbSubtitle.Text = $"{_nbCommandes} commande(s) — {_lines.Count} produit(s) différent(s)";
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
            var bwLogo = PrintService.LoadBlackAndWhiteLogo(_entreprise.LogoPath);
            if (bwLogo != null)
            {
                var image = new Image { Source = bwLogo, Width = 80, Height = 80, Stretch = Stretch.Uniform };
                headerPara.Inlines.Add(new InlineUIContainer(image));
                headerPara.Inlines.Add(new LineBreak());
            }
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

                var lineGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameText = new TextBlock
                {
                    Text = line.ProduitNom,
                    FontSize = 11,
                    MaxWidth = Math.Max(120, width - 115),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(nameText, 0);
                lineGrid.Children.Add(nameText);

                var dotsText = new TextBlock
                {
                    Text = new string('.', 120),
                    FontSize = 10,
                    Foreground = Brushes.DimGray,
                    Margin = new Thickness(3, 0, 3, 1),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    TextWrapping = TextWrapping.NoWrap,
                    ClipToBounds = true
                };
                Grid.SetColumn(dotsText, 1);
                lineGrid.Children.Add(dotsText);

                var qtyText = new TextBlock
                {
                    Text = line.QuantiteDisplay,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(qtyText, 2);
                lineGrid.Children.Add(qtyText);

                cell.Blocks.Add(new BlockUIContainer(lineGrid));
                row.Cells.Add(cell);
                rowGroup.Rows.Add(row);
            }

            table.RowGroups.Add(rowGroup);
            doc.Blocks.Add(table);

            doc.Blocks.Add(new Paragraph(new Run(new string('-', 35))) { Margin = new Thickness(0, 5, 0, 5) });

            // Total
            var totalPara = new Paragraph { TextAlignment = TextAlignment.Center };
            int nbPce = (int)_lines.Where(l => l.Unite == "pce").Sum(l => l.QuantiteTotale);
            decimal totalKg = _lines.Where(l => l.Unite == "kg").Sum(l => l.QuantiteTotale);
            string summaryStr = totalKg > 0
                ? $"Total : {nbPce} pce | {totalKg:0.###} kg"
                : $"Total : {nbPce} pce";
            totalPara.Inlines.Add(new Bold(new Run(summaryStr)));
            doc.Blocks.Add(totalPara);

            return doc;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
