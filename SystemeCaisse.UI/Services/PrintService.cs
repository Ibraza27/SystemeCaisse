using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Printing;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;
using ZXing;
using BrushesWpf = System.Windows.Media.Brushes;
using ImageWpf = System.Windows.Controls.Image;

namespace SystemeCaisse.UI.Services
{
    public class PrintService
    {
        // Largeur imprimable réelle par défaut pour un rouleau thermique 80mm (~67mm utiles sur tête 72mm à 96 DPI)
        private const double DefaultPrintableWidth80Mm = 253;

        public void PrintTicket(Vente vente, Entreprise entreprise, bool isTrainingMode = false)
        {
            try
            {
                double width = GetTicketWidth();
                var doc = GenerateTicketDocument(vente, entreprise, isTrainingMode, width);
                PrintFlowDocument(doc, "Ticket de Caisse");
            }
            catch (Exception ex)
            {
                MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur d'impression : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void PrintCommandeTicket(Commande commande, Entreprise entreprise)
        {
            try
            {
                double width = GetTicketWidth();
                var doc = GenerateCommandeTicketDocument(commande, entreprise, width);
                PrintFlowDocument(doc, $"Commande {commande.NumeroCommande}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(WindowHelper.GetAdminWindow(), $"Erreur d'impression : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void PrintFlowDocument(FlowDocument doc, string jobTitle)
        {
            double width = GetTicketWidth();
            var pd = new PrintDialog();

            // Sélectionner automatiquement l'imprimante configurée dans Périphériques si disponible
            string? configuredPrinter = GetConfiguredPrinterName();
            if (!string.IsNullOrWhiteSpace(configuredPrinter))
            {
                try
                {
                    using var server = new LocalPrintServer();
                    var queue = server.GetPrintQueues()
                        .FirstOrDefault(q => string.Equals(q.Name, configuredPrinter, StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(q.FullName, configuredPrinter, StringComparison.OrdinalIgnoreCase));
                    if (queue != null)
                    {
                        pd.PrintQueue = queue;
                    }
                }
                catch { }
            }

            // Si le driver de l'imprimante thermique annonce une zone imprimable plus étroite, s'y adapter
            try
            {
                if (pd.PrintableAreaWidth > 120 && pd.PrintableAreaWidth < width)
                {
                    width = Math.Max(150, pd.PrintableAreaWidth - 4);
                }
            }
            catch { }

            doc.PageWidth = width;
            doc.ColumnWidth = width;
            doc.PageHeight = double.NaN;

            IDocumentPaginatorSource idp = doc;
            pd.PrintDocument(idp.DocumentPaginator, jobTitle);
        }

        private static PosteSettings? TryReadPosteSettings()
        {
            try
            {
                return new LocalSettingsStore().Current;
            }
            catch { }
            return null;
        }

        private static string? GetConfiguredPrinterName()
        {
            var poste = TryReadPosteSettings();
            if (!string.IsNullOrWhiteSpace(poste?.Peripheriques?.Imprimante))
                return poste.Peripheriques.Imprimante;

            try
            {
                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "caisse.db");
                if (File.Exists(dbPath))
                {
                    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                    optionsBuilder.UseSqlite($"Data Source={dbPath}");
                    using var context = new AppDbContext(optionsBuilder.Options);
                    var cfg = context.Configuration.AsNoTracking().FirstOrDefault(c => c.Cle == "imprimante_defaut");
                    if (!string.IsNullOrWhiteSpace(cfg?.Valeur))
                        return cfg.Valeur;
                }
            }
            catch { }
            return null;
        }

        public static bool ShouldShowLogoOnTicket()
        {
            var poste = TryReadPosteSettings();
            if (poste?.Peripheriques != null)
                return poste.Peripheriques.AfficherLogoTicket;
            return true;
        }

        /// <summary>
        /// Calcule la largeur utile imprimable en pixels (96 DPI) à partir de la largeur papier configurée (58mm à 80mm).
        /// Sur une imprimante thermique 80mm, la tête d'impression fait 72mm et le driver applique ~4-5mm de marge :
        /// la largeur utile sans débordement est de (mm - 13) * 3.78 (~253px pour 80mm, ~170px pour 58mm).
        /// </summary>
        public double GetTicketWidth()
        {
            int mm = 80;
            try
            {
                var poste = TryReadPosteSettings();
                if (poste?.Peripheriques != null && poste.Peripheriques.LargeurTicketMm >= 50 && poste.Peripheriques.LargeurTicketMm <= 90)
                {
                    mm = poste.Peripheriques.LargeurTicketMm;
                }
                else
                {
                    string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "caisse.db");
                    if (File.Exists(dbPath))
                    {
                        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                        optionsBuilder.UseSqlite($"Data Source={dbPath}");
                        using var context = new AppDbContext(optionsBuilder.Options);
                        var cfg = context.Configuration.AsNoTracking().FirstOrDefault(c => c.Cle == "ticket_width");
                        if (cfg != null && int.TryParse(cfg.Valeur, out int w) && w >= 50 && w <= 90)
                        {
                            mm = w;
                        }
                    }
                }
            }
            catch { }

            double printableMm = Math.Max(44, mm - 13);
            return Math.Round(printableMm * 3.78);
        }

        private static BitmapSource? ResolveRawLogoBitmap(string? logoPath)
        {
            var candidatePaths = new List<string>();

            if (!string.IsNullOrWhiteSpace(logoPath))
            {
                candidatePaths.Add(logoPath);

                try
                {
                    var resolved = Produit.ImagePathResolver?.Invoke(logoPath);
                    if (!string.IsNullOrWhiteSpace(resolved))
                        candidatePaths.Add(resolved);
                }
                catch { }

                string cleanRel = logoPath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
                candidatePaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cleanRel));
                candidatePaths.Add(Path.Combine(AppPaths.ImagesCacheDir, cleanRel));

                string fileName = Path.GetFileName(logoPath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    candidatePaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Logo", fileName));
                    candidatePaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", fileName));
                    candidatePaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName));
                    candidatePaths.Add(Path.Combine(AppPaths.ImagesCacheDir, "Images", "Logo", fileName));
                    candidatePaths.Add(Path.Combine(AppPaths.ImagesCacheDir, "Images", fileName));
                }
            }

            // Repli systématique sur le logo de l'application (comme dans MainWindow.xaml TargetNullValue=/Images/logo.png)
            candidatePaths.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "logo.png"));
            candidatePaths.Add(Path.Combine(AppPaths.InstallDir, "Images", "logo.png"));

