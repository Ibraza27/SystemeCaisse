using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SystemeCaisse.UI.Models;

namespace SystemeCaisse.UI.Controls
{
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
            Loaded += (_, _) => DrawChart();
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible)
                {
                    Dispatcher.BeginInvoke(new Action(DrawChart), DispatcherPriority.Loaded);
                }
            };
        }

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleLineChart chart)
            {
                chart.DrawChart();
                chart.Dispatcher.BeginInvoke(new Action(chart.DrawChart), DispatcherPriority.Loaded);
            }
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawChart();
        }

        private void DrawChart()
        {
            if (ChartCanvas == null || AxisXCanvas == null || AxisYCanvas == null)
                return;

            ChartCanvas.Children.Clear();
            AxisXCanvas.Children.Clear();
            AxisYCanvas.Children.Clear();

            if (DataPoints == null || !DataPoints.Any())
                return;

            if (ChartCanvas.ActualWidth <= 0 || ChartCanvas.ActualHeight <= 0)
                return;

            var points = DataPoints.ToList();
            double maxVal = points.Max(p => p.Value);
            if (maxVal <= 0) maxVal = 100;

            double width = ChartCanvas.ActualWidth;
            double height = Math.Max(40, ChartCanvas.ActualHeight - 24);
            double topOffset = 14;

            // Grid lines
            for (int j = 0; j <= 4; j++)
            {
                double yLine = (height + topOffset) - (j * (height / 4));
                var line = new Line
                {
                    X1 = 0,
                    Y1 = yLine,
                    X2 = width,
                    Y2 = yLine,
                    Stroke = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                    StrokeThickness = 1
                };
                ChartCanvas.Children.Add(line);
            }

            // Handle single point gracefully by centering it and drawing a reference line
            if (points.Count == 1)
            {
                double val = points[0].Value;
                double y = (height + topOffset) - ((val / maxVal) * height);
                double centerX = width / 2;

                if (AreaBrush != null)
                {
                    var areaRect = new Polygon
                    {
                        Fill = AreaBrush,
                        Opacity = 0.2,
                        Points = new PointCollection
                        {
                            new Point(0, height + topOffset),
                            new Point(0, y),
                            new Point(width, y),
                            new Point(width, height + topOffset)
                        }
                    };
                    ChartCanvas.Children.Add(areaRect);
                }

                var singleLine = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = LineColor,
                    StrokeThickness = 2.5
                };
                ChartCanvas.Children.Add(singleLine);

                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = LineColor,
                    Stroke = PointBrush,
                    StrokeThickness = 2,
                    ToolTip = $"{points[0].Label} : {val:N2} €"
                };
                Canvas.SetLeft(dot, centerX - 4);
                Canvas.SetTop(dot, y - 4);
                ChartCanvas.Children.Add(dot);

                var lbl = new TextBlock { Text = points[0].Label, FontSize = 10, Foreground = Brushes.Gray };
                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(lbl, centerX - (lbl.DesiredSize.Width / 2));
                Canvas.SetTop(lbl, 5);
                AxisXCanvas.Children.Add(lbl);

                DrawYLabel(0, 0, height, topOffset);
                DrawYLabel(maxVal / 2, height / 2, height, topOffset);
                DrawYLabel(maxVal, height, height, topOffset);
                return;
            }

            double stepX = width / (points.Count - 1);

            var polyline = new Polyline
            {
                Stroke = LineColor,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round,
                Points = new PointCollection()
            };

            // Draw Area fill FIRST
            var effectiveAreaBrush = AreaBrush ?? LineColor;
            if (effectiveAreaBrush != null)
            {
                var areaPolygon = new Polygon
                {
                    Fill = effectiveAreaBrush,
                    Opacity = 0.18,
                    Points = new PointCollection()
                };
                areaPolygon.Points.Add(new Point(0, height + topOffset));
                for (int i = 0; i < points.Count; i++)
                {
                    double x = i * stepX;
                    double y = (height + topOffset) - ((points[i].Value / maxVal) * height);
                    areaPolygon.Points.Add(new Point(x, y));
                }
                areaPolygon.Points.Add(new Point(width, height + topOffset));
                ChartCanvas.Children.Add(areaPolygon);
            }

            for (int i = 0; i < points.Count; i++)
            {
                double x = i * stepX;
                double val = points[i].Value;
                double y = (height + topOffset) - ((val / maxVal) * height);
                polyline.Points.Add(new Point(x, y));
            }

            ChartCanvas.Children.Add(polyline);

            int labelInterval = points.Count <= 12 ? 1 : Math.Max(1, points.Count / 8);

            // Draw X Labels & Points on top of the line
            for (int i = 0; i < points.Count; i++)
            {
                double x = i * stepX;
                double val = points[i].Value;
                double y = (height + topOffset) - ((val / maxVal) * height);

                var dotStroke = points[i].ColorBrush ?? LineColor;
                bool hasValue = val > 0;
                double dotSize = hasValue ? 7 : 5;
                var dot = new Ellipse
                {
                    Width = dotSize,
                    Height = dotSize,
                    Fill = hasValue ? dotStroke : PointBrush,
                    Stroke = dotStroke,
                    StrokeThickness = 1.5,
                    ToolTip = $"{points[i].Label} : {val:N2} €"
                };
                Canvas.SetLeft(dot, x - (dotSize / 2));
                Canvas.SetTop(dot, y - (dotSize / 2));
                ChartCanvas.Children.Add(dot);

                if (i % labelInterval == 0 || i == points.Count - 1)
                {
                    // Avoid overlapping the last label with the previous interval label
                    if (i == points.Count - 1 && i % labelInterval != 0 && (points.Count - 1 - (i - (i % labelInterval))) < 2)
                        continue;

                    var lbl = new TextBlock { Text = points[i].Label, FontSize = 10, Foreground = Brushes.Gray };
                    lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double left = Math.Max(0, Math.Min(width - lbl.DesiredSize.Width, x - (lbl.DesiredSize.Width / 2)));
                    Canvas.SetLeft(lbl, left);
                    Canvas.SetTop(lbl, 5);
                    AxisXCanvas.Children.Add(lbl);
                }
            }

            // Draw Y Labels
            DrawYLabel(0, 0, height, topOffset);
            DrawYLabel(maxVal / 2, height / 2, height, topOffset);
            DrawYLabel(maxVal, height, height, topOffset);
        }

        private void DrawYLabel(double val, double yPosFromBottom, double totalHeight, double topOffset)
        {
            var lbl = new TextBlock
            {
                Text = $"{val:N0}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                TextAlignment = TextAlignment.Right,
                Width = 28
            };
            Canvas.SetLeft(lbl, 0);

            double y = (totalHeight + topOffset) - yPosFromBottom;
            if (y < 0) y = 0;
            if (y > totalHeight + topOffset) y = totalHeight + topOffset - 10;

            Canvas.SetTop(lbl, y - 6);
            AxisYCanvas.Children.Add(lbl);
        }
    }
}
