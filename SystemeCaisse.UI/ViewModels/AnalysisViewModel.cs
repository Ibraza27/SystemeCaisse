using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.Core.Entities;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using SystemeCaisse.UI.Models;
using OfficeOpenXml;

namespace SystemeCaisse.UI.ViewModels
{
    public enum AnalysisType { Day, Hour, Weekday, Category }

    public partial class AnalysisViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private CancellationTokenSource? _cts;

        [ObservableProperty] private DateTime _startDate;
        [ObservableProperty] private DateTime _endDate;
        [ObservableProperty] private string _currentPeriodLabel = "Mois en cours";
        
        [ObservableProperty] private ObservableCollection<ProductAnalysisItem> _productAnalysis;
        [ObservableProperty] private ObservableCollection<CategoryAnalysisItem> _categoryAnalysis;
        [ObservableProperty] private ObservableCollection<TimeAnalysisItem> _timeAnalysis;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _isChartViewActive;
        partial void OnIsChartViewActiveChanged(bool value) => UpdateCharts();

        [ObservableProperty] private bool _isActive = false;
        partial void OnIsActiveChanged(bool value) { if(value) UpdateCharts(); }
        [ObservableProperty] private AnalysisType _currentType = AnalysisType.Day;
        partial void OnCurrentTypeChanged(AnalysisType value) => UpdateCharts();

        [ObservableProperty] private bool _isExporting;
        [ObservableProperty] private double _exportProgress;

        // Chart Data (v31: Using stable SimpleChart from Dashboard)
        [ObservableProperty] private ObservableCollection<LineDataPoint> _revenuePoints = new();
        [ObservableProperty] private ObservableCollection<LineDataPoint> _salesCountPoints = new();

        [ObservableProperty] private string _topChartTitle = "Évolution CA";
        [ObservableProperty] private string _bottomChartTitle = "Nombre de ventes";
        [ObservableProperty] private bool _isBottomChartVisible = true;

        private List<CategoryAnalysisItem> _lastCategories = new();
        private List<TimeAnalysisItem> _lastTemporal = new();
        private List<LigneVente> _lastLines = new();

        public AnalysisViewModel(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            OfficeOpenXml.ExcelPackage.License.SetNonCommercialOrganization("Individual");
            ProductAnalysis = new ObservableCollection<ProductAnalysisItem>();
            CategoryAnalysis = new ObservableCollection<CategoryAnalysisItem>();
            TimeAnalysis = new ObservableCollection<TimeAnalysisItem>();
            
            var now = DateTime.Now;
            StartDate = new DateTime(now.Year, now.Month, 1);
            EndDate = now.Date.AddDays(1).AddTicks(-1);
            
            _ = LoadAnalysis();
        }

        [RelayCommand]
        public void SetAnalysisType(string type)
        {
            if (Enum.TryParse<AnalysisType>(type, out var newType))
            {
                CurrentType = newType;
                UpdateCharts();
            }
        }

        [RelayCommand]
        public void SetPeriod(string period)
        {
            var today = DateTime.Today;
            switch (period)
            {
                case "Today":
                    StartDate = today;
                    EndDate = today.AddDays(1).AddTicks(-1);
                    CurrentPeriodLabel = "Aujourd'hui";
                    break;
                case "Yesterday":
                    StartDate = today.AddDays(-1);
                    EndDate = today.AddTicks(-1);
                    CurrentPeriodLabel = "Hier";
                    break;
                case "Month":
                    StartDate = new DateTime(today.Year, today.Month, 1);
                    EndDate = today.AddDays(1).AddTicks(-1);
                    CurrentPeriodLabel = "Ce mois";
                    break;
                case "Year":
                    StartDate = new DateTime(today.Year, 1, 1);
                    EndDate = today.AddDays(1).AddTicks(-1);
                    CurrentPeriodLabel = "Cette année";
                    break;
            }
            LoadAnalysisCommand.ExecuteAsync(null);
        }

