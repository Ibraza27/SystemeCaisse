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
    public partial class SimpleBarChart : UserControl
    {
        public static readonly DependencyProperty DataPointsProperty =
            DependencyProperty.Register("DataPoints", typeof(IEnumerable<LineDataPoint>), typeof(SimpleBarChart),
                new PropertyMetadata(null, OnDataChanged));

        public static readonly DependencyProperty BarColorProperty =
            DependencyProperty.Register("BarColor", typeof(Brush), typeof(SimpleBarChart), new PropertyMetadata(Brushes.Blue));

        public IEnumerable<LineDataPoint> DataPoints
        {
            get { return (IEnumerable<LineDataPoint>)GetValue(DataPointsProperty); }
            set { SetValue(DataPointsProperty, value); }
        }

        public Brush BarColor
        {
            get { return (Brush)GetValue(BarColorProperty); }
            set { SetValue(BarColorProperty, value); }
        }

        public SimpleBarChart()
        {
            InitializeComponent();
        }

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var chart = d as SimpleBarChart;
            chart?.DrawChart();
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawChart();
        }

        private void DrawChart()
        {
            if (ChartCanvas == null) return;
            ChartCanvas.Children.Clear();
            AxisXCanvas.Children.Clear();
            AxisYCanvas.Children.Clear();

            if (DataPoints == null || !DataPoints.Any() || ChartCanvas.ActualWidth <= 0 || ChartCanvas.ActualHeight <= 0) return;

            var points = DataPoints.ToList();
            double maxVal = points.Max(p => p.Value);
            if (maxVal == 0) maxVal = 100;

            double canvasWidth = ChartCanvas.ActualWidth;
            double canvasHeight = ChartCanvas.ActualHeight - 20; // Reserve 20px extra at top for labels
            
            double barWidth = (canvasWidth / points.Count) * 0.7; 
            double gap = (canvasWidth / points.Count) * 0.3;

            // Grid lines
            for (int j = 0; j <= 4; j++)
            {
                double yLine = (canvasHeight + 20) - (j * (canvasHeight / 4));
                var line = new Line 
                { 
                    X1 = 0, Y1 = yLine, X2 = canvasWidth, Y2 = yLine, 
                    Stroke = new SolidColorBrush(Color.FromRgb(240, 240, 240)), 
                    StrokeThickness = 1
                };
                ChartCanvas.Children.Add(line);
            }

            for (int i = 0; i < points.Count; i++)
            {
                double x = (i * (barWidth + gap)) + (gap / 2);
                double val = points[i].Value;
                double h = (val / maxVal) * canvasHeight;
                double y = (canvasHeight + 20) - h;

                var rect = new Rectangle
                {
                    Width = barWidth,
                    Height = h >= 0 ? h : 0,
                    Fill = points[i].ColorBrush ?? BarColor,
                    RadiusX = 4, RadiusY = 4,
                    ToolTip = $"{points[i].Label}: {val:N2}"
                };

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                ChartCanvas.Children.Add(rect);

                // Top Label (Value + Secondary if available)
                string topText = val > 0 ? $"{val:N0}" : "";
                if (!string.IsNullOrEmpty(points[i].SecondaryLabel))
                {
                    topText += $"\n({points[i].SecondaryLabel})";
                }

                if (!string.IsNullOrEmpty(topText))
                {
                    var valLbl = new TextBlock 
                    { 
                        Text = topText, 
                        FontSize = 9, 
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center,
                        Width = barWidth + gap,
                        Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50))
                    };
                    Canvas.SetLeft(valLbl, x - (gap/2));
                    Canvas.SetTop(valLbl, y - 25);
                    ChartCanvas.Children.Add(valLbl);
                }

                // X Label
                if (points.Count <= 12 || i % (points.Count / 6) == 0)
                {
                    var lbl = new TextBlock 
                    { 
                        Text = points[i].Label, 
                        FontSize = 10, 
                        Foreground = Brushes.Gray,
                        TextAlignment = TextAlignment.Center,
                        Width = barWidth + gap
                    };
                    Canvas.SetLeft(lbl, x - (gap/2));
                    Canvas.SetTop(lbl, 5);
                    AxisXCanvas.Children.Add(lbl);
                }
            }

            // Y Labels
            DrawYLabel(0, 0, canvasHeight);
            DrawYLabel(maxVal / 2, canvasHeight / 2, canvasHeight);
            DrawYLabel(maxVal, canvasHeight, canvasHeight);
        }

        private void DrawYLabel(double val, double yPosFromBottom, double totalHeight)
        {
            var lbl = new TextBlock { Text = $"{val:N0}", FontSize = 10, Foreground = Brushes.Gray, TextAlignment = TextAlignment.Right, Width = 25 };
            Canvas.SetLeft(lbl, 0);
            double y = totalHeight - yPosFromBottom;
            Canvas.SetTop(lbl, y - 6);
            AxisYCanvas.Children.Add(lbl);
        }
    }
}
