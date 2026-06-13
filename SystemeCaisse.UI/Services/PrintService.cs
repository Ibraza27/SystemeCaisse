using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SystemeCaisse.Core.Entities;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Drawing.Imaging;
using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Infrastructure.Data;
using ColorDrawing = System.Drawing.Color;
using ImageDrawing = System.Drawing.Image;
using ImageWpf = System.Windows.Controls.Image;
using ColorWpf = System.Windows.Media.Color;
using BrushWpf = System.Windows.Media.Brush;
using BrushesWpf = System.Windows.Media.Brushes;
using ZXing;

namespace SystemeCaisse.UI.Services
{
    public class PrintService
    {
        private const double TicketWidth = 300; // ~80mm at 96dpi

        public void PrintTicket(Vente vente, Entreprise entreprise, bool isTrainingMode = false)
        {
            try
            {
                double width = GetTicketWidth();
                var doc = GenerateTicketDocument(vente, entreprise, isTrainingMode, width);
                
                var pd = new PrintDialog();
                doc.PageWidth = width;
                doc.PageHeight = double.NaN;
                
                IDocumentPaginatorSource idp = doc;
                pd.PrintDocument(idp.DocumentPaginator, "Ticket de Caisse");
            }
            catch (Exception ex)
            {
                MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur d'impression : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public double GetTicketWidth()
        {
            try
            {
                // We don't have direct access to the factory here easily without refactoring, 
                // but we can check if there's a global config or just use a default that matches the settings.
                // Since this is a service, let's assume 80mm (300px) as default, but we should try to read from DB if possible.
                // Let's use a simpler approach: the caller can pass it or we can fetch it.
                // For now, let's look for the config in the database manually.
                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "caisse.db");
                if (File.Exists(dbPath))
                {
                    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                    optionsBuilder.UseSqlite($"Data Source={dbPath}");
                    using var context = new AppDbContext(optionsBuilder.Options);
                    var cfg = context.Configuration.FirstOrDefault(c => c.Cle == "ticket_width");
                    if (cfg != null && int.TryParse(cfg.Valeur, out int w))
                    {
                        // Convert mm to pixels (approx 96 DPI) -> 1mm ~ 3.78px
                        // 80mm ~ 302px
                        // 58mm ~ 219px
                        return w * 3.78;
                    }
                }
            }
            catch { }
            return 300; // Default 80mm
        }

        public FlowDocument GenerateTicketDocument(Vente vente, Entreprise entreprise, bool isTrainingMode, double width)
        {
            var doc = new FlowDocument
            {
                PageWidth = width,
                PagePadding = new Thickness(5),
                FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
                FontSize = 11,
                Background = BrushesWpf.White
            };

            // 1. Header (Logo + Enterprise Info)
            var headerPara = new Paragraph { TextAlignment = TextAlignment.Center };
            
            if (isTrainingMode)
            {
                headerPara.Inlines.Add(new Bold(new Run("*** MODE FORMATION ***")) { FontSize = 16 });
                headerPara.Inlines.Add(new LineBreak());
            }
            
            // Logo Resolution Fix
            string? finalLogoPath = null;
            if (!string.IsNullOrEmpty(entreprise.LogoPath))
            {
                if (File.Exists(entreprise.LogoPath))
                {
                    finalLogoPath = entreprise.LogoPath;
                }
                else
                {
                    // Check relative to app startup directory
                    string fileName = Path.GetFileName(entreprise.LogoPath);
                    string relativePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                    if (File.Exists(relativePath))
                    {
                        finalLogoPath = relativePath;
                    }
                }
            }

            if (finalLogoPath != null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(finalLogoPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    var image = new ImageWpf { Source = bitmap, Width = 100, Height = 100, Stretch = Stretch.Uniform };
                    headerPara.Inlines.Add(new InlineUIContainer(image));
                    headerPara.Inlines.Add(new LineBreak());
                }
                catch { }
            }

            headerPara.Inlines.Add(new Bold(new Run(entreprise.Nom ?? "Magasin")) { FontSize = 14 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run(entreprise.Adresse ?? ""));
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run(entreprise.Telephone ?? ""));
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new LineBreak());

            // 2. Dates & Barcode
            // Date logic
            headerPara.Inlines.Add(new Run(vente.CreatedAt.ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture)));
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run(vente.CreatedAt.ToString("dd MMMM yyyy", new CultureInfo("fr-FR"))));
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new LineBreak());

            // Barcode
            try
            {
                var writer = new ZXing.BarcodeWriter<System.Drawing.Bitmap>
                {
                    Format = ZXing.BarcodeFormat.CODE_128,
                    Options = new ZXing.Common.EncodingOptions
                    {
                        Width = 250,
                        Height = 60,
                        Margin = 0
                    },
                    Renderer = new ZXing.Windows.Compatibility.BitmapRenderer()
                };
                
                using (var bitmap = writer.Write(vente.NumeroTicket))
                using (MemoryStream ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    var wpfBitmap = new BitmapImage();
                    wpfBitmap.BeginInit();
                    wpfBitmap.StreamSource = ms;
                    wpfBitmap.CacheOption = BitmapCacheOption.OnLoad;
                    wpfBitmap.EndInit();
                    
                    var barcodeImg = new ImageWpf { Source = wpfBitmap, Width = 250, Height = 60 };
                    headerPara.Inlines.Add(new InlineUIContainer(barcodeImg));
                }
            }
            catch (Exception) 
            { 
               headerPara.Inlines.Add(new Run($"Ticket N°: {vente.NumeroTicket}")); 
               // Console.WriteLine(ex.Message);
            }
            
            headerPara.Inlines.Add(new LineBreak());
            doc.Blocks.Add(headerPara);

            // 3. Products Grouped by Category
            var categoryGroups = vente.Lignes
                .GroupBy(l => l.CategorieNom ?? "DIVERS")
                .OrderBy(g => g.Key);

            foreach (var group in categoryGroups)
            {
                var catPara = new Paragraph(new Bold(new Run($">> {group.Key.ToUpper()}"))) { Margin = new Thickness(0, 10, 0, 5) };
                doc.Blocks.Add(catPara);

                var table = new Table { CellSpacing = 0 };
                table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                var rowGroup = new TableRowGroup();

                foreach (var line in group)
                {
                    // Line 1: Name
                    var row1 = new TableRow();
                    row1.Cells.Add(new TableCell(new Paragraph(new Run(line.ProduitNom)) { Margin = new Thickness(0) }));
                    rowGroup.Rows.Add(row1);

                    // Line 1.5: Promotion Name
                    if (!string.IsNullOrEmpty(line.PromotionAppliquee))
                    {
                        var promoRow = new TableRow();
                        var promoPara = new Paragraph(new Run($"   * {line.PromotionAppliquee}")) 
                        { 
                            FontSize = 9, 
                            FontStyle = FontStyles.Italic, 
                            Foreground = BrushesWpf.DarkGreen,
                            Margin = new Thickness(0) 
                        };
                        promoRow.Cells.Add(new TableCell(promoPara));
                        rowGroup.Rows.Add(promoRow);
                    }

                    // Line 2: Qty x Price and Total TTC
                    var row2 = new TableRow();
                    var detailStack = new DockPanel { LastChildFill = true };
                    
                    string qtyStr = line.Quantite % 1 == 0 ? $"{line.Quantite:0}" : $"{line.Quantite:0.000}";
                    
                    // Show original price if discounted
                    decimal originalTotal = line.PrixUnitaire * line.Quantite;
                    bool isDiscounted = line.Remise > 0;

                    string detailStr = $"   {qtyStr} X {line.PrixUnitaire:0.00}€";
                    
                    var detailText = new TextBlock { Text = detailStr, FontSize = 10, Foreground = BrushesWpf.DimGray };
                    DockPanel.SetDock(detailText, Dock.Left);
                    detailStack.Children.Add(detailText);

                    var priceStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                    if (isDiscounted)
                    {
                        priceStack.Children.Add(new TextBlock { 
                            Text = $"{originalTotal:0.00} ", 
                            FontSize = 9, 
                            Foreground = BrushesWpf.Gray, 
                            TextDecorations = TextDecorations.Strikethrough,
                            VerticalAlignment = VerticalAlignment.Bottom
                        });
                    }
                    priceStack.Children.Add(new TextBlock { 
                        Text = $"{line.TotalLigne:0.00}", 
                        FontWeight = FontWeights.Bold 
                    });

                    DockPanel.SetDock(priceStack, Dock.Right);
                    detailStack.Children.Add(priceStack);

                    row2.Cells.Add(new TableCell(new BlockUIContainer(detailStack)));
                    rowGroup.Rows.Add(row2);
                }
                table.RowGroups.Add(rowGroup);
                doc.Blocks.Add(table);
            }

            // Divider
            doc.Blocks.Add(new Paragraph(new Run(new string('-', 35))) { TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 10, 0, 5) });

            // 4. Totals Section
            var totalGrid = new Grid();
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            int rowIdx = 0;
            void AddTotalRow(string label, string value, bool isBold = false, double fontSize = 11)
            {
                totalGrid.RowDefinitions.Add(new RowDefinition());
                var lbl = new TextBlock { Text = label, FontSize = fontSize };
                if (isBold) lbl.FontWeight = FontWeights.Bold;
                Grid.SetRow(lbl, rowIdx);
                Grid.SetColumn(lbl, 0);
                totalGrid.Children.Add(lbl);

                var val = new TextBlock { Text = value, FontSize = fontSize, HorizontalAlignment = HorizontalAlignment.Right };
                if (isBold) val.FontWeight = FontWeights.Bold;
                Grid.SetRow(val, rowIdx);
                Grid.SetColumn(val, 1);
                totalGrid.Children.Add(val);
                rowIdx++;
            }

            AddTotalRow($"Total {vente.NbArticles} articles", $"{(vente.Total + vente.TotalRemise):0.00}", true);
            if (vente.TotalRemise > 0)
            {
                AddTotalRow("Total économies", $"-{vente.TotalRemise:0.00}");
            }
            AddTotalRow("TOTAL À PAYER", $"{vente.Total:0.00}", true, 14);

            doc.Blocks.Add(new BlockUIContainer(totalGrid));

            // 5. Payment Details
            var payPara = new Paragraph { Margin = new Thickness(0, 10, 0, 0) };
            if (vente.MontantEspeces > 0)
            {
                 payPara.Inlines.Add(new Run($"{ (vente.MoyenPaiement == "Mixte" || vente.MoyenPaiement == "Espece/CB" ? "Part Espèces" : "Reçu Espèces") } : {vente.MontantEspeces:0.00}€"));
                 payPara.Inlines.Add(new LineBreak());
            }
            if (vente.MontantCB > 0)
            {
                 payPara.Inlines.Add(new Run($"{ (vente.MoyenPaiement == "Mixte" || vente.MoyenPaiement == "Espece/CB" ? "Part CB" : "Reçu CB") } : {vente.MontantCB:0.00}€"));
                 payPara.Inlines.Add(new LineBreak());
            }
            if (vente.MonnaieRendue > 0)
            {
                 payPara.Inlines.Add(new Bold(new Run($"Monnaie Rendue : {vente.MonnaieRendue:0.00}€")));
                 payPara.Inlines.Add(new LineBreak());
            }
            doc.Blocks.Add(payPara);

            // 6. TVA Breakdown Table
            doc.Blocks.Add(new Paragraph(new Run(new string('-', 35))) { Margin = new Thickness(0, 10, 0, 5) });
            
            var tvaTable = new Table { CellSpacing = 0, FontSize = 10 };
            tvaTable.Columns.Add(new TableColumn { Width = new GridLength(40) }); // Code
            tvaTable.Columns.Add(new TableColumn { Width = new GridLength(60) }); // %
            tvaTable.Columns.Add(new TableColumn { Width = new GridLength(60) }); // HT
            tvaTable.Columns.Add(new TableColumn { Width = new GridLength(60) }); // TVA
            tvaTable.Columns.Add(new TableColumn { Width = new GridLength(60) }); // TTC
 
            var tvaRows = new TableRowGroup();
            var tvaHeader = new TableRow { FontWeight = FontWeights.Bold };
            tvaHeader.Cells.Add(new TableCell(new Paragraph(new Run("Code"))));
            tvaHeader.Cells.Add(new TableCell(new Paragraph(new Run("%"))));
            tvaHeader.Cells.Add(new TableCell(new Paragraph(new Run("HT"))));
            tvaHeader.Cells.Add(new TableCell(new Paragraph(new Run("TVA"))));
            tvaHeader.Cells.Add(new TableCell(new Paragraph(new Run("TTC"))));
            tvaRows.Rows.Add(tvaHeader);
 
            // Calculate real TVA groups
            var tvaGroups = vente.Lignes
                .GroupBy(l => l.TaxTier)
                .OrderBy(g => g.Key);
 
            foreach (var group in tvaGroups)
            {
                decimal rate = group.Key == 1 ? 5.5m : (group.Key == 2 ? 10.0m : 20.0m);
                decimal ttcForGroup = group.Sum(l => l.TotalLigne);
                decimal htForGroup = ttcForGroup / (1 + (rate / 100));
                decimal tvaForGroup = ttcForGroup - htForGroup;
 
                var rowTva = new TableRow();
                rowTva.Cells.Add(new TableCell(new Paragraph(new Run(group.Key.ToString()))));
                rowTva.Cells.Add(new TableCell(new Paragraph(new Run($"{rate:0.0}%"))));
                rowTva.Cells.Add(new TableCell(new Paragraph(new Run($"{htForGroup:0.00}"))));
                rowTva.Cells.Add(new TableCell(new Paragraph(new Run($"{tvaForGroup:0.00}"))));
                rowTva.Cells.Add(new TableCell(new Paragraph(new Run($"{ttcForGroup:0.00}"))));
                tvaRows.Rows.Add(rowTva);
            }
 
            tvaTable.RowGroups.Add(tvaRows);
            doc.Blocks.Add(tvaTable);

            // 7. Footer
            var footerPara = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
            footerPara.Inlines.Add(new Run("Merci de votre visite !"));
            footerPara.Inlines.Add(new LineBreak());
            footerPara.Inlines.Add(new Run("À bientôt chez " + (entreprise.Nom ?? "nous")));
            doc.Blocks.Add(footerPara);

            return doc;
        }

        public void PrintCommandeTicket(Commande commande, Entreprise entreprise)
        {
            try
            {
                double width = GetTicketWidth();
                var doc = GenerateCommandeTicketDocument(commande, entreprise, width);
                var pd = new PrintDialog();
                doc.PageWidth = width;
                doc.PageHeight = double.NaN;
                IDocumentPaginatorSource idp = doc;
                pd.PrintDocument(idp.DocumentPaginator, $"Commande {commande.NumeroCommande}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur d'impression : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public FlowDocument GenerateCommandeTicketDocument(Commande commande, Entreprise entreprise, double width)
        {
            var doc = new FlowDocument
            {
                PageWidth = width,
                PagePadding = new Thickness(5),
                FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
                FontSize = 11,
                Background = BrushesWpf.White
            };

            // 1. Header (Logo + Enterprise Info)
            var headerPara = new Paragraph { TextAlignment = TextAlignment.Center };

            string? finalLogoPath = null;
            if (!string.IsNullOrEmpty(entreprise.LogoPath))
            {
                if (File.Exists(entreprise.LogoPath)) finalLogoPath = entreprise.LogoPath;
                else
                {
                    string relativePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.GetFileName(entreprise.LogoPath));
                    if (File.Exists(relativePath)) finalLogoPath = relativePath;
                }
            }

            if (finalLogoPath != null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(finalLogoPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    var image = new ImageWpf { Source = bitmap, Width = 100, Height = 100, Stretch = Stretch.Uniform };
                    headerPara.Inlines.Add(new InlineUIContainer(image));
                    headerPara.Inlines.Add(new LineBreak());
                }
                catch { }
            }

            headerPara.Inlines.Add(new Bold(new Run(entreprise.Nom ?? "Magasin")) { FontSize = 14 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run(entreprise.Adresse ?? ""));
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run(entreprise.Telephone ?? ""));
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new LineBreak());

            // 2. COMMANDE Header
            headerPara.Inlines.Add(new Bold(new Run("═══ COMMANDE ═══")) { FontSize = 14 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Bold(new Run(commande.NumeroCommande)) { FontSize = 16 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new LineBreak());

            // Client info
            headerPara.Inlines.Add(new Bold(new Run($"{commande.Prenom} {commande.Nom}")) { FontSize = 14 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Bold(new Run($"Tél: {FormatPhone(commande.Telephone)}")) { FontSize = 12 });
            headerPara.Inlines.Add(new LineBreak());
            if (!string.IsNullOrWhiteSpace(commande.Adresse))
            {
                headerPara.Inlines.Add(new Run(commande.Adresse));
                headerPara.Inlines.Add(new LineBreak());
            }
            if (!string.IsNullOrWhiteSpace(commande.VilleCodePostal))
            {
                headerPara.Inlines.Add(new Run(commande.VilleCodePostal));
                headerPara.Inlines.Add(new LineBreak());
            }
            headerPara.Inlines.Add(new LineBreak());

            // Date
            headerPara.Inlines.Add(new Run(commande.CreatedAt.ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture)));
            headerPara.Inlines.Add(new LineBreak());
            doc.Blocks.Add(headerPara);

            // 3. Products
            doc.Blocks.Add(new Paragraph(new Run(new string('-', 35))) { Margin = new Thickness(0, 5, 0, 5) });

            var table = new Table { CellSpacing = 0 };
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            var rowGroup = new TableRowGroup();

            foreach (var line in commande.Lignes)
            {
                var row1 = new TableRow();
                row1.Cells.Add(new TableCell(new Paragraph(new Run(line.ProduitNom)) { Margin = new Thickness(0) }));
                rowGroup.Rows.Add(row1);

                if (!string.IsNullOrEmpty(line.PromotionAppliquee))
                {
                    var promoRow = new TableRow();
                    promoRow.Cells.Add(new TableCell(new Paragraph(new Run($"   * {line.PromotionAppliquee}"))
                    {
                        FontSize = 9, FontStyle = FontStyles.Italic, Foreground = BrushesWpf.DarkGreen, Margin = new Thickness(0)
                    }));
                    rowGroup.Rows.Add(promoRow);
                }

                var row2 = new TableRow();
                var detailStack = new DockPanel { LastChildFill = true };
                string qtyStr = line.Quantite % 1 == 0 ? $"{line.Quantite:0}" : $"{line.Quantite:0.000}";
                string detailStr = $"   {qtyStr} X {line.PrixUnitaire:0.00}€";
                var detailText = new TextBlock { Text = detailStr, FontSize = 10, Foreground = BrushesWpf.DimGray };
                DockPanel.SetDock(detailText, Dock.Left);
                detailStack.Children.Add(detailText);
                var priceText = new TextBlock { Text = $"{line.TotalLigne:0.00}", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
                DockPanel.SetDock(priceText, Dock.Right);
                detailStack.Children.Add(priceText);
                row2.Cells.Add(new TableCell(new BlockUIContainer(detailStack)));
                rowGroup.Rows.Add(row2);
            }
            table.RowGroups.Add(rowGroup);
            doc.Blocks.Add(table);

            // 4. Totals
            doc.Blocks.Add(new Paragraph(new Run(new string('-', 35))) { TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 10, 0, 5) });

            var totalGrid = new Grid();
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            int rowIdx = 0;

            void AddRow(string label, string value, bool isBold = false, double fontSize = 11)
            {
                totalGrid.RowDefinitions.Add(new RowDefinition());
                var lbl = new TextBlock { Text = label, FontSize = fontSize };
                if (isBold) lbl.FontWeight = FontWeights.Bold;
                Grid.SetRow(lbl, rowIdx); Grid.SetColumn(lbl, 0);
                totalGrid.Children.Add(lbl);
                var val = new TextBlock { Text = value, FontSize = fontSize, HorizontalAlignment = HorizontalAlignment.Right };
                if (isBold) val.FontWeight = FontWeights.Bold;
                Grid.SetRow(val, rowIdx); Grid.SetColumn(val, 1);
                totalGrid.Children.Add(val);
                rowIdx++;
            }

            AddRow($"Total {commande.NbArticles} articles", $"{commande.Total:0.00}", true);
            if (commande.TotalRemise > 0)
                AddRow("Économies", $"-{commande.TotalRemise:0.00}");
            if (commande.AvecLivraison)
                AddRow("🚚 Livraison", $"{commande.MontantLivraison:0.00}");
            AddRow("TOTAL À PAYER", $"{commande.TotalAvecLivraison:0.00}", true, 14);
            AddRow("Montant Payé", $"{commande.MontantPaye:0.00}");
            if (!string.IsNullOrWhiteSpace(commande.ModePaiement))
                AddRow("Mode paiement", commande.ModePaiementDisplay);
            AddRow("RESTANT", $"{commande.Restant:0.00}", true, 12);

            doc.Blocks.Add(new BlockUIContainer(totalGrid));

            // 5. Payment status
            doc.Blocks.Add(new Paragraph(new Run(new string('-', 35))) { Margin = new Thickness(0, 10, 0, 5) });

            var statusPara = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
            if (commande.Restant <= 0)
            {
                statusPara.Inlines.Add(new Bold(new Run("═══ RÉGLÉ ═══")) { FontSize = 16 });
            }
            else
            {
                statusPara.Inlines.Add(new Bold(new Run("═══ NON RÉGLÉ ═══")) { FontSize = 16 });
                statusPara.Inlines.Add(new LineBreak());
                statusPara.Inlines.Add(new Bold(new Run($"Restant : {commande.Restant:0.00}€")) { FontSize = 12 });
            }
            doc.Blocks.Add(statusPara);

            // 6. Footer
            var footerPara = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            footerPara.Inlines.Add(new Run("Merci pour votre commande !"));
            footerPara.Inlines.Add(new LineBreak());
            footerPara.Inlines.Add(new Run("À bientôt chez " + (entreprise.Nom ?? "nous")));
            doc.Blocks.Add(footerPara);

            return doc;
        }

        /// <summary>
        /// Formats a phone number with spaces between digit pairs for readability.
        /// Example: "0612345678" → "06 12 34 56 78"
        /// </summary>
        private static string FormatPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return phone ?? "";
            var digits = new System.Text.StringBuilder();
            foreach (char c in phone)
            {
                if (char.IsDigit(c) || c == '+') digits.Append(c);
            }
            string clean = digits.ToString();
            if (clean.Length < 4) return clean;
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < clean.Length; i++)
            {
                if (i > 0 && i % 2 == 0) result.Append(' ');
                result.Append(clean[i]);
            }
            return result.ToString();
        }
    }
}