        [RelayCommand]
        public async Task LoadAnalysis()
        {
            if (IsLoading) return;
            IsActive = true;
            if (!IsActive) return;

            _isDisposed = false;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            IsLoading = true;

            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => 
            {
                ProductAnalysis.Clear();
                CategoryAnalysis.Clear();
                TimeAnalysis.Clear();
                RevenuePoints.Clear();
                SalesCountPoints.Clear();
                _lastLines.Clear();
                _lastCategories.Clear();
                _lastTemporal.Clear();

                OnPropertyChanged(nameof(RevenuePoints));
                OnPropertyChanged(nameof(SalesCountPoints));
            }), System.Windows.Threading.DispatcherPriority.Background);

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync(token);
                if (!IsActive || token.IsCancellationRequested) return;

                var lines = await context.LignesVente.AsNoTracking()
                    .Include(l => l.Vente)
                    .Where(l => l.Vente != null && l.Vente.CreatedAt >= StartDate && l.Vente.CreatedAt <= EndDate)
                    .ToListAsync(token);
                
                if (!IsActive || token.IsCancellationRequested) return;
                    
                var products = await context.Produits.AsNoTracking().ToDictionaryAsync(p => p.Id, token);
                if (!IsActive || token.IsCancellationRequested) return;

                var productGroups = lines
                    .GroupBy(l => l.ProduitId)
                    .Select(g => 
                    {
                        var pid = g.Key;
                        var prod = (pid.HasValue && products.ContainsKey(pid.Value)) ? products[pid.Value] : null;
                        decimal qty = g.Sum(x => x.Quantite);
                        decimal revenue = g.Sum(x => x.TotalLigne);
                        decimal cost = prod != null ? prod.PrixAchat * qty : 0;
                        return new ProductAnalysisItem
                        {
                            ProductName = prod?.Nom ?? (g.FirstOrDefault()?.ProduitNom ?? "Inconnu"),
                            Category = !string.IsNullOrWhiteSpace(prod?.Categorie) ? prod.Categorie : "Autre",
                            QuantitySold = qty,
                            TotalRevenue = revenue,
                            TotalMargin = revenue - cost
                        };
                    })
                    .OrderByDescending(x => x.TotalRevenue)
                    .ToList();

                var catGroups = lines
                    .GroupBy(l => products.ContainsKey(l.ProduitId ?? 0) ? (!string.IsNullOrWhiteSpace(products[l.ProduitId ?? 0].Categorie) ? products[l.ProduitId ?? 0].Categorie : "Autre") : "Autre")
                    .Select(g => 
                    {
                        decimal revenue = g.Sum(x => x.TotalLigne);
                        decimal cost = g.Sum(x => (products.ContainsKey(x.ProduitId ?? 0) ? products[x.ProduitId ?? 0].PrixAchat : 0) * x.Quantite);
                        return new CategoryAnalysisItem
                        {
                            CategoryName = g.Key,
                            TotalRevenue = revenue,
                            TotalMargin = revenue - cost,
                            ItemsCount = g.Count(),
                            TotalQuantity = g.Sum(x => x.Quantite)
                        };
                    })
                    .OrderByDescending(x => x.TotalRevenue)
                    .ToList();

                var timeGroups = lines
                    .GroupBy(l => l.Vente?.CreatedAt.Date ?? DateTime.MinValue)
                    .Select(g => new TimeAnalysisItem
                    {
                        Date = g.Key,
                        TotalRevenue = g.Sum(x => x.TotalLigne),
                        TotalMargin = g.Sum(x => x.TotalLigne - (products.ContainsKey(x.ProduitId ?? 0) ? products[x.ProduitId ?? 0].PrixAchat * x.Quantite : 0)),
                        TicketsCount = g.Select(l => l.VenteId).Distinct().Count()
                    })
                    .OrderBy(x => x.Date)
                    .ToList();

                if (!IsActive) return;

                Application.Current.Dispatcher.BeginInvoke(new Action(() => 
                {
                    if (!IsActive || token.IsCancellationRequested) return;
                    ProductAnalysis.Clear();
                    foreach (var item in productGroups) ProductAnalysis.Add(item);
                    CategoryAnalysis.Clear();
                    foreach (var item in catGroups) CategoryAnalysis.Add(item);
                    TimeAnalysis.Clear();
                    foreach (var item in timeGroups) TimeAnalysis.Add(item);
                    PrepareCharts(catGroups, timeGroups, lines);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SILENT STABILITY: Analysis Error: {ex.Message}");
            }
            finally
            {
                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => IsLoading = false), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void PrepareCharts(List<CategoryAnalysisItem> categories, List<TimeAnalysisItem> temporal, List<LigneVente> lines)
        {
            _lastCategories = categories;
            _lastTemporal = temporal;
            _lastLines = lines;
            Application.Current.Dispatcher.BeginInvoke(new Action(() => UpdateCharts()), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateCharts()
        {
            if (!IsActive || _lastLines == null) return;

            var revPoints = new List<LineDataPoint>();
            var countPoints = new List<LineDataPoint>();

            var dayNames = new[] { "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam", "Dim" };
            var weekdayColors = new Brush[] 
            { 
                new SolidColorBrush(Color.FromRgb(129, 212, 199)), // Teal
                new SolidColorBrush(Color.FromRgb(255, 255, 180)), // Yellow
                new SolidColorBrush(Color.FromRgb(180, 180, 220)), // Lavender
                new SolidColorBrush(Color.FromRgb(255, 120, 110)), // Salmon
                new SolidColorBrush(Color.FromRgb(130, 180, 220)), // Blue
                new SolidColorBrush(Color.FromRgb(255, 180, 100)), // Orange
                new SolidColorBrush(Color.FromRgb(180, 230, 110))  // Lime
            };

            switch (CurrentType)
            {
                case AnalysisType.Day:
                    TopChartTitle = "Évolution journalière (CA)";
                    BottomChartTitle = "Nombre de ventes";
                    foreach(var t in _lastTemporal)
                    {
                        revPoints.Add(new LineDataPoint { Label = t.Date.ToString("dd"), Value = (double)t.TotalRevenue });
                        countPoints.Add(new LineDataPoint { Label = t.Date.ToString("dd"), Value = (double)t.TicketsCount, ColorBrush = new SolidColorBrush(Color.FromRgb(100, 100, 255)) }); // Blue for counts
                    }
                    break;

                case AnalysisType.Hour:
                    TopChartTitle = "Chiffre d'affaires par heure";
                    BottomChartTitle = "Nombre de ventes par heure";
                    var hourData = _lastLines
                        .GroupBy(l => l.Vente?.CreatedAt.Hour ?? 0)
                        .ToDictionary(g => g.Key, g => new { CA = g.Sum(x => x.TotalLigne), Count = g.Select(x => x.VenteId).Distinct().Count() });
                    for (int h = 0; h < 24; h++)
                    {
                        revPoints.Add(new LineDataPoint { Label = $"{h}h", Value = hourData.ContainsKey(h) ? (double)hourData[h].CA : 0.0 });
                        countPoints.Add(new LineDataPoint { Label = $"{h}h", Value = hourData.ContainsKey(h) ? (double)hourData[h].Count : 0.0, ColorBrush = new SolidColorBrush(Color.FromRgb(100, 100, 255)) });
                    }
                    break;

                case AnalysisType.Weekday:
                    TopChartTitle = "Performance par jour de la semaine";
                    BottomChartTitle = "";
                    var weekdayGroups = _lastLines
                        .GroupBy(l => ((int)l.Vente!.CreatedAt.DayOfWeek + 6) % 7)
                        .ToDictionary(g => g.Key, g => new { CA = g.Sum(x => x.TotalLigne), Count = g.Select(x => x.VenteId).Distinct().Count() });
                    for (int i = 0; i < 7; i++)
                    {
                        double ca = weekdayGroups.ContainsKey(i) ? (double)weekdayGroups[i].CA : 0;
                        int count = weekdayGroups.ContainsKey(i) ? weekdayGroups[i].Count : 0;
                        revPoints.Add(new LineDataPoint 
                        { 
                            Label = dayNames[i], 
                            Value = ca, 
                            SecondaryLabel = $"{count} ventes",
                            ColorBrush = weekdayColors[i] 
                        });
                    }
                    break;

                case AnalysisType.Category:
                    TopChartTitle = "CA par catégorie";
                    BottomChartTitle = "";
                    var topCategories = _lastCategories.Take(5).ToList();
                    foreach(var c in topCategories)
                    {
                        revPoints.Add(new LineDataPoint { Label = c.CategoryName, Value = (double)c.TotalRevenue });
                    }
                    break;
            }

            var newRevenuePoints = new ObservableCollection<LineDataPoint>();
            foreach (var p in revPoints) newRevenuePoints.Add(p);
            RevenuePoints = newRevenuePoints;

            var newSalesCountPoints = new ObservableCollection<LineDataPoint>();
            foreach (var p in countPoints) newSalesCountPoints.Add(p);
            SalesCountPoints = newSalesCountPoints;

            IsBottomChartVisible = !string.IsNullOrEmpty(BottomChartTitle);
        }

        private bool _isDisposed = false;

        public void Cleanup()
        {
            IsActive = false;
            _cts?.Cancel();
            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => 
            {
                System.Diagnostics.Debug.WriteLine("SILENT STABILITY: Analysis View Unbound.");
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        
        [RelayCommand]
        public async Task Export()
        {
            if (ProductAnalysis.Count == 0 && CategoryAnalysis.Count == 0) return;

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Fichiers Excel (*.xlsx)|*.xlsx",
                FileName = $"Rapport_Analyse_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.xlsx",
                Title = "Exporter le rapport d'analyse"
            };

            if (sfd.ShowDialog() == true)
            {
                var filePath = sfd.FileName;
                IsExporting = true;
                ExportProgress = 0;

                try
                {
                    await Task.Run(() =>
                    {
                        using (var package = new OfficeOpenXml.ExcelPackage())
                        {
                            // --- 1. SHEET: RÉSUMÉ ---
                            ExportProgress = 10;
                            var wsSummary = package.Workbook.Worksheets.Add("Résumé");
                            
                            wsSummary.Cells[1, 1].Value = "Rapport des ventes";
                            wsSummary.Cells[1, 1].Style.Font.Bold = true;
                            wsSummary.Cells[1, 1].Style.Font.Size = 20;

                            wsSummary.Cells[2, 1].Value = $"Du {StartDate:dd/MM/yyyy} au {EndDate:dd/MM/yyyy}";
                            wsSummary.Cells[2, 1].Style.Font.Bold = true;
                            wsSummary.Cells[2, 1].Style.Font.Size = 16;
                            
                            wsSummary.Cells[5, 1].Value = "Statistiques générales";
                            wsSummary.Cells[5, 1].Style.Font.Bold = true;
                            wsSummary.Cells[5, 1].Style.Font.Size = 14;

                            var totalRevenue = ProductAnalysis.Sum(x => x.TotalRevenue);
                            var totalSales = _lastTemporal.Sum(x => x.TicketsCount);
                            var totalItems = _lastLines.Sum(x => (double)x.Quantite);
                            var avgTicket = totalSales != 0 ? (double)totalRevenue / totalSales : 0;

                            wsSummary.Cells[7, 1].Value = "Indicateur";
                            wsSummary.Cells[7, 2].Value = "Valeur";
                            wsSummary.Cells[8, 1].Value = "Nombre de ventes";
                            wsSummary.Cells[8, 2].Value = totalSales;
                            wsSummary.Cells[9, 1].Value = "Chiffre d'affaires total";
                            wsSummary.Cells[9, 2].Value = (double)totalRevenue;
                            wsSummary.Cells[10, 1].Value = "Ticket moyen";
                            wsSummary.Cells[10, 2].Value = avgTicket;
                            wsSummary.Cells[11, 1].Value = "Nombre d'articles vendus";
                            wsSummary.Cells[11, 2].Value = totalItems;

                            using (var range = wsSummary.Cells[7, 1, 11, 2])
                            {
                                range.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                                range.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                                range.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                                range.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                            }

                            using (var headerRange = wsSummary.Cells[7, 1, 7, 2])
                            {
                                headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                                headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.ColorTranslator.FromHtml("#4472C4"));
                                headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);
                                headerRange.Style.Font.Bold = true;
                            }
                            wsSummary.Cells[9, 2].Style.Numberformat.Format = "#,##0.00 €";
                            wsSummary.Cells[10, 2].Style.Numberformat.Format = "#,##0.00 €";
                            wsSummary.Cells.AutoFitColumns();

                            // --- 2. SHEET: DÉTAIL DES VENTES ---
                            ExportProgress = 30;
                            var wsDetails = package.Workbook.Worksheets.Add("Détail des ventes");
                            var headerDetails = new[] { "N° Ticket", "Date/Heure", "Produit", "Quantité", "Prix unitaire", "Remise", "Total", "Mode paiement" };
                            for (int i = 0; i < headerDetails.Length; i++) wsDetails.Cells[1, i + 1].Value = headerDetails[i];

                            int row = 2;
                            var sortedLines = _lastLines.OrderByDescending(x => x.Vente?.CreatedAt).ToList();
                            foreach (var l in sortedLines)
                            {
                                wsDetails.Cells[row, 1].Value = l.Vente?.NumeroTicket ?? "";
                                wsDetails.Cells[row, 2].Value = l.Vente?.CreatedAt;
                                wsDetails.Cells[row, 2].Style.Numberformat.Format = "dd/MM/yyyy HH:mm";
                                wsDetails.Cells[row, 3].Value = l.ProduitNom;
                                wsDetails.Cells[row, 4].Value = (double)l.Quantite;
                                wsDetails.Cells[row, 5].Value = (double)l.PrixUnitaire;
                                wsDetails.Cells[row, 6].Value = (double)l.Remise;
                                wsDetails.Cells[row, 7].Value = (double)l.TotalLigne;
                                wsDetails.Cells[row, 8].Value = l.Vente?.MoyenPaiement ?? "";
                                row++;
                            }
                            var tableDetails = wsDetails.Tables.Add(wsDetails.Cells[1, 1, Math.Max(1, row - 1), 8], "TableDetails");
                            tableDetails.TableStyle = OfficeOpenXml.Table.TableStyles.Medium9;
                            wsDetails.Cells.AutoFitColumns();

                            // --- 3. SHEET: TOP PRODUITS ---
                            ExportProgress = 50;
                            var wsTop = package.Workbook.Worksheets.Add("Top produits");
                            var headerTop = new[] { "Rang", "Produit", "Quantité vendue", "CA généré", "% du CA total" };
                            for (int i = 0; i < headerTop.Length; i++) wsTop.Cells[1, i + 1].Value = headerTop[i];

                            row = 2;
                            int rank = 1;
                            foreach (var p in ProductAnalysis.OrderByDescending(x => x.TotalRevenue).Take(50))
                            {
                                wsTop.Cells[row, 1].Value = rank++;
                                wsTop.Cells[row, 2].Value = p.ProductName;
                                wsTop.Cells[row, 3].Value = (double)p.QuantitySold;
                                wsTop.Cells[row, 4].Value = (double)p.TotalRevenue;
                                wsTop.Cells[row, 5].Value = totalRevenue != 0 ? (double)(p.TotalRevenue / totalRevenue) : 0;
                                row++;
                            }
                            var tableTop = wsTop.Tables.Add(wsTop.Cells[1, 1, Math.Max(1, row - 1), 5], "TableTop");
                            tableTop.TableStyle = OfficeOpenXml.Table.TableStyles.Medium9;
                            wsTop.Cells[2, 5, Math.Max(2, row - 1), 5].Style.Numberformat.Format = "0.0%";
                            wsTop.Cells[2, 4, Math.Max(2, row - 1), 4].Style.Numberformat.Format = "#,##0.00 €";
                            wsTop.Cells.AutoFitColumns();

                            // --- 4. SHEET: GRAPHIQUES (Dynamic Chart) ---
                            ExportProgress = 70;
                            var wsGfx = package.Workbook.Worksheets.Add("Graphiques");
                            wsGfx.Cells[1, 1].Value = "Evolution du CA";
                            wsGfx.Cells[1, 1].Style.Font.Bold = true;
                            wsGfx.Cells[3, 1].Value = "Date";
                            wsGfx.Cells[3, 2].Value = "CA (€)";
                            wsGfx.Cells[3, 1, 3, 2].Style.Font.Bold = true;

                            row = 4;
                            var temporalData = _lastTemporal.OrderBy(x => x.Date).ToList();
                            foreach (var t in temporalData)
                            {
                                wsGfx.Cells[row, 1].Value = t.Date;
                                wsGfx.Cells[row, 1].Style.Numberformat.Format = "dd/MM/yyyy";
                                wsGfx.Cells[row, 2].Value = (double)t.TotalRevenue;
                                row++;
                            }
                            wsGfx.Cells[4, 2, Math.Max(4, row - 1), 2].Style.Numberformat.Format = "#,##0.00";
                            wsGfx.Cells.AutoFitColumns();

                            if (temporalData.Count >= 2)
                            {
                                var chartLines = wsGfx.Drawings.AddChart("EvolutionChart", OfficeOpenXml.Drawing.Chart.eChartType.Line);
                                chartLines.SetPosition(3, 0, 4, 0);
                                chartLines.SetSize(800, 400);
                                chartLines.Title.Text = "Evolution du chiffre d'affaires";
                                
                                var series = chartLines.Series.Add(wsGfx.Cells[4, 2, row - 1, 2], wsGfx.Cells[4, 1, row - 1, 1]);
                                series.Header = "CA (€)";
                            }

                            // --- 4b. Pie Chart: Répartition par catégorie ---
                            int pieDataStart = row + 2;
                            wsGfx.Cells[pieDataStart - 1, 1].Value = "CA par catégorie";
                            wsGfx.Cells[pieDataStart - 1, 1].Style.Font.Bold = true;
                            
                            int pieRow = pieDataStart;
                            foreach (var cat in CategoryAnalysis.OrderByDescending(x => x.TotalRevenue))
                            {
                                wsGfx.Cells[pieRow, 1].Value = cat.CategoryName;
                                wsGfx.Cells[pieRow, 2].Value = (double)cat.TotalRevenue;
                                pieRow++;
                            }

                            if (CategoryAnalysis.Count > 0)
                            {
                                var chartPie = wsGfx.Drawings.AddChart("CategoryChart", OfficeOpenXml.Drawing.Chart.eChartType.Pie);
                                chartPie.SetPosition(3, 0, 15, 0);
                                chartPie.SetSize(400, 400);
                                chartPie.Title.Text = "Répartition par catégorie";
                                
                                var seriesPie = chartPie.Series.Add(wsGfx.Cells[pieDataStart, 2, pieRow - 1, 2], wsGfx.Cells[pieDataStart, 1, pieRow - 1, 1]);
                            }

                            // --- 5. SHEET: ANALYSE TEMPORELLE ---
                            ExportProgress = 85;
                            var wsTime = package.Workbook.Worksheets.Add("Analyse temporelle");
                            wsTime.Cells[1, 1].Value = "Analyse par heure";
                            wsTime.Cells[1, 1].Style.Font.Bold = true;
                            var headerHour = new[] { "Heure", "Nombre de ventes", "CA total", "Ticket moyen" };
                            for (int i = 0; i < headerHour.Length; i++) wsTime.Cells[3, i + 1].Value = headerHour[i];
                            var hourData = _lastLines.GroupBy(l => l.Vente?.CreatedAt.Hour ?? 0).ToDictionary(g => g.Key, g => new { CA = g.Sum(x => x.TotalLigne), Count = g.Select(x => x.VenteId).Distinct().Count() });
                            for (int h = 0; h < 24; h++)
                            {
                                double ca = hourData.ContainsKey(h) ? (double)hourData[h].CA : 0;
                                int cnt = hourData.ContainsKey(h) ? hourData[h].Count : 0;
                                wsTime.Cells[4 + h, 1].Value = $"{h}h";
                                wsTime.Cells[4 + h, 2].Value = cnt;
                                wsTime.Cells[4 + h, 3].Value = ca;
                                wsTime.Cells[4 + h, 4].Value = cnt != 0 ? ca / cnt : 0;
                            }
                            using (var headerRange = wsTime.Cells[3, 1, 3, 4])
                            {
                                headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                                headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.ColorTranslator.FromHtml("#4472C4"));
                                headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);
                            }
                            wsTime.Cells[4, 3, 27, 4].Style.Numberformat.Format = "#,##0.00 €";

                            wsTime.Cells[1, 6].Value = "Analyse par jour de la semaine";
                            wsTime.Cells[1, 6].Style.Font.Bold = true;
                            var headerWeek = new[] { "Jour", "Nombre de ventes", "CA total", "CA moyen" };
                            for (int i = 0; i < headerWeek.Length; i++) wsTime.Cells[3, 6 + i].Value = headerWeek[i];
                            var dayNames = new[] { "Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi", "Dimanche" };
                            var weekdayGroups = _lastLines.GroupBy(l => ((int)l.Vente!.CreatedAt.DayOfWeek + 6) % 7).ToDictionary(g => g.Key, g => new { CA = g.Sum(x => x.TotalLigne), Count = g.Select(x => x.VenteId).Distinct().Count() });
                            for (int i = 0; i < 7; i++)
                            {
                                double ca = weekdayGroups.ContainsKey(i) ? (double)weekdayGroups[i].CA : 0;
                                int cnt = weekdayGroups.ContainsKey(i) ? weekdayGroups[i].Count : 0;
                                wsTime.Cells[4 + i, 6].Value = dayNames[i];
                                wsTime.Cells[4 + i, 7].Value = cnt;
                                wsTime.Cells[4 + i, 8].Value = ca;
                                wsTime.Cells[4 + i, 9].Value = cnt != 0 ? ca / cnt : 0;
                            }
                            using (var headerRange = wsTime.Cells[3, 6, 3, 9])
                            {
                                headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                                headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.ColorTranslator.FromHtml("#4472C4"));
                                headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);
                            }
                            wsTime.Cells[4, 8, 10, 9].Style.Numberformat.Format = "#,##0.00 €";
                            wsTime.Cells.AutoFitColumns();

                            // --- 6. SHEET: RENTABILITÉ ---
                            ExportProgress = 95;
                            var wsProfit = package.Workbook.Worksheets.Add("Rentabilité");
                            wsProfit.Cells[1, 1].Value = "Analyse de rentabilité par catégorie";
                            wsProfit.Cells[1, 1].Style.Font.Bold = true;
                            var headerProfit = new[] { "Catégorie", "Qté", "CA", "Coût", "Marge", "Taux" };
                            for (int i = 0; i < headerProfit.Length; i++) wsProfit.Cells[3, i + 1].Value = headerProfit[i];
                            row = 4;
                            foreach (var cat in CategoryAnalysis)
                            {
                                wsProfit.Cells[row, 1].Value = cat.CategoryName;
                                wsProfit.Cells[row, 2].Value = (double)cat.TotalQuantity;
                                wsProfit.Cells[row, 3].Value = (double)cat.TotalRevenue;
                                var cost = cat.TotalRevenue - cat.TotalMargin;
                                wsProfit.Cells[row, 4].Value = (double)cost;
                                wsProfit.Cells[row, 5].Value = (double)cat.TotalMargin;
                                wsProfit.Cells[row, 6].Value = cat.TotalRevenue != 0 ? (double)(cat.TotalMargin / cat.TotalRevenue) : 0;
                                row++;
                            }
                            var tableProfit = wsProfit.Tables.Add(wsProfit.Cells[3, 1, Math.Max(3, row - 1), 6], "TableProfit");
                            tableProfit.TableStyle = OfficeOpenXml.Table.TableStyles.Medium9;
                            wsProfit.Cells[4, 3, row - 1, 5].Style.Numberformat.Format = "#,##0.00 €";
                            wsProfit.Cells[4, 6, row - 1, 6].Style.Numberformat.Format = "0.0%";
                            wsProfit.Cells.AutoFitColumns();

                            package.SaveAs(new System.IO.FileInfo(filePath));
                            ExportProgress = 100;
                        }
                    });

                    if (MessageBox.Show($"Export réussi vers {filePath}\n\nSouhaitez-vous ouvrir le fichier ?", "Export Réussi", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de l'export : {ex.Message}", "Erreur d'Export", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsExporting = false;
                    ExportProgress = 0;
                }
            }
        }
    }

    public class ProductAnalysisItem
    {
        public string ProductName { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal QuantitySold { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal AveragePrice => QuantitySold != 0 ? TotalRevenue / QuantitySold : 0;
        public decimal TotalMargin { get; set; }
        public double MarginPercent => TotalRevenue != 0 ? (double)(TotalMargin / TotalRevenue) : 0;
    }

    public class CategoryAnalysisItem
    {
        public string CategoryName { get; set; } = "";
        public decimal TotalRevenue { get; set; }
        public decimal TotalMargin { get; set; }
        public decimal TotalQuantity { get; set; }
        public int ItemsCount { get; set; }
        public double MarginPercent => TotalRevenue != 0 ? (double)(TotalMargin / TotalRevenue) : 0;
    }

    public class TimeAnalysisItem
    {
        public DateTime Date { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalMargin { get; set; }
        public int TicketsCount { get; set; }
        public decimal AverageTicket => TicketsCount != 0 ? TotalRevenue / TicketsCount : 0;
    }
}
