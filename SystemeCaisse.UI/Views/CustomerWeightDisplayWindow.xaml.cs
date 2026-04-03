using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.Views
{
    /// <summary>
    /// Fenêtre client affichée sur l'écran secondaire pendant la pesée.
    /// Affiche la photo du produit, le prix/kg, le poids en temps réel et le prix calculé.
    /// </summary>
    public partial class CustomerWeightDisplayWindow : Window
    {
        private readonly SerialScaleService? _scaleService;
        private readonly decimal _prixKg;

        public CustomerWeightDisplayWindow(Produit produit, SerialScaleService? scaleService)
        {
            InitializeComponent();
            _scaleService = scaleService;
            _prixKg = produit.PrixVente;

            // Product info
            ProductNameText.Text = produit.Nom ?? "Produit";
            PricePerKgText.Text = $"{produit.PrixVente:N2} €/kg";

            // Product image
            LoadProductImage(produit);

            // Subscribe to scale events
            if (_scaleService != null && _scaleService.IsConnected)
            {
                _scaleService.WeightChanged += OnWeightChanged;
                _scaleService.StatusChanged += OnStatusChanged;
                StatusIcon.Text = "🟢";
                StatusText.Text = "Balance connectée";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#81C784"));
            }
            else
            {
                StatusIcon.Text = "🔴";
                StatusText.Text = "Balance non connectée";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF5350"));
            }
        }

        private void LoadProductImage(Produit produit)
        {
            if (!string.IsNullOrWhiteSpace(produit.ImagePath))
            {
                try
                {
                    string imagePath = produit.ImagePath;
                    
                    // Build full path if relative
                    if (!Path.IsPathRooted(imagePath))
                    {
                        imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath);
                    }

                    if (File.Exists(imagePath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();

                        ProductImage.Source = bitmap;
                        ImageBorder.Visibility = Visibility.Visible;
                        NoImageBorder.Visibility = Visibility.Collapsed;
                        return;
                    }
                }
                catch { /* fallback to no image */ }
            }

            ImageBorder.Visibility = Visibility.Collapsed;
            NoImageBorder.Visibility = Visibility.Visible;
        }

        private void OnWeightChanged(decimal weight)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                WeightText.Text = weight.ToString("N3");
                decimal totalPrice = weight * _prixKg;
                TotalPriceText.Text = totalPrice.ToString("N2");
            });
        }

        private void OnStatusChanged(string status)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                bool connected = status == "Connecté";
                StatusIcon.Text = connected ? "🟢" : "🔴";
                StatusText.Text = connected ? "Balance connectée" : "Balance déconnectée";
                StatusText.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(connected ? "#81C784" : "#EF5350"));
            });
        }

        /// <summary>
        /// Positionne et affiche la fenêtre en plein écran sur l'écran client.
        /// </summary>
        public void ShowOnScreen(ScreenHelper.ScreenInfo screen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = screen.LogicalBounds.Left;
            Top = screen.LogicalBounds.Top;
            Width = screen.LogicalBounds.Width;
            Height = screen.LogicalBounds.Height;

            Show();

            // Re-set after show for reliability
            Left = screen.LogicalBounds.Left;
            Top = screen.LogicalBounds.Top;
            WindowState = WindowState.Maximized;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_scaleService != null)
            {
                _scaleService.WeightChanged -= OnWeightChanged;
                _scaleService.StatusChanged -= OnStatusChanged;
            }
            base.OnClosed(e);
        }
    }
}
