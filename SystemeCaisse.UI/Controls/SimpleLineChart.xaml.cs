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
    // LineDataPoint is now in SystemeCaisse.UI.Models

    public partial class SimpleLineChart : UserControl
    {
        public static readonly DependencyProperty DataPointsProperty =
            DependencyProperty.Register("DataPoints", typeof(IEnumerable<LineDataPoint>), typeof(SimpleLineChart),
                new PropertyMetadata(null, OnDataChanged));

        public static readonly DependencyProperty LineColorProperty =
            DependencyProperty.Register("LineColor", typeof(Brush), typeof(SimpleLineChart), new PropertyMetadata(Brushes.Blue));

        public static readonly DependencyProperty AreaBrushProperty =
            DependencyProperty.Register("AreaBrush", typeof(Brush), typeof(SimpleLineChart), new PropertyMetadata(null));

        public static readonly DependencyProperty PointBrushProperty =
            DependencyProperty.Register("PointBrush", typeof(Brush), typeof(SimpleLineChart), new PropertyMetadata(Brushes.White));

        public IEnumerable<LineDataPoint> DataPoints
        {
            get { return (IEnumerable<LineDataPoint>)GetValue(DataPointsProperty); }
            set { SetValue(DataPointsProperty, value); }
        }

        public Brush LineColor
        {
            get { return (Brush)GetValue(LineColorProperty); }
            set { SetValue(LineColorProperty, value); }
        }

        public Brush AreaBrush
        {
            get { return (Brush)GetValue(AreaBrushProperty); }
            set { SetValue(AreaBrushProperty, value); }
        }

        public Brush PointBrush
        {
            get { return (Brush)GetValue(PointBrushProperty); }
            set { SetValue(PointBrushProperty, value); }
        }

        public SimpleLineChart()
        {
            InitializeComponent();
        }

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var chart = d as SimpleLineChart;
            chart?.DrawChart();
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawChart();
        }

        private void DrawChart()
        {
            ChartCanvas.Children.Clear();
            AxisXCanvas.Children.Clear();
            AxisYCanvas.Children.Clear();

            if (DataPoints == null || !DataPoints.Any() || ChartCanvas.ActualWidth <= 0 || ChartCanvas.ActualHeight <= 0) return;

            var points = DataPoints.ToList();
            double maxVal = points.Max(p => p.Value);
            double minVal = 0; 
            if (maxVal == 0) maxVal = 100; 

            double width = ChartCanvas.ActualWidth;
            double height = ChartCanvas.ActualHeight - 20; // 20px padding at top
            double stepX = width / (points.Count - 1 > 0 ? points.Count - 1 : 1);

            var polyline = new Polyline
            {
                Stroke = LineColor,
                StrokeThickness = 2.5,
                Points = new PointCollection()
            };

            // Grid lines (Thinner and more subtle)
            for (int j = 0; j <= 4; j++)
            {
                double yLine = (height + 20) - (j * (height / 4));
                var line = new Line 
                { 
                    X1 = 0, Y1 = yLine, X2 = width, Y2 = yLine, 
                    Stroke = new SolidColorBrush(Color.FromRgb(240, 240, 240)), 
                    StrokeThickness = 1
                };
                ChartCanvas.Children.Add(line);
            }

            // Draw Area fill FIRST
            if (AreaBrush != null)
            {
                var areaPolygon = new Polygon
                {
                    Fill = AreaBrush,
                    Opacity = 0.3,
                    Points = new PointCollection()
                };
                areaPolygon.Points.Add(new Point(0, height + 20));
                for (int i = 0; i < points.Count; i++)
                {
                    double x = i * stepX;
                    double y = (height + 20) - ((points[i].Value / maxVal) * height);
                    areaPolygon.Points.Add(new Point(x, y));
                }
                areaPolygon.Points.Add(new Point(width, height + 20));
                ChartCanvas.Children.Add(areaPolygon);
            }

            // Draw X Labels & Points
            for (int i = 0; i < points.Count; i++)
            {
                double x = i * stepX;
                double val = points[i].Value;
                double y = (height + 20) - ((val / maxVal) * height);
                
                polyline.Points.Add(new Point(x, y));

                // Professional dot
                var dotPointBrush = points[i].ColorBrush ?? LineColor;
                var dot = new Ellipse { Width = 6, Height = 6, Fill = PointBrush, Stroke = dotPointBrush, StrokeThickness = 1.5 };
                Canvas.SetLeft(dot, x - 3);
                Canvas.SetTop(dot, y - 3);
                ChartCanvas.Children.Add(dot);

                // Tooltip
                dot.ToolTip = $"{points[i].Label}: {val:N2}";

                // X Label
                if (points.Count <= 12 || i % (points.Count / 8) == 0) 
                {
                    var lbl = new TextBlock { Text = points[i].Label, FontSize = 10, Foreground = Brushes.Gray };
                    lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(lbl, x - (lbl.DesiredSize.Width / 2));
                    Canvas.SetTop(lbl, 5);
                    AxisXCanvas.Children.Add(lbl);
                }
            }

            // Draw Y Labels
            DrawYLabel(0, 0, height);
            DrawYLabel(maxVal / 2, height / 2, height);
            DrawYLabel(maxVal, height, height);

            ChartCanvas.Children.Add(polyline);
        }

        private void DrawYLabel(double val, double yPosFromBottom, double totalHeight)
        {
            var lbl = new TextBlock { Text = $"{val:N0}", FontSize = 10, Foreground = Brushes.Gray, TextAlignment = TextAlignment.Right, Width = 25 };
            Canvas.SetLeft(lbl, 0);
            
            double y = totalHeight - yPosFromBottom;
            // Clamp to bounds
            if (y < 0) y = 0; 
            if (y > totalHeight) y = totalHeight - 10;
            
            Canvas.SetTop(lbl, y - 6);
            AxisYCanvas.Children.Add(lbl);
        }
    }
}