            foreach (var path in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                    catch { }
                }
            }

            // Dernier repli : ressource WPF embarquée pack://application:,,,/Images/logo.png
            try
            {
                var packBmp = new BitmapImage();
                packBmp.BeginInit();
                packBmp.UriSource = new Uri("pack://application:,,,/Images/logo.png", UriKind.Absolute);
                packBmp.CacheOption = BitmapCacheOption.OnLoad;
                packBmp.EndInit();
                packBmp.Freeze();
                return packBmp;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Charge le logo et le convertit en Noir & Blanc contrasté dans un format Bgra32 standard
        /// (compatible avec 100% des drivers d'imprimantes thermiques GDI/XPS qui ignorent parfois le format Gray8).
        /// </summary>
        public static BitmapSource? LoadBlackAndWhiteLogo(string? logoPath)
        {
            if (!ShouldShowLogoOnTicket()) return null;

            var source = ResolveRawLogoBitmap(logoPath);
            if (source == null) return null;

            try
            {
                var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth;
                int h = bgra.PixelHeight;
                int stride = w * 4;
                byte[] pixels = new byte[h * stride];
                bgra.CopyPixels(pixels, stride, 0);

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];

                    // Fusion alpha sur fond blanc pur (255, 255, 255)
                    int rBlended = (r * a + 255 * (255 - a)) / 255;
                    int gBlended = (g * a + 255 * (255 - a)) / 255;
                    int bBlended = (b * a + 255 * (255 - a)) / 255;

                    int lum = (int)(0.299 * rBlended + 0.587 * gBlended + 0.114 * bBlended);

                    // Renforcement du contraste pour imprimante thermique :
                    // les traits colorés (ex: bleu Hippocampe) deviennent gris foncé/noir net, le fond reste blanc pur
                    byte bw;
                    if (lum >= 235)
                    {
                        bw = 255;
                    }
                    else
                    {
                        double normalized = lum / 235.0;
                        int darkened = (int)Math.Round(normalized * normalized * 150.0);
                        bw = (byte)Math.Clamp(darkened, 0, 255);
                    }

                    pixels[i] = bw;     // B
                    pixels[i + 1] = bw; // G
                    pixels[i + 2] = bw; // R
                    pixels[i + 3] = 255; // A (opaque)
                }

                var bwBitmap = BitmapSource.Create(
                    w,
                    h,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    pixels,
                    stride);
                bwBitmap.Freeze();
                return bwBitmap;
            }
            catch
            {
                return source;
            }
        }

        public static BlockUIContainer CreateSeparatorBlock(double marginTop = 6, double marginBottom = 6)
        {
            return new BlockUIContainer(new Border
            {
                Height = 1,
                Background = BrushesWpf.Black,
                Margin = new Thickness(0, marginTop, 0, marginBottom)
            });
        }

        public FlowDocument GenerateTicketDocument(Vente vente, Entreprise entreprise, bool isTrainingMode, double width)
        {
            if (width <= 0) width = DefaultPrintableWidth80Mm;

            var doc = new FlowDocument
            {
                PageWidth = width,
                ColumnWidth = width,
                PagePadding = new Thickness(4, 4, 8, 4),
                FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
                FontSize = 10.5,
                Background = BrushesWpf.White
            };

            double contentWidth = Math.Max(140, width - 12);

            // 1. Header (Logo + Enterprise Info)
            var headerPara = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0) };

            if (isTrainingMode)
            {
                headerPara.Inlines.Add(new Bold(new Run("*** MODE FORMATION ***")) { FontSize = 14 });
                headerPara.Inlines.Add(new LineBreak());
            }

            var bwLogo = LoadBlackAndWhiteLogo(entreprise.LogoPath);
            if (bwLogo != null)
            {
                var image = new ImageWpf
                {
                    Source = bwLogo,
                    Width = 85,
                    Height = 85,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                headerPara.Inlines.Add(new InlineUIContainer(image));
                headerPara.Inlines.Add(new LineBreak());
            }

            headerPara.Inlines.Add(new Bold(new Run(entreprise.Nom ?? "Magasin")) { FontSize = 13 });
            headerPara.Inlines.Add(new LineBreak());
            if (!string.IsNullOrWhiteSpace(entreprise.Adresse))
            {
                headerPara.Inlines.Add(new Run(entreprise.Adresse));
                headerPara.Inlines.Add(new LineBreak());
            }
            if (!string.IsNullOrWhiteSpace(entreprise.Telephone))
            {
                headerPara.Inlines.Add(new Run(entreprise.Telephone));
                headerPara.Inlines.Add(new LineBreak());
            }
            headerPara.Inlines.Add(new LineBreak());

            // 2. Dates & Barcode
            headerPara.Inlines.Add(new Run(vente.CreatedAt.ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture)));
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Run(vente.CreatedAt.ToString("dd MMMM yyyy", new CultureInfo("fr-FR"))));
            headerPara.Inlines.Add(new LineBreak());

            int barcodeWidth = (int)Math.Min(200, Math.Max(130, contentWidth - 16));
            try
            {
                var writer = new ZXing.BarcodeWriter<System.Drawing.Bitmap>
                {
                    Format = ZXing.BarcodeFormat.CODE_128,
                    Options = new ZXing.Common.EncodingOptions
                    {
                        Width = barcodeWidth,
                        Height = 50,
                        Margin = 0
                    },
                    Renderer = new ZXing.Windows.Compatibility.BitmapRenderer()
                };

                using var bitmap = writer.Write(vente.NumeroTicket);
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var wpfBitmap = new BitmapImage();
                wpfBitmap.BeginInit();
                wpfBitmap.StreamSource = ms;
                wpfBitmap.CacheOption = BitmapCacheOption.OnLoad;
                wpfBitmap.EndInit();
                wpfBitmap.Freeze();

                var barcodeImg = new ImageWpf
                {
                    Source = wpfBitmap,
                    Width = barcodeWidth,
                    Height = 50,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 4, 0, 2)
                };
                headerPara.Inlines.Add(new InlineUIContainer(barcodeImg));
            }
            catch
            {
                headerPara.Inlines.Add(new Run($"Ticket N°: {vente.NumeroTicket}"));
            }

            doc.Blocks.Add(headerPara);

            // 3. Products Grouped by Category
            var categoryGroups = vente.Lignes
                .GroupBy(l => l.CategorieNom ?? "DIVERS")
                .OrderBy(g => g.Key);

            foreach (var group in categoryGroups)
            {
                var catPara = new Paragraph(new Bold(new Run($">> {group.Key.ToUpper()}")))
                {
                    Margin = new Thickness(0, 8, 0, 3),
                    FontSize = 10.5
                };
                doc.Blocks.Add(catPara);

                var table = new Table { CellSpacing = 0, Margin = new Thickness(0) };
                table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                var rowGroup = new TableRowGroup();

                foreach (var line in group)
                {
                    // Line 1: Name
                    var row1 = new TableRow();
                    var namePara = new Paragraph(new Run(line.ProduitNom)) { Margin = new Thickness(0) };
                    row1.Cells.Add(new TableCell(namePara) { Padding = new Thickness(0, 1, 0, 0) });
                    rowGroup.Rows.Add(row1);

                    // Line 1.5: Promotion Name
                    if (!string.IsNullOrEmpty(line.PromotionAppliquee))
                    {
                        var promoRow = new TableRow();
                        var promoPara = new Paragraph(new Run($"  * {line.PromotionAppliquee}"))
                        {
                            FontSize = 9,
                            FontStyle = FontStyles.Italic,
                            Foreground = BrushesWpf.Black,
                            Margin = new Thickness(0)
                        };
                        promoRow.Cells.Add(new TableCell(promoPara) { Padding = new Thickness(0) });
                        rowGroup.Rows.Add(promoRow);
                    }

                    // Line 2: Qty x Price and Total TTC (Grid 2 colonnes sans débordement)
                    var row2 = new TableRow();
                    var lineGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                    lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    string qtyStr = line.Quantite % 1 == 0 ? $"{line.Quantite:0}" : $"{line.Quantite:0.000}";
                    decimal originalTotal = line.PrixUnitaire * line.Quantite;
                    bool isDiscounted = line.Remise > 0;

                    var detailText = new TextBlock
                    {
                        Text = $"  {qtyStr} x {line.PrixUnitaire:0.00}€",
                        FontSize = 9.5,
                        Foreground = BrushesWpf.DimGray,
                        TextWrapping = TextWrapping.Wrap
                    };
                    Grid.SetColumn(detailText, 0);
                    lineGrid.Children.Add(detailText);

                    var priceStack = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    if (isDiscounted)
                    {
                        priceStack.Children.Add(new TextBlock
                        {
                            Text = $"{originalTotal:0.00} ",
                            FontSize = 9,
                            Foreground = BrushesWpf.Gray,
                            TextDecorations = TextDecorations.Strikethrough,
                            VerticalAlignment = VerticalAlignment.Bottom
                        });
                    }
                    priceStack.Children.Add(new TextBlock
                    {
                        Text = $"{line.TotalLigne:0.00}€",
                        FontWeight = FontWeights.Bold,
                        FontSize = 10.5
                    });

                    Grid.SetColumn(priceStack, 1);
                    lineGrid.Children.Add(priceStack);

                    row2.Cells.Add(new TableCell(new BlockUIContainer(lineGrid)) { Padding = new Thickness(0) });
                    rowGroup.Rows.Add(row2);
                }
                table.RowGroups.Add(rowGroup);
                doc.Blocks.Add(table);
            }

            // Divider
            doc.Blocks.Add(CreateSeparatorBlock(8, 5));

            // 4. Totals Section
            var totalGrid = new Grid();
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            int rowIdx = 0;
            void AddTotalRow(string label, string value, bool isBold = false, double fontSize = 10.5)
            {
                totalGrid.RowDefinitions.Add(new RowDefinition());
                var lbl = new TextBlock
                {
                    Text = label,
                    FontSize = fontSize,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 6, 1)
                };
                if (isBold) lbl.FontWeight = FontWeights.Bold;
                Grid.SetRow(lbl, rowIdx);
                Grid.SetColumn(lbl, 0);
                totalGrid.Children.Add(lbl);

                var val = new TextBlock
                {
                    Text = value,
                    FontSize = fontSize,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 1, 0, 1)
                };
                if (isBold) val.FontWeight = FontWeights.Bold;
                Grid.SetRow(val, rowIdx);
                Grid.SetColumn(val, 1);
                totalGrid.Children.Add(val);
                rowIdx++;
            }

            AddTotalRow($"Total {vente.NbArticles} articles", $"{(vente.Total + vente.TotalRemise):0.00}€", true);
            if (vente.TotalRemise > 0)
            {
                AddTotalRow("Total économies", $"-{vente.TotalRemise:0.00}€");
            }
            AddTotalRow("TOTAL À PAYER", $"{vente.Total:0.00}€", true, 13);

            doc.Blocks.Add(new BlockUIContainer(totalGrid));

            // 5. Payment Details
            var payPara = new Paragraph { Margin = new Thickness(0, 8, 0, 0), FontSize = 10 };
            if (vente.MoyenPaiement == "Wero")
            {
                payPara.Inlines.Add(new Run($"Payé par Wero : {vente.Total:0.00}€"));
                payPara.Inlines.Add(new LineBreak());
            }
            else if (vente.MoyenPaiement == "En ligne")
            {
                payPara.Inlines.Add(new Run($"Payé En ligne : {vente.Total:0.00}€"));
                payPara.Inlines.Add(new LineBreak());
            }
            if (vente.MontantEspeces > 0)
            {
                payPara.Inlines.Add(new Run($"{(vente.MoyenPaiement == "Mixte" || vente.MoyenPaiement == "Espece/CB" ? "Part Espèces" : "Reçu Espèces")} : {vente.MontantEspeces:0.00}€"));
                payPara.Inlines.Add(new LineBreak());
            }
            if (vente.MontantCB > 0)
            {
                payPara.Inlines.Add(new Run($"{(vente.MoyenPaiement == "Mixte" || vente.MoyenPaiement == "Espece/CB" ? "Part CB" : "Reçu CB")} : {vente.MontantCB:0.00}€"));
                payPara.Inlines.Add(new LineBreak());
            }
            if (vente.MonnaieRendue > 0)
            {
                payPara.Inlines.Add(new Bold(new Run($"Monnaie Rendue : {vente.MonnaieRendue:0.00}€")));
                payPara.Inlines.Add(new LineBreak());
            }
            doc.Blocks.Add(payPara);

            // 6. TVA Breakdown Table (colonnes proportionnelles * sans débordement)
            doc.Blocks.Add(CreateSeparatorBlock(8, 5));

            var tvaTable = new Table { CellSpacing = 0, FontSize = 9.5, Margin = new Thickness(0) };
            tvaTable.Columns.Add(new TableColumn { Width = new GridLength(0.7, GridUnitType.Star) }); // Code
            tvaTable.Columns.Add(new TableColumn { Width = new GridLength(1.0, GridUnitType.Star) }); // %
            tvaTable.Columns.Add(new TableColumn { Width = new GridLength(1.1, GridUnitType.Star) }); // HT
            tvaTable.Columns.Add(new TableColumn { Width = new GridLength(1.1, GridUnitType.Star) }); // TVA
            tvaTable.Columns.Add(new TableColumn { Width = new GridLength(1.1, GridUnitType.Star) }); // TTC

            static TableCell CreateTvaCell(string text, TextAlignment align = TextAlignment.Left)
            {
                return new TableCell(new Paragraph(new Run(text)) { Margin = new Thickness(0), TextAlignment = align })
                {
                    Padding = new Thickness(0, 1, 2, 1)
                };
            }

            var tvaRows = new TableRowGroup();
            var tvaHeader = new TableRow { FontWeight = FontWeights.Bold };
            tvaHeader.Cells.Add(CreateTvaCell("Code"));
            tvaHeader.Cells.Add(CreateTvaCell("%"));
            tvaHeader.Cells.Add(CreateTvaCell("HT", TextAlignment.Right));
            tvaHeader.Cells.Add(CreateTvaCell("TVA", TextAlignment.Right));
            tvaHeader.Cells.Add(CreateTvaCell("TTC", TextAlignment.Right));
            tvaRows.Rows.Add(tvaHeader);

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
                rowTva.Cells.Add(CreateTvaCell(group.Key.ToString()));
                rowTva.Cells.Add(CreateTvaCell($"{rate:0.0}%"));
                rowTva.Cells.Add(CreateTvaCell($"{htForGroup:0.00}", TextAlignment.Right));
                rowTva.Cells.Add(CreateTvaCell($"{tvaForGroup:0.00}", TextAlignment.Right));
                rowTva.Cells.Add(CreateTvaCell($"{ttcForGroup:0.00}", TextAlignment.Right));
                tvaRows.Rows.Add(rowTva);
            }

            tvaTable.RowGroups.Add(tvaRows);
            doc.Blocks.Add(tvaTable);

            // 7. Footer
            var footerPara = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 0), FontSize = 10 };
            footerPara.Inlines.Add(new Run("Merci de votre visite !"));
            footerPara.Inlines.Add(new LineBreak());
            footerPara.Inlines.Add(new Run("À bientôt chez " + (entreprise.Nom ?? "nous")));
            doc.Blocks.Add(footerPara);

            return doc;
        }

        public FlowDocument GenerateCommandeTicketDocument(Commande commande, Entreprise entreprise, double width)
        {
            if (width <= 0) width = DefaultPrintableWidth80Mm;

            var doc = new FlowDocument
            {
                PageWidth = width,
                ColumnWidth = width,
                PagePadding = new Thickness(4, 4, 8, 4),
                FontFamily = new FontFamily("Consolas, Courier New, Monospace"),
                FontSize = 10.5,
                Background = BrushesWpf.White
            };

            // 1. Header (Logo + Enterprise Info)
            var headerPara = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0) };

            var bwLogo = LoadBlackAndWhiteLogo(entreprise.LogoPath);
            if (bwLogo != null)
            {
                var image = new ImageWpf
                {
                    Source = bwLogo,
                    Width = 85,
                    Height = 85,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                headerPara.Inlines.Add(new InlineUIContainer(image));
                headerPara.Inlines.Add(new LineBreak());
            }

            headerPara.Inlines.Add(new Bold(new Run(entreprise.Nom ?? "Magasin")) { FontSize = 13 });
            headerPara.Inlines.Add(new LineBreak());
            if (!string.IsNullOrWhiteSpace(entreprise.Adresse))
            {
                headerPara.Inlines.Add(new Run(entreprise.Adresse));
                headerPara.Inlines.Add(new LineBreak());
            }
            if (!string.IsNullOrWhiteSpace(entreprise.Telephone))
            {
                headerPara.Inlines.Add(new Run(entreprise.Telephone));
                headerPara.Inlines.Add(new LineBreak());
            }
            headerPara.Inlines.Add(new LineBreak());

            // 2. COMMANDE Header
            headerPara.Inlines.Add(new Bold(new Run(commande.IsGroupedCommande ? "══ COMMANDE REGROUPÉE ══" : "═══ COMMANDE ═══")) { FontSize = 12.5 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Bold(new Run(commande.NumeroCommande)) { FontSize = 14 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new LineBreak());

            // Client info
            headerPara.Inlines.Add(new Bold(new Run($"{commande.Prenom} {commande.Nom}")) { FontSize = 13 });
            headerPara.Inlines.Add(new LineBreak());
            headerPara.Inlines.Add(new Bold(new Run($"Tél: {FormatPhone(commande.Telephone)}")) { FontSize = 11.5 });
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
            if (!string.IsNullOrWhiteSpace(commande.Commentaire))
            {
                headerPara.Inlines.Add(new Bold(new Run($"Note : {commande.Commentaire}")) { FontSize = 10.5 });
                headerPara.Inlines.Add(new LineBreak());
            }
            headerPara.Inlines.Add(new LineBreak());

            // Date
            headerPara.Inlines.Add(new Run(commande.CreatedAt.ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture)));
            headerPara.Inlines.Add(new LineBreak());
            doc.Blocks.Add(headerPara);

            // 3. Products
            doc.Blocks.Add(CreateSeparatorBlock(6, 6));

            void AppendLinesTable(IEnumerable<LigneCommande> lines)
            {
                var table = new Table { CellSpacing = 0, Margin = new Thickness(0) };
                table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                var rowGroup = new TableRowGroup();

                foreach (var line in lines)
                {
                    var row1 = new TableRow();
                    row1.Cells.Add(new TableCell(new Paragraph(new Run(line.ProduitNom)) { Margin = new Thickness(0) })
                    {
                        Padding = new Thickness(0, 1, 0, 0)
                    });
                    rowGroup.Rows.Add(row1);

                    if (!string.IsNullOrEmpty(line.PromotionAppliquee))
                    {
                        var promoRow = new TableRow();
                        promoRow.Cells.Add(new TableCell(new Paragraph(new Run($"  * {line.PromotionAppliquee}"))
                        {
                            FontSize = 9,
                            FontStyle = FontStyles.Italic,
                            Foreground = BrushesWpf.Black,
                            Margin = new Thickness(0)
                        })
                        {
                            Padding = new Thickness(0)
                        });
                        rowGroup.Rows.Add(promoRow);
                    }

                    var row2 = new TableRow();
                    var lineGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                    lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var detailText = new TextBlock
                    {
                        Text = $"  {line.QuantiteDisplay} x {line.PrixUnitaire:0.00}€",
                        FontSize = 9.5,
                        Foreground = BrushesWpf.DimGray,
                        TextWrapping = TextWrapping.Wrap
                    };
                    Grid.SetColumn(detailText, 0);
                    lineGrid.Children.Add(detailText);

                    var priceText = new TextBlock
                    {
                        Text = $"{line.TotalLigne:0.00}€",
                        FontWeight = FontWeights.Bold,
                        FontSize = 10.5,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(priceText, 1);
                    lineGrid.Children.Add(priceText);

                    row2.Cells.Add(new TableCell(new BlockUIContainer(lineGrid)) { Padding = new Thickness(0) });
                    rowGroup.Rows.Add(row2);
                }
                table.RowGroups.Add(rowGroup);
                doc.Blocks.Add(table);
            }

            if (commande.IsGroupedCommande)
            {
                foreach (var subGroup in commande.SubClientTotals)
                {
                    var clientHeader = new Paragraph(new Bold(new Run($"── {subGroup.ClientNom.ToUpper()} ──")))
                    {
                        Margin = new Thickness(0, 8, 0, 3),
                        FontSize = 11
                    };
                    doc.Blocks.Add(clientHeader);

                    AppendLinesTable(subGroup.Lignes);

                    var subTotalGrid = new Grid { Margin = new Thickness(0, 2, 0, 5) };
                    subTotalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    subTotalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var stLbl = new TextBlock
                    {
                        Text = $"Sous-total {subGroup.ClientNom}",
                        FontWeight = FontWeights.Bold,
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap
                    };
                    var stVal = new TextBlock
                    {
                        Text = $"{subGroup.Total:0.00}€",
                        FontWeight = FontWeights.Bold,
                        FontSize = 10,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(stLbl, 0);
                    Grid.SetColumn(stVal, 1);
                    subTotalGrid.Children.Add(stLbl);
                    subTotalGrid.Children.Add(stVal);
                    doc.Blocks.Add(new BlockUIContainer(subTotalGrid));
                }

                var ungrouped = commande.Lignes.Where(l => string.IsNullOrWhiteSpace(l.ClientNom)).ToList();
                if (ungrouped.Count > 0)
                {
                    AppendLinesTable(ungrouped);
                }
            }
            else
            {
                AppendLinesTable(commande.Lignes);
            }

            // 4. Totals
            doc.Blocks.Add(CreateSeparatorBlock(8, 5));

            var totalGrid = new Grid();
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int rowIdx = 0;

            void AddRow(string label, string value, bool isBold = false, double fontSize = 10.5)
            {
                totalGrid.RowDefinitions.Add(new RowDefinition());
                var lbl = new TextBlock
                {
                    Text = label,
                    FontSize = fontSize,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 6, 1)
                };
                if (isBold) lbl.FontWeight = FontWeights.Bold;
                Grid.SetRow(lbl, rowIdx);
                Grid.SetColumn(lbl, 0);
                totalGrid.Children.Add(lbl);

                var val = new TextBlock
                {
                    Text = value,
                    FontSize = fontSize,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 1, 0, 1)
                };
                if (isBold) val.FontWeight = FontWeights.Bold;
                Grid.SetRow(val, rowIdx);
                Grid.SetColumn(val, 1);
                totalGrid.Children.Add(val);
                rowIdx++;
            }

            AddRow($"Total {commande.NbArticles} articles", $"{commande.Total:0.00}€", true);
            if (commande.TotalRemise > 0)
                AddRow("Économies", $"-{commande.TotalRemise:0.00}€");
            if (commande.AvecLivraison)
                AddRow("Livraison", $"{commande.MontantLivraison:0.00}€");
            AddRow("TOTAL À PAYER", $"{commande.TotalAvecLivraison:0.00}€", true, 13);
            AddRow("Montant Payé", $"{commande.MontantPaye:0.00}€");
            if (!string.IsNullOrWhiteSpace(commande.ModePaiement))
                AddRow("Mode paiement", commande.ModePaiementDisplay);
            AddRow("RESTANT", $"{commande.Restant:0.00}€", true, 11.5);

            doc.Blocks.Add(new BlockUIContainer(totalGrid));

            // 5. Payment status
            doc.Blocks.Add(CreateSeparatorBlock(8, 5));

            var statusPara = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
            if (commande.Restant <= 0)
            {
                statusPara.Inlines.Add(new Bold(new Run("═══ RÉGLÉ ═══")) { FontSize = 14 });
            }
            else
            {
                statusPara.Inlines.Add(new Bold(new Run("═══ NON RÉGLÉ ═══")) { FontSize = 14 });
                statusPara.Inlines.Add(new LineBreak());
                statusPara.Inlines.Add(new Bold(new Run($"Restant : {commande.Restant:0.00}€")) { FontSize = 11.5 });
            }
            doc.Blocks.Add(statusPara);

            // 6. Footer
            var footerPara = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0), FontSize = 10 };
            footerPara.Inlines.Add(new Run("Merci pour votre commande !"));
            footerPara.Inlines.Add(new LineBreak());
            footerPara.Inlines.Add(new Run("À bientôt chez " + (entreprise.Nom ?? "nous")));
            doc.Blocks.Add(footerPara);

            return doc;
        }

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
