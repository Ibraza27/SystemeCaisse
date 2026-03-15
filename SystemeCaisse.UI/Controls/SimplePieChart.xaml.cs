using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using SystemeCaisse.UI.Models;

namespace SystemeCaisse.UI.Controls
{
    // PieDataPoint is now in SystemeCaisse.UI.Models

    public partial class SimplePieChart : UserControl
    {
        public static readonly DependencyProperty DataPointsProperty =
            DependencyProperty.Register("DataPoints", typeof(IEnumerable<PieDataPoint>), typeof(SimplePieChart), 
                new PropertyMetadata(null, OnDataChanged));

        public static readonly DependencyProperty LegendItemsProperty =
            DependencyProperty.Register("LegendItems", typeof(IEnumerable<PieDataPoint>), typeof(SimplePieChart), new PropertyMetadata(null));

        public IEnumerable<PieDataPoint> DataPoints
        {
            get { return (IEnumerable<PieDataPoint>)GetValue(DataPointsProperty); }
            set { SetValue(DataPointsProperty, value); }
        }

        public IEnumerable<PieDataPoint> LegendItems
        {
            get { return (IEnumerable<PieDataPoint>)GetValue(LegendItemsProperty); }
            set { SetValue(LegendItemsProperty, value); }
        }

        public SimplePieChart()
        {
            InitializeComponent();
        }

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var chart = d as SimplePieChart;
            chart?.DrawChart();
        }

        private void DrawChart()
        {
            DrawingCanvas.Children.Clear();
            if (DataPoints == null || !DataPoints.Any()) return;

            double total = DataPoints.Sum(p => p.Value);
            if (total <= 0) return;

            // Recalculate percentages for legend convenience
            foreach (var p in DataPoints) p.Percentage = p.Value / total;
            LegendItems = DataPoints.ToList(); // Update legend binding

            double radius = 100;
            double centerX = 100;
            double centerY = 100;
            double currentAngle = -90; // Start at top

            foreach (var point in DataPoints)
            {
                double sweepAngle = (point.Value / total) * 360;
                
                // Avoid full circle glitch for single item
                if (sweepAngle >= 360) sweepAngle = 359.99;

                DrawSlice(centerX, centerY, radius, currentAngle, sweepAngle, point.ColorBrush);
                currentAngle += sweepAngle;
            }
            
            // Draw center hole (Donut)
            var hole = new Ellipse
            {
                Width = 120,
                Height = 120,
                Fill = Brushes.White,
                Stroke = Brushes.Transparent
            };
            Canvas.SetLeft(hole, centerX - 60);
            Canvas.SetTop(hole, centerY - 60);
            DrawingCanvas.Children.Add(hole);
        }

        private void DrawSlice(double cx, double cy, double r, double startAngle, double sweepAngle, Brush fill)
        {
            double radStart = (Math.PI / 180.0) * startAngle;
            double radEnd = (Math.PI / 180.0) * (startAngle + sweepAngle);

            double xStart = cx + Math.Cos(radStart) * r;
            double yStart = cy + Math.Sin(radStart) * r;

            double xEnd = cx + Math.Cos(radEnd) * r;
            double yEnd = cy + Math.Sin(radEnd) * r;

            var path = new Path
            {
                Fill = fill,
                Stroke = Brushes.White,
                StrokeThickness = 2
            };

            var geometry = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = new Point(cx, cy),
                IsClosed = true
            };

            figure.Segments.Add(new LineSegment(new Point(xStart, yStart), false));
            figure.Segments.Add(new ArcSegment(new Point(xEnd, yEnd), new Size(r, r), 0, sweepAngle > 180, SweepDirection.Clockwise, false));
            
            geometry.Figures.Add(figure);
            path.Data = geometry;

            DrawingCanvas.Children.Add(path);
        }
    }
}
