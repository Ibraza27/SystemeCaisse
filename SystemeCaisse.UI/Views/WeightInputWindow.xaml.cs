using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.Views
{
    public partial class WeightInputWindow : Window
    {
        public decimal PoidsSaisi { get; private set; }

        private readonly SerialScaleService? _scaleService;
        private readonly Produit? _produit;
        private readonly decimal _prixKg;
        private bool _isAutoMode = true;
        private decimal _liveWeight;

        // Pré-cached brushes pour éviter les allocations à chaque mise à jour
        private static readonly SolidColorBrush BrushGreen = new((Color)ColorConverter.ConvertFromString("#2E7D32"));
        private static readonly SolidColorBrush BrushGreenBg = new((Color)ColorConverter.ConvertFromString("#E8F5E9"));
        private static readonly SolidColorBrush BrushOrange = new((Color)ColorConverter.ConvertFromString("#E65100"));
        private static readonly SolidColorBrush BrushOrangeBg = new((Color)ColorConverter.ConvertFromString("#FFF3E0"));

        static WeightInputWindow()
        {
            // Freeze brushes for thread-safe cross-thread usage
            BrushGreen.Freeze();
            BrushGreenBg.Freeze();
            BrushOrange.Freeze();
            BrushOrangeBg.Freeze();
        }

        /// <summary>
        /// Constructeur avec service balance optionnel.
        /// Si scaleService est null ou non connecté, bascule en mode manuel.
        /// </summary>
        public WeightInputWindow(Produit? produit = null, SerialScaleService? scaleService = null)
        {
            InitializeComponent();
            _produit = produit;
            _scaleService = scaleService;
            _prixKg = produit?.PrixVente ?? 0;

            if (produit != null)
            {
                DataContext = produit;
                ProductNameTxt.Text = produit.Nom;
                ProductPriceTxt.Text = $"{produit.PrixVente:N2} €/kg";
            }

            // Check if scale is available
            if (_scaleService != null && _scaleService.IsConnected)
            {
                // Auto mode available
                _scaleService.WeightChanged += OnWeightChanged;
                _scaleService.StatusChanged += OnStatusChanged;
                UpdateConnectionStatus(true);
                AutoModeRadio.IsChecked = true;
            }
            else
            {
                // No scale: default to manual mode
                ManualModeRadio.IsChecked = true;
                AutoModeRadio.IsEnabled = false;
                if (_scaleService == null)
                {
                    AutoModeRadio.Visibility = Visibility.Collapsed;
                }
            }

            UpdateModeVisibility();

            // Manual input: calculate price on text change
            WeightInput.TextChanged += (s, e) => UpdateManualPriceEstimation();
            WeightInput.Focus();
        }

        private void OnWeightChanged(decimal weight)
        {
            // DispatcherPriority.Send = Priorité MAXIMALE, exécution immédiate
            // (plus prioritaire que Input, Render, DataBind, etc.)
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                _liveWeight = weight;
                LiveWeightText.Text = weight.ToString("N3");

                decimal estimated = weight * _prixKg;
                EstimatedPriceText.Text = $"{estimated:N2} €";
            });
        }

        private void OnStatusChanged(string status)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                bool connected = status == "Connecté";
                UpdateConnectionStatus(connected);
                StatusText.Text = status;
            });
        }

        private void UpdateConnectionStatus(bool connected)
        {
            if (connected)
            {
                StatusIcon.Text = "🟢";
                StatusText.Text = "Balance connectée";
                StatusText.Foreground = BrushGreen;
                StatusBorder.Background = BrushGreenBg;
            }
            else
            {
                StatusIcon.Text = "🔴";
                StatusText.Text = "Balance déconnectée";
                StatusText.Foreground = BrushOrange;
                StatusBorder.Background = BrushOrangeBg;
            }
        }

        private void ModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateModeVisibility();
        }

        private void UpdateModeVisibility()
        {
            if (AutoPanel == null || ManualPanel == null) return;

            _isAutoMode = AutoModeRadio?.IsChecked == true;
            
            AutoPanel.Visibility = _isAutoMode ? Visibility.Visible : Visibility.Collapsed;
            ManualPanel.Visibility = _isAutoMode ? Visibility.Collapsed : Visibility.Visible;
            BtnValider.Visibility = _isAutoMode ? Visibility.Collapsed : Visibility.Visible;

            if (!_isAutoMode)
            {
                WeightInput.Focus();
            }
        }

        private void UpdateManualPriceEstimation()
        {
            if (decimal.TryParse(WeightInput.Text.Replace('.', ','), out decimal poids) && poids > 0)
            {
                decimal estimated = poids * _prixKg;
                ManualEstimatedPriceText.Text = $"{estimated:N2} €";
                ManualPriceEstBorder.Visibility = Visibility.Visible;
            }
            else
            {
                ManualPriceEstBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnTare_Click(object sender, RoutedEventArgs e)
        {
            _scaleService?.Tare();
        }

        private void BtnZero_Click(object sender, RoutedEventArgs e)
        {
            _scaleService?.Zero();
        }

        private void BtnAddAuto_Click(object sender, RoutedEventArgs e)
        {
            if (_liveWeight <= 0)
            {
                MessageBox.Show(this, "Le poids doit être supérieur à 0.\nPlacez un article sur la balance.", 
                    "Poids invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PoidsSaisi = _liveWeight;
            DialogResult = true;
            Close();
        }

        private void BtnValider_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(WeightInput.Text.Replace('.', ','), out decimal poids) && poids > 0)
            {
                PoidsSaisi = poids;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(this, "Veuillez saisir un poids valide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Properly detach events to avoid memory leaks
            if (_scaleService != null)
            {
                _scaleService.WeightChanged -= OnWeightChanged;
                _scaleService.StatusChanged -= OnStatusChanged;
            }
            base.OnClosed(e);
        }
    }
}
