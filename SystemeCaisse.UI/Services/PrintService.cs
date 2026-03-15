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
                var doc = GenerateTicketDocument(vente, entreprise, isTrainingMode);
                
                var pd = new PrintDialog();
                doc.PageWidth = TicketWidth;
                doc.PageHeight = double.NaN;
                
                IDocumentPaginatorSource idp = doc;
                pd.PrintDocument(idp.DocumentPaginator, "Ticket de Caisse");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur d'impression : {ex.Message}");
            }
        }

        public FlowDocument GenerateTicketDocument(Vente vente, Entreprise entreprise, bool isTrainingMode)
        {
            var doc = new FlowDocument
            {
                PageWidth = TicketWidth,
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
            
            // Logo
            if (!string.IsNullOrEmpty(entreprise.LogoPath) && File.Exists(entreprise.LogoPath))
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri(entreprise.LogoPath));
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
            catch (Exception ex) 
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
                 payPara.Inlines.Add(new Run($"{ (vente.MoyenPaiement == "Mixte" ? "Espèces" : "Reçu Espèces") } : {vente.MontantEspeces:0.00}€"));
                 payPara.Inlines.Add(new LineBreak());
            }
            if (vente.MontantCB > 0)
            {
                 payPara.Inlines.Add(new Run($"{ (vente.MoyenPaiement == "Mixte" ? "Carte Bancaire" : "Reçu CB") } : {vente.MontantCB:0.00}€"));
                 payPara.Inlines.Add(new LineBreak());
            }
            if (vente.MonnaieRendue > 0)
            {
                 payPara.Inlines.Add(new Bold(new Run($"Monnaie Rendue : {vente.MonnaieRendue:0.00}€")));
                 payPara.Inlines.Add(new LineBreak());
            }
            doc.Blocks.Add(payPara);

            // 6. TVA Breakdown Table (Simplified fixed 20% and 5.5% as examples or real calculation)
            // Real calculation based on data would be better.
            // For now, let's assume standard rates if not stored.
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

            // Mock TVA Calculation (since real TVA per product is not yet fully in core entities)
            // In a real app, this information would be stored in LigneVente.
            decimal ttc = vente.Total;
            decimal ht = ttc / 1.20m;
            decimal tvaValue = ttc - ht;

            var rowTva = new TableRow();
            rowTva.Cells.Add(new TableCell(new Paragraph(new Run("1"))));
            rowTva.Cells.Add(new TableCell(new Paragraph(new Run("20%00"))));
            rowTva.Cells.Add(new TableCell(new Paragraph(new Run($"{ht:0.00}"))));
            rowTva.Cells.Add(new TableCell(new Paragraph(new Run($"{tvaValue:0.00}"))));
            rowTva.Cells.Add(new TableCell(new Paragraph(new Run($"{ttc:0.00}"))));
            tvaRows.Rows.Add(rowTva);

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
    }
}

