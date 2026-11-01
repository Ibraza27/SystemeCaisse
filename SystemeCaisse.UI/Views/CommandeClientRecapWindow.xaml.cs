using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.Views
{
    public class ClientRecapLine
    {
        public string ClientDisplay { get; set; } = string.Empty;
        public string Telephone { get; set; } = string.Empty;
        public string Ville { get; set; } = string.Empty;
        public int NbCommandes { get; set; }
        public decimal Total { get; set; }
        public decimal Paye { get; set; }
        public decimal Restant { get; set; }
        public bool IsRegle => Restant <= 0;
    }

    public partial class CommandeClientRecapWindow : Window
    {
        private readonly List<ClientRecapLine> _lines;
        private readonly PrintService _printService;
        private readonly Entreprise _entreprise;
        private readonly int _nbCommandes;

        public CommandeClientRecapWindow(List<Commande> commandes, PrintService printService, Entreprise entreprise)
        {
            InitializeComponent();
            _printService = printService;
            _entreprise = entreprise;
            _nbCommandes = commandes.Count;

            // Aggregate by client (Nom + Prénom + Téléphone), keep Ville for grouping
            _lines = commandes
                .GroupBy(c => new { Nom = (c.Nom ?? "").ToUpper(), Prenom = (c.Prenom ?? "").ToUpper(), c.Telephone })
                .Select(g =>
                {
                    // Use the most common Ville for this client
                    string ville = g.Select(c => c.Ville ?? "Autre")
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .GroupBy(v => v.ToUpperInvariant())
                        .OrderByDescending(vg => vg.Count())
                        .Select(vg => vg.First())
                        .FirstOrDefault() ?? "Autre";

                    return new ClientRecapLine
                    {
                        ClientDisplay = $"{g.Key.Prenom} {g.Key.Nom}",
                        Telephone = g.Key.Telephone,
                        Ville = ville,
                        NbCommandes = g.Count(),
                        Total = g.Sum(c => c.TotalAvecLivraison),
                        Paye = g.Sum(c => c.MontantPaye),
                        Restant = Math.Round(g.Sum(c => c.TotalAvecLivraison) - g.Sum(c => c.MontantPaye), 2)
                    };
                })
                .OrderBy(l => l.Ville)
                .ThenBy(l => l.ClientDisplay)
                .ToList();

            // Apply CollectionView grouping by Ville
            var view = CollectionViewSource.GetDefaultView(_lines);
            view.GroupDescriptions.Add(new PropertyGroupDescription("Ville"));
            dgRecap.ItemsSource = view;

            int nbVilles = _lines.Select(l => l.Ville.ToUpperInvariant()).Distinct().Count();
            tbSubtitle.Text = $"{_nbCommandes} commande(s) — {_lines.Count} client(s) — {nbVilles} ville(s) — Total : {_lines.Sum(l => l.Total):C}";
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double width = _printService.GetTicketWidth();
                var doc = GenerateRecapDocument(width);
                _printService.PrintFlowDocument(doc, "Récap Clients");
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
                ColumnWidth = width,
                PagePadding = new Thickness(4, 4, 8, 4),
                FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
                FontSize = 10.5,
                Background = Brushes.White
            };

            // Header
            var headerPara = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            var bwLogo = PrintService.LoadBlackAndWhiteLogo(_entreprise.LogoPath);
            if (bwLogo != null)
            {
                var image = new Image
                {
                    Source = bwLogo,
                    Width = 75,
                    Height = 75,
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                headerPara.Inlines.Add(new InlineUIContainer(image));
                headerPara.Inlines.Add(new LineBreak());
            }
            headerPara.Inlines.Add(new Bold(new Run(_entreprise.Nom ?? "Magasin")) { FontSize = 13 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Bold(new Run("══ RÉCAP CLIENTS ══")) { FontSize = 12.5 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run($"{_nbCommandes} commande(s) — {_lines.Count} client(s)"));
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run(DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)));
            doc.Blocks.Add(headerPara);

            doc.Blocks.Add(PrintService.CreateSeparatorBlock());

            // Group by Ville and print
            var villeGroups = _lines.GroupBy(l => l.Ville).OrderBy(g => g.Key);
            foreach (var villeGroup in villeGroups)
            {
                // Ville header
                var villePara = new Paragraph { Margin = new Thickness(0, 6, 0, 2) };
                villePara.Inlines.Add(new Bold(new Run($"📍 {villeGroup.Key.ToUpper()}")) { FontSize = 11.5 });
                doc.Blocks.Add(villePara);

                doc.Blocks.Add(PrintService.CreateSeparatorBlock());

                foreach (var line in villeGroup)
                {
                    var clientPara = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                    clientPara.Inlines.Add(new Bold(new Run(line.ClientDisplay)));
                    clientPara.Inlines.Add(new LineBreak());
                    clientPara.Inlines.Add(new Run($"Tél: {line.Telephone}") { FontSize = 10 });
                    clientPara.Inlines.Add(new LineBreak());
                    clientPara.Inlines.Add(new Run($"{line.NbCommandes} cmd | Total: {line.Total:0.00}€ | Payé: {line.Paye:0.00}€") { FontSize = 10 });
                    if (line.Restant > 0)
                    {
                        clientPara.Inlines.Add(new LineBreak());
                        clientPara.Inlines.Add(new Bold(new Run($"RESTANT: {line.Restant:0.00}€")) { FontSize = 10 });
                    }
                    doc.Blocks.Add(clientPara);
                }
            }

            doc.Blocks.Add(PrintService.CreateSeparatorBlock());

            // Total
            var totalPara = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
            totalPara.Inlines.Add(new Bold(new Run($"Total : {_lines.Sum(l => l.Total):C}")));
            totalPara.Inlines.Add(new LineBreak());
            totalPara.Inlines.Add(new Bold(new Run($"Payé : {_lines.Sum(l => l.Paye):C}")));
            decimal totalRestant = _lines.Sum(l => l.Restant);
            if (totalRestant > 0)
            {
                totalPara.Inlines.Add(new LineBreak());
                totalPara.Inlines.Add(new Bold(new Run($"Restant : {totalRestant:C}")));
            }
            doc.Blocks.Add(totalPara);

            return doc;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
