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
            double minVal = 0; // Always start Y at 0
            if (maxVal == 0) maxVal = 100; // Avoid divide by zero

            double width = ChartCanvas.ActualWidth;
            double height = ChartCanvas.ActualHeight;
            double stepX = width / (points.Count - 1 > 0 ? points.Count - 1 : 1);

            var polyline = new Polyline
            {
                Stroke = LineColor,
                StrokeThickness = 3,
                Points = new PointCollection()
            };

            // Draw Grid Lines & X Labels
            for (int i = 0; i < points.Count; i++)
            {
                double x = i * stepX;
                double val = points[i].Value;
                // Invert Y axis (0 is top)
                double y = height - ((val / maxVal) * height);
                
                polyline.Points.Add(new Point(x, y));

                // Dot at point
                var dot = new Ellipse { Width = 8, Height = 8, Fill = Brushes.White, Stroke = LineColor, StrokeThickness = 2 };
                Canvas.SetLeft(dot, x - 4);
                Canvas.SetTop(dot, y - 4);
                ChartCanvas.Children.Add(dot);

                // Tooltip trigger
                dot.ToolTip = $"{points[i].Label}: {val:C2}";

                // X Label (Skip some if too many)
                if (points.Count <= 12 || i % (points.Count / 6) == 0) 
                {
                    var lbl = new TextBlock { Text = points[i].Label, FontSize = 10, Foreground = Brushes.Gray };
                    lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(lbl, x - (lbl.DesiredSize.Width / 2) + 30); // Offset for container margin
                    Canvas.SetTop(lbl, 5);
                    AxisXCanvas.Children.Add(lbl);
                    
                    // Vertical Grid Line
                    var line = new Line 
                    { 
                        X1 = x, Y1 = 0, X2 = x, Y2 = height, 
                        Stroke = Brushes.LightGray, StrokeDashArray = new DoubleCollection { 2, 2 } 
                    };
                    ChartCanvas.Children.Add(line);
                }
            }

            // Draw Y Labels (0, 50%, 100%)
            DrawYLabel(0, 0, height);
            DrawYLabel(maxVal / 2, height / 2, height);
            DrawYLabel(maxVal, height, height);

            ChartCanvas.Children.Add(polyline);
            
            // Fill area under curve
            var polygon = new Polygon
            {
                Fill = LineColor,
                Opacity = 0.2,
                Points = polyline.Points.Clone()
            };
            polygon.Points.Add(new Point(width, height));
            polygon.Points.Add(new Point(0, height));
            ChartCanvas.Children.Insert(0, polygon);
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
