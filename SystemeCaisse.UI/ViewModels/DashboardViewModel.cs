using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.UI.Controls;
using SystemeCaisse.UI.Models;

namespace SystemeCaisse.UI.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private string _selectedPeriodKey = "Month";

        // --- Filters ---
        [ObservableProperty] private DateTime _startDate;
        [ObservableProperty] private DateTime _endDate;
        [ObservableProperty] private string _currentPeriodLabel = "Ce mois";

        // --- KPI Properties (String for display) ---
        [ObservableProperty] private string _salesTotal = "0,00 €";
        [ObservableProperty] private string _salesTrend = "0%";
        [ObservableProperty] private bool _salesTrendPositive = true;

        [ObservableProperty] private string _marginTotal = "0,00 €";
        [ObservableProperty] private string _marginTrend = "0%";
        [ObservableProperty] private bool _marginTrendPositive = true;

        [ObservableProperty] private string _txCount = "0";
        [ObservableProperty] private string _txTrend = "0%";
        [ObservableProperty] private bool _txTrendPositive = true;

        [ObservableProperty] private string _avgBasket = "0,00 €";
        [ObservableProperty] private string _avgBasketTrend = "0%";
        [ObservableProperty] private bool _avgBasketTrendPositive = true;

        // --- Charts Data ---
        [ObservableProperty] private ObservableCollection<LineDataPoint> _salesEvolution;
        [ObservableProperty] private ObservableCollection<PieDataPoint> _categoryDistribution;
        [ObservableProperty] private ObservableCollection<TopProductItem> _topProducts;

        // Brushes helper
        private readonly Brush[] _pieColors = new Brush[]
        {
            new SolidColorBrush(Color.FromRgb(46, 204, 113)),  // Green
            new SolidColorBrush(Color.FromRgb(52, 152, 219)),  // Blue
            new SolidColorBrush(Color.FromRgb(155, 89, 182)),  // Purple
            new SolidColorBrush(Color.FromRgb(241, 196, 15)),  // Yellow
            new SolidColorBrush(Color.FromRgb(230, 126, 34)),  // Orange
            new SolidColorBrush(Color.FromRgb(231, 76, 60)),   // Red
            new SolidColorBrush(Color.FromRgb(149, 165, 166))  // Grey
        };

        public DashboardViewModel(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            SalesEvolution = new ObservableCollection<LineDataPoint>();
            CategoryDistribution = new ObservableCollection<PieDataPoint>();
            TopProducts = new ObservableCollection<TopProductItem>();
        }

        public async Task InitializeAsync()
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SetPeriod("Month");
            });
            await LoadDashboardDataInternalAsync();
        }

        [RelayCommand]
        public void SetPeriod(string period)
        {
            _selectedPeriodKey = period ?? "Month";
            var today = DateTime.Today;
            switch (_selectedPeriodKey)
            {
                case "Today":
                    StartDate = today;
                    EndDate = today;
                    CurrentPeriodLabel = "Aujourd'hui";
                    break;
                case "Yesterday":
                    StartDate = today.AddDays(-1);
                    EndDate = today.AddDays(-1);
                    CurrentPeriodLabel = "Hier";
                    break;
                case "Week":
                    StartDate = today.AddDays(-6);
                    EndDate = today;
                    CurrentPeriodLabel = "7 derniers jours";
                    break;
                case "Month":
                    StartDate = new DateTime(today.Year, today.Month, 1);
                    EndDate = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
                    CurrentPeriodLabel = "Ce mois";
                    break;
                case "Year":
                    StartDate = new DateTime(today.Year, 1, 1);
                    EndDate = new DateTime(today.Year, 12, 31);
                    CurrentPeriodLabel = "Cette année";
                    break;
            }
            _ = LoadDashboardDataInternalAsync();
        }

        [RelayCommand]
        public async Task LoadDashboardData() => await LoadDashboardDataInternalAsync();

        public async Task LoadDashboardDataInternalAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Always normalize start/end bounds to full days so DatePicker TwoWay bindings
                // stripping the time portion of EndDate never exclude sales from the end date.
                var startBound = StartDate.Date;
                var endBound = EndDate.Date.AddDays(1).AddTicks(-1);
                if (endBound < startBound)
                {
                    endBound = startBound.AddDays(1).AddTicks(-1);
                }

                // 1. Fetch Current Period Data
                var sales = await context.Ventes
                    .AsNoTracking()
                    .Where(v => v.CreatedAt >= startBound && v.CreatedAt <= endBound)
                    .Include(v => v.Lignes)
                    .ToListAsync();

                // 2. Fetch Previous Period Data (for Trends)
                DateTime prevStart;
                DateTime prevEnd;
                if (_selectedPeriodKey == "Month" && startBound.Day == 1)
                {
                    prevStart = startBound.AddMonths(-1);
                    prevEnd = startBound.AddTicks(-1);
                }
                else if (_selectedPeriodKey == "Year" && startBound.DayOfYear == 1)
                {
                    prevStart = startBound.AddYears(-1);
                    prevEnd = startBound.AddTicks(-1);
                }
                else
                {
                    var duration = endBound - startBound;
                    prevStart = startBound.Subtract(duration).AddTicks(-1);
                    prevEnd = startBound.AddTicks(-1);
                }

                var prevSales = await context.Ventes
                    .AsNoTracking()
                    .Where(v => v.CreatedAt >= prevStart && v.CreatedAt <= prevEnd)
                    .ToListAsync();

                // Set HasData BEFORE populating chart collections so the chart container is Visible
                // and has a valid ActualWidth when SalesEvolution notifies the UI.
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    HasData = sales.Any();
                });

                // --- Calculate KPIs ---
                CalculateKpis(sales, prevSales);

                // --- Calculate Charts ---
                await CalculateCharts(sales, startBound, endBound, context);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dashboard Error: {ex.Message}\nStack: {ex.StackTrace}");
            }
        }

        [ObservableProperty] private bool _hasData;
        [ObservableProperty] private bool _isActive;
        partial void OnIsActiveChanged(bool value) { if (value) _ = LoadDashboardDataInternalAsync(); }

        private void CalculateKpis(List<Core.Entities.Vente> current, List<Core.Entities.Vente> previous)
        {
            // Total Sales
            decimal curTotal = current.Sum(v => v.Total);
            decimal prevTotal = previous.Sum(v => v.Total);
            SalesTotal = $"{curTotal:C}";
            SetTrend(curTotal, prevTotal, s => SalesTrend = s, b => SalesTrendPositive = b);

            // Transactions
            int curCount = current.Count;
            int prevCount = previous.Count;
            TxCount = curCount.ToString();
            SetTrend(curCount, prevCount, s => TxTrend = s, b => TxTrendPositive = b);

            // Average Basket
            decimal curAvg = curCount > 0 ? curTotal / curCount : 0;
            decimal prevAvg = prevCount > 0 ? prevTotal / prevCount : 0;
            AvgBasket = $"{curAvg:C}";
            SetTrend(curAvg, prevAvg, s => AvgBasketTrend = s, b => AvgBasketTrendPositive = b);
        }

        private void SetTrend(decimal current, decimal previous, Action<string> SetLabel, Action<bool> SetPositive)
        {
            if (previous == 0)
            {
                SetLabel(current > 0 ? "+100%" : "0%");
                SetPositive(current >= 0);
                return;
            }
            decimal change = ((current - previous) / previous) * 100;
            SetLabel($"{(change > 0 ? "+" : "")}{change:F1}%");
            SetPositive(change >= 0);
        }

        private async Task CalculateCharts(List<Core.Entities.Vente> sales, DateTime startBound, DateTime endBound, AppDbContext context)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CategoryDistribution.Clear();
                TopProducts.Clear();
            });

            // --- 1. Line Chart (Evolution du Chiffre d'Affaires) ---
            var newSalesEvolution = new ObservableCollection<LineDataPoint>();
            int totalDays = (int)Math.Round((endBound.Date - startBound.Date).TotalDays) + 1;
            bool isSingleDay = totalDays <= 1 && _selectedPeriodKey != "Month" && _selectedPeriodKey != "Year";

            if (isSingleDay)
            {
                // Hourly breakdown for a single day (e.g. Today / Yesterday)
                var salesByHour = sales
                    .GroupBy(v => v.CreatedAt.Hour)
                    .ToDictionary(g => g.Key, g => (double)g.Sum(v => v.Total));

                int minHour = salesByHour.Count > 0 ? Math.Min(8, salesByHour.Keys.Min()) : 8;
                int maxHour = salesByHour.Count > 0 ? Math.Max(20, salesByHour.Keys.Max()) : 20;

                for (int h = minHour; h <= maxHour; h++)
                {
                    salesByHour.TryGetValue(h, out double total);
                    newSalesEvolution.Add(new LineDataPoint
                    {
                        Label = $"{h:00}h",
                        Value = total
                    });
                }
            }
            else if (totalDays <= 62)
            {
                // Daily breakdown for Week, Month, or custom multi-day period <= 2 months
                var salesByDate = sales
                    .GroupBy(v => v.CreatedAt.Date)
                    .ToDictionary(g => g.Key, g => (double)g.Sum(v => v.Total));

                DateTime endDay = endBound.Date;
                if (_selectedPeriodKey == "Month" && endDay <= startBound.Date)
                {
                    endDay = new DateTime(startBound.Year, startBound.Month, DateTime.DaysInMonth(startBound.Year, startBound.Month));
                }

                for (var day = startBound.Date; day <= endDay; day = day.AddDays(1))
                {
                    salesByDate.TryGetValue(day, out double total);
                    newSalesEvolution.Add(new LineDataPoint
                    {
                        Label = day.ToString("dd/MM", CultureInfo.InvariantCulture),
                        Value = total
                    });
                }
            }
            else
            {
                // Monthly breakdown for Year or long custom periods (> 62 days)
                var salesByMonth = sales
                    .GroupBy(v => new { v.CreatedAt.Year, v.CreatedAt.Month })
                    .ToDictionary(g => (g.Key.Year, g.Key.Month), g => (double)g.Sum(v => v.Total));

                var startMonth = new DateTime(startBound.Year, startBound.Month, 1);
                var endMonth = new DateTime(endBound.Year, endBound.Month, 1);
                if (endMonth <= startMonth)
                {
                    endMonth = new DateTime(startBound.Year, 12, 1);
                }

                var frCulture = CultureInfo.GetCultureInfo("fr-FR");
                for (var m = startMonth; m <= endMonth; m = m.AddMonths(1))
                {
                    salesByMonth.TryGetValue((m.Year, m.Month), out double total);
                    newSalesEvolution.Add(new LineDataPoint
                    {
                        Label = m.ToString("MMM", frCulture),
                        Value = total
                    });
                }
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SalesEvolution = newSalesEvolution;
            });

            // --- 2. Pie Chart (Categories) ---
            var flatLines = sales.SelectMany(v => v.Lignes).ToList();

            var productIds = flatLines.Select(l => l.ProduitId).Distinct().ToList();
            var productInfo = await context.Produits
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => new
                {
                    Category = !string.IsNullOrWhiteSpace(p.Categorie) ? p.Categorie : "Autre",
                    p.ImagePath
                });

            var catGroups = flatLines
                .GroupBy(l => (l.ProduitId.HasValue && productInfo.ContainsKey(l.ProduitId.Value))
                                ? productInfo[l.ProduitId.Value].Category
                                : (!string.IsNullOrWhiteSpace(l.CategorieNom) ? l.CategorieNom : "Autre"))
                .Select(g => new { Cat = g.Key, Total = g.Sum(l => l.TotalLigne) })
                .OrderByDescending(x => x.Total)
                .ToList();

            var newCategoryDistribution = new ObservableCollection<PieDataPoint>();
            int colorIdx = 0;
            foreach (var item in catGroups)
            {
                newCategoryDistribution.Add(new PieDataPoint
                {
                    Label = item.Cat,
                    Value = (double)item.Total,
                    ColorBrush = _pieColors[colorIdx % _pieColors.Length]
                });
                colorIdx++;
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CategoryDistribution = newCategoryDistribution;
            });

            // --- 3. Top Products Table ---
            var topItems = flatLines
                .GroupBy(l => new { l.ProduitNom, l.ProduitId })
                .Select(g => new TopProductItem
                {
                    ProductName = g.Key.ProduitNom,
                    Quantity = g.Sum(x => x.Quantite),
                    TotalRevenue = g.Sum(x => x.TotalLigne),
                    ImagePath = (g.Key.ProduitId.HasValue && productInfo.ContainsKey(g.Key.ProduitId.Value))
                                ? productInfo[g.Key.ProduitId.Value].ImagePath : null
                })
                .OrderByDescending(x => x.TotalRevenue)
                .Take(5)
                .ToList();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var item in topItems) TopProducts.Add(item);
            });
        }
    }
}
