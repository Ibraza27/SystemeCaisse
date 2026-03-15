using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SystemeCaisse.UI.Controls
{
    public partial class KpiCard : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(KpiCard), new PropertyMetadata("Title"));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(string), typeof(KpiCard), new PropertyMetadata("0"));

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(string), typeof(KpiCard), new PropertyMetadata("📊"));

        public static readonly DependencyProperty IconBackgroundProperty =
            DependencyProperty.Register("IconBackground", typeof(Brush), typeof(KpiCard), new PropertyMetadata(Brushes.Gray));

        public static readonly DependencyProperty TrendProperty =
            DependencyProperty.Register("Trend", typeof(string), typeof(KpiCard), new PropertyMetadata(""));

        public static readonly DependencyProperty IsPositiveTrendProperty =
            DependencyProperty.Register("IsPositiveTrend", typeof(bool), typeof(KpiCard), new PropertyMetadata(true));
            
        public static readonly DependencyProperty TrendLabelProperty =
            DependencyProperty.Register("TrendLabel", typeof(string), typeof(KpiCard), new PropertyMetadata("vs hier"));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public string Value
        {
            get { return (string)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public string Icon
        {
            get { return (string)GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }

        public Brush IconBackground
        {
            get { return (Brush)GetValue(IconBackgroundProperty); }
            set { SetValue(IconBackgroundProperty, value); }
        }
        
        public string Trend
        {
            get { return (string)GetValue(TrendProperty); }
            set { SetValue(TrendProperty, value); }
        }

        public bool IsPositiveTrend
        {
            get { return (bool)GetValue(IsPositiveTrendProperty); }
            set { SetValue(IsPositiveTrendProperty, value); }
        }
        
        public string TrendLabel
        {
            get { return (string)GetValue(TrendLabelProperty); }
            set { SetValue(TrendLabelProperty, value); }
        }

        public KpiCard()
        {
            InitializeComponent();
        }
    }
}
