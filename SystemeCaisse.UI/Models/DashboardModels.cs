using System;
using System.Windows.Media;

namespace SystemeCaisse.UI.Models
{
    public class TopProductItem
    {
        public string ProductName { get; set; }
        public decimal Quantity { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class SalesDataPoint
    {
        public DateTime Date { get; set; }
        public string Label { get; set; }
        public decimal Total { get; set; }
        public double BarHeight { get; set; } // Pixel height for chart (Max ~150)
    }

    public class LineDataPoint
    {
        public string Label { get; set; }
        public double Value { get; set; }
        public System.Windows.Media.Brush? ColorBrush { get; set; }
        public string? SecondaryLabel { get; set; }
    }

    public class PieDataPoint
    {
        public string Label { get; set; }
        public double Value { get; set; }
        public System.Windows.Media.Brush ColorBrush { get; set; }
        public double Percentage { get; set; }
    }
}
