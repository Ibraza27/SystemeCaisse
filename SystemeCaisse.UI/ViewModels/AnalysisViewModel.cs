using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.Core.Entities;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using System.Collections.Generic;
using System.Globalization;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Threading;

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

        // Chart Data (LiveChartsCore v2)
        [ObservableProperty] private ObservableCollection<ISeries> _revenueSeries = new();
        [ObservableProperty] private ObservableCollection<ISeries> _salesCountSeries = new();

        [ObservableProperty] private ObservableCollection<Axis> _xAxes = new();
        [ObservableProperty] private ObservableCollection<Axis> _yAxes = new();
        [ObservableProperty] private string _topChartTitle = "Évolution CA";
        [ObservableProperty] private string _bottomChartTitle = "Nombre de ventes";
        [ObservableProperty] private bool _isBottomChartVisible = true;

        private List<CategoryAnalysisItem> _lastCategories = new();
        private List<TimeAnalysisItem> _lastTemporal = new();
        private List<LigneVente> _lastLines = new();

        public AnalysisViewModel(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            ProductAnalysis = new ObservableCollection<ProductAnalysisItem>();
            CategoryAnalysis = new ObservableCollection<CategoryAnalysisItem>();
            TimeAnalysis = new ObservableCollection<TimeAnalysisItem>();
            
            // Default to current month
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
                case "Week":
                    StartDate = today.AddDays(-6);
                    EndDate = today.AddDays(1).AddTicks(-1);
                    CurrentPeriodLabel = "7 derniers jours";
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
            // v28: PARANOID GUARD - Ensure we only load if the tab is REALLY active.
            // This prevents triggers that might have been queued before a rapid tab switch.
            if (!IsActive) return;

            _isDisposed = false;

            // Cancel any previous task
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            IsLoading = true;

            // CLEAR DATA HERE (At the start of loading)
            // This is safer than clearing during exit, as it avoids race conditions with LVC
            // v25: Clear existing data asynchronously to avoid blocking the transition
            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => 
            {
                ProductAnalysis.Clear();
                CategoryAnalysis.Clear();
                TimeAnalysis.Clear();
                RevenueSeries.Clear();
                SalesCountSeries.Clear();
                XAxes.Clear();
                YAxes.Clear();
                _lastLines.Clear();
                _lastCategories.Clear();
                _lastTemporal.Clear();

                OnPropertyChanged(nameof(RevenueSeries));
                OnPropertyChanged(nameof(SalesCountSeries));
                OnPropertyChanged(nameof(XAxes));
                OnPropertyChanged(nameof(YAxes));
            }), System.Windows.Threading.DispatcherPriority.Background);
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync(token);
                if (!IsActive || token.IsCancellationRequested) return;

                // 1. Fetch Sales Lines in Period (Include Vente for Date/Time grouping)
                var lines = await context.LignesVente.AsNoTracking()
                    .Include(l => l.Vente)
                    .Where(l => l.Vente != null && l.Vente.CreatedAt >= StartDate && l.Vente.CreatedAt <= EndDate)
                    .ToListAsync(token);
                
                if (!IsActive || token.IsCancellationRequested) return;
                    
                // 2. Fetch all products to get Categories and Cost Prices
                var products = await context.Produits.AsNoTracking().ToDictionaryAsync(p => p.Id, token);
                if (!IsActive || token.IsCancellationRequested) return;

                // 3. Group and Calculate (Products)
                var productGroups = lines
                    .GroupBy(l => l.ProduitId)
                    .Select(g => 
                    {
                        var pid = g.Key;
                        var prod = (pid.HasValue && products.ContainsKey(pid.Value)) ? products[pid.Value] : null;
                        
                        decimal qty = g.Sum(x => x.Quantite);
                        decimal revenue = g.Sum(x => x.TotalLigne);
                        decimal cost = prod != null ? prod.PrixAchat * qty : 0;
                        decimal margin = revenue - cost;
                        
                        return new ProductAnalysisItem
                        {
                            ProductName = prod?.Nom ?? (g.FirstOrDefault()?.ProduitNom ?? "Inconnu"),
                            Category = prod?.Categorie ?? "Divers",
                            QuantitySold = qty,
                            TotalRevenue = revenue,
                            TotalMargin = margin
                        };
                    })
                    .OrderByDescending(x => x.TotalRevenue)
                    .ToList();

                if (!IsActive) return;

                // 4. Group and Calculate (Categories)
                var catGroups = lines
                    .GroupBy(l => products.ContainsKey(l.ProduitId ?? 0) ? products[l.ProduitId ?? 0].Categorie ?? "Divers" : "Divers")
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

                if (!IsActive) return;

                // 5. Group and Calculate (Temporal)
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

                // v25: Load new data asynchronously at background priority
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
                // v23: Silent Stability. We log instead of showing a MessageBox to avoid modal loops.
                System.Diagnostics.Debug.WriteLine($"SILENT STABILITY: Analysis Error: {ex.Message}");
                Application.Current.Dispatcher.BeginInvoke(new Action(() => 
                {
                    IsLoading = false;
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            finally
            {
                // v27: Ensure IsLoading is reset asynchronously
                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => IsLoading = false), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void PrepareCharts(List<CategoryAnalysisItem> categories, List<TimeAnalysisItem> temporal, List<LigneVente> lines)
        {
            _lastCategories = categories;
            _lastTemporal = temporal;
            _lastLines = lines;
            
            // Run on UI thread to ensure collections are updated safely (v27: Non-blocking)
            Application.Current.Dispatcher.BeginInvoke(new Action(() => UpdateCharts()), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateCharts()
        {
            if (_isDisposed || !IsActive || _lastLines == null) return;

            // Run on UI thread to avoid cross-thread exceptions with ObservableCollection
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => UpdateCharts()), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            // Palette de couleurs premium
            var catColors = new SKColor[] 
            { 
                SKColor.Parse("#81D4FA"), SKColor.Parse("#FFF59D"), SKColor.Parse("#C5CAE9"), 
                SKColor.Parse("#EF9A9A"), SKColor.Parse("#A5D6A7") 
            };

            // Use robust CLEAR/ADD pattern instead of instance replacement
            // This ensures LiveCharts doesn't lose the binding reference
            var revSeries = new List<ISeries>();
            var countSeries = new List<ISeries>();
            var axesX = new List<Axis>();
            var axesY = new List<Axis>();

            switch (CurrentType)
            {
                case AnalysisType.Day:
                    TopChartTitle = "Évolution journalière (CA)";
                    BottomChartTitle = "Nombre de ventes";
                    
                    revSeries.Add(new LineSeries<double>
                    {
                        Name = "CA (€)",
                        Values = _lastTemporal.Select(t => (double)t.TotalRevenue).ToArray(),
                        GeometrySize = 8,
                        GeometryFill = new SolidColorPaint(SKColors.White),
                        GeometryStroke = new SolidColorPaint(SKColor.Parse("#2E7D32"), 3),
                        Stroke = new SolidColorPaint(SKColor.Parse("#2E7D32"), 3),
                        Fill = new SolidColorPaint(SKColor.Parse("#A5D6A7").WithAlpha(100)),
                        LineSmoothness = 0.5
                    });

                    countSeries.Add(new ColumnSeries<double>
                    {
                        Name = "Ventes",
                        Values = _lastTemporal.Select(t => (double)t.TicketsCount).ToArray(),
                        Fill = new SolidColorPaint(SKColor.Parse("#5C6BC0")), // Bleu-Violet
                        Stroke = null
                    });

                    axesX.Add(new Axis 
                    { 
                        Labels = _lastTemporal.Select(t => t.Date.ToString("dd")).ToArray(),
                        LabelsRotation = 45 
                    });
                    break;

                case AnalysisType.Hour:
                    TopChartTitle = "Chiffre d'affaires par heure";
                    BottomChartTitle = "Nombre de ventes par heure";

                    var hourData = _lastLines
                        .GroupBy(l => l.Vente?.CreatedAt.Hour ?? 0)
                        .ToDictionary(g => g.Key, g => new { CA = g.Sum(x => x.TotalLigne), Count = g.Select(x => x.VenteId).Distinct().Count() });

                    var hours = Enumerable.Range(0, 24).ToList();

                    revSeries.Add(new ColumnSeries<double>
                    {
                        Name = "CA (€)",
                        Values = hours.Select(h => hourData.ContainsKey(h) ? (double)hourData[h].CA : 0.0).ToArray(),
                        Fill = new SolidColorPaint(SKColor.Parse("#4CAF50")),
                        Padding = 2
                    });

                    countSeries.Add(new ColumnSeries<double>
                    {
                        Name = "Ventes",
                        Values = hours.Select(h => hourData.ContainsKey(h) ? (double)hourData[h].Count : 0.0).ToArray(),
                        Fill = new SolidColorPaint(SKColor.Parse("#5C6BC0")),
                        Padding = 2
                    });

                    axesX.Add(new Axis { 
                        Labels = hours.Select(h => h.ToString()).ToArray(), 
                        Name = "Heure",
                        SeparatorsPaint = new SolidColorPaint(SKColors.LightGray.WithAlpha(50)) { StrokeThickness = 1 }
                    });
                    break;

                case AnalysisType.Weekday:
                    TopChartTitle = "Performance par jour de la semaine";
                    BottomChartTitle = ""; // Pas de graphe du bas pour cette vue
                    
                    var dayNames = new[] { "Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi", "Dimanche" };
                    var weekdayGroups = _lastLines
                        .GroupBy(l => ((int)l.Vente!.CreatedAt.DayOfWeek + 6) % 7) // 0 = Lundi, 6 = Dimanche
                        .ToDictionary(g => g.Key, g => new { CA = g.Sum(x => x.TotalLigne), Count = g.Select(x => x.VenteId).Distinct().Count() });

                    var weekdayCA = new double[7];
                    var weekdayCounts = new int[7];
                    for (int i = 0; i < 7; i++)
                    {
                        weekdayCA[i] = weekdayGroups.ContainsKey(i) ? (double)weekdayGroups[i].CA : 0;
                        weekdayCounts[i] = weekdayGroups.ContainsKey(i) ? weekdayGroups[i].Count : 0;
                    }

                    revSeries.Add(new ColumnSeries<double>
                    {
                        Name = "CA Moyen par jour",
                        Values = weekdayCA,
                        Fill = new SolidColorPaint(SKColor.Parse("#2E7D32")), // Vert premium
                        DataLabelsPaint = new SolidColorPaint(SKColors.Black),
                        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                        DataLabelsFormatter = point => {
                            if (point == null) return "";
                            var idx = (int)point.Coordinate.SecondaryValue;
                            if (idx < 0 || idx >= weekdayCounts.Length) return "";
                            var count = weekdayCounts[idx];
                            var avg = count > 0 ? point.Coordinate.PrimaryValue / count : 0;
                            return count > 0 ? $"{avg:N0}€\n({count} v.)" : "";
                        },
                        MaxBarWidth = 60,
                        Padding = 20
                    });

                    axesX.Add(new Axis { Labels = dayNames, Name = "Jour de la semaine" });
                    break;

                case AnalysisType.Category:
                    TopChartTitle = "CA par catégorie";
                    BottomChartTitle = "";
                    
                    var topCategories = _lastCategories.Take(5).ToList();
                    revSeries.Add(new ColumnSeries<double>
                    {
                        Name = "CA (€)",
                        Values = topCategories.Select(c => (double)c.TotalRevenue).ToArray(),
                        Fill = new SolidColorPaint(SKColor.Parse("#1976D2")), // Bleu premium
                        DataLabelsPaint = new SolidColorPaint(SKColors.Black),
                        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                        DataLabelsFormatter = point => {
                            if (point == null) return "";
                            return $"{point.Coordinate.PrimaryValue:N0}€";
                        },
                        MaxBarWidth = 80,
                        Padding = 30
                    });
                    
                    axesX.Add(new Axis { 
                        Labels = topCategories.Select(c => c.CategoryName).ToArray(), 
                        LabelsRotation = 15 // Moins de rotation pour plus de lisibilité
                    });
                    break;
            }

            // Sync collections
            RevenueSeries.Clear();
            foreach (var s in revSeries) RevenueSeries.Add(s);

            SalesCountSeries.Clear();
            foreach (var s in countSeries) SalesCountSeries.Add(s);

            XAxes.Clear();
            foreach (var a in axesX) XAxes.Add(a);

            // Add Y Axis with grid lines for detail
            YAxes.Clear();
            YAxes.Add(new Axis
            {
                SeparatorsPaint = new SolidColorPaint(SKColors.LightGray.WithAlpha(80)) { StrokeThickness = 1 },
                Labeler = value => value.ToString("N0") + " €"
            });

            IsBottomChartVisible = !string.IsNullOrEmpty(BottomChartTitle);
            
            // Force property changed notification for the collections to ensure LiveCharts updates
            OnPropertyChanged(nameof(RevenueSeries));
            OnPropertyChanged(nameof(SalesCountSeries));
            OnPropertyChanged(nameof(XAxes));
            OnPropertyChanged(nameof(YAxes));
        }

        private bool _isDisposed = false;

        public void Cleanup()
        {
            IsActive = false;
            _cts?.Cancel();

            // v30: PASSIVE CLEANUP
            // We no longer clear collections immediately. Clearing triggers collection changed events,
            // which forces WPF to recalculate layouts exactly when the tab is hiding.
            // This was a primary cause of transition deadlocks. We leave the data for the GC to handle later.

            // v27: Defer the silent log
            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => 
            {
                System.Diagnostics.Debug.WriteLine("SILENT STABILITY: Analysis View Unbound.");
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        
        [Obsolete("Use Cleanup instead")]
        public void ClearCharts() => Cleanup();
        [RelayCommand]
        public async Task Export()
        {
            if (ProductAnalysis.Count == 0 && CategoryAnalysis.Count == 0)
            {
                MessageBox.Show("Aucune donnée à exporter.");
                return;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Fichiers Excel CSV (*.csv)|*.csv",
                FileName = $"Rapport_Analyse_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.csv",
                Title = "Exporter le rapport d'analyse"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new System.IO.StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                    {
                        // 1. Metadata / Header
                        writer.WriteLine("RAPPORT D'ANALYSE DES VENTES");
                        writer.WriteLine($"Période:;{StartDate:dd/MM/yyyy}; au ;{EndDate:dd/MM/yyyy}");
                        writer.WriteLine($"Généré le:;{DateTime.Now:dd/MM/yyyy HH:mm}");
                        writer.WriteLine("");

                        // 2. Section Produits
                        writer.WriteLine("--- ANALYSE PAR PRODUIT ---");
                        writer.WriteLine("Produit;Catégorie;Quantité;CA Généré;Prix Moyen;Marge Totale;% Marge");
                        foreach (var item in ProductAnalysis)
                        {
                            writer.WriteLine($"{item.ProductName};{item.Category};{item.QuantitySold:N2};{item.TotalRevenue:N2};{item.AveragePrice:N2};{item.TotalMargin:N2};{item.MarginPercent:P1}");
                        }
                        writer.WriteLine("");

                        // 3. Section Catégories
                        writer.WriteLine("--- ANALYSE PAR CATEGORIE ---");
                        writer.WriteLine("Catégorie;Nb Articles;Qté Totale;CA Global;Marge Globale;% Marge");
                        foreach (var item in CategoryAnalysis)
                        {
                            writer.WriteLine($"{item.CategoryName};{item.ItemsCount};{item.TotalQuantity:N2};{item.TotalRevenue:N2};{item.TotalMargin:N2};{item.MarginPercent:P1}");
                        }
                        writer.WriteLine("");

                        // 4. Section Temporelle
                        writer.WriteLine("--- EVOLUTION TEMPORELLE ---");
                        writer.WriteLine("Date;Nb Tickets;CA Journée;Panier Moyen;Marge Journée");
                        foreach (var item in TimeAnalysis)
                        {
                            writer.WriteLine($"{item.Date:dd/MM/yyyy};{item.TicketsCount};{item.TotalRevenue:N2};{item.AverageTicket:N2};{item.TotalMargin:N2}");
                        }
                    }

                    MessageBox.Show($"Export réussi vers {sfd.FileName}", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de l'export : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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
