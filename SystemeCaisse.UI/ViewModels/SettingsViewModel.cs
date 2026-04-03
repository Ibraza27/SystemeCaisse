using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Printing;
using System.Windows;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Core.Interfaces;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.UI.Services;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging;

namespace SystemeCaisse.UI.ViewModels
{
    public class DisplayPromotionItem : ObservableObject
    {
        public Promotion Promotion { get; }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public DisplayPromotionItem(Promotion promo, bool isSelected)
        {
            Promotion = promo;
            _isSelected = isSelected;
        }
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        
        private readonly IDataMigrationService _migrationService;
        
        [ObservableProperty]
        private Entreprise _entrepriseInfo;

        [ObservableProperty]
        private string _selectedPrinter;

        [ObservableProperty]
        private ObservableCollection<string> _availablePrinters;

        [ObservableProperty]
        private int _ticketWidth = 80;

        [ObservableProperty]
        private bool _showLogoOnTicket = true;

        [ObservableProperty]
        private bool _isTrainingMode;
        
        [ObservableProperty]
        private bool _isCustomerDisplayEnabled = true;
        
        [ObservableProperty]
        private int _selectedScreenIndex = 1;
        
        [ObservableProperty]
        private bool _showCustomerDisplayPromotions = true;
        
        [ObservableProperty]
        private bool _isCompactCustomerDisplay;
        
        [ObservableProperty]
        private ObservableCollection<string> _availableScreens = new();

        // === Scale Properties ===
        [ObservableProperty]
        private bool _isScaleEnabled;

        [ObservableProperty]
        private string _scalePortName = "COM3";

        [ObservableProperty]
        private int _scaleBaudRate = 9600;

        [ObservableProperty]
        private ObservableCollection<string> _availableComPorts = new();

        [ObservableProperty]
        private string _scaleTestStatus = "";

        public List<int> AvailableBaudRates { get; } = new() { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };

        private bool _scaleSettingsChanged;

        partial void OnIsScaleEnabledChanged(bool value)
        {
            _scaleSettingsChanged = true;
        }

        partial void OnScalePortNameChanged(string value)
        {
            _scaleSettingsChanged = true;
        }

        partial void OnScaleBaudRateChanged(int value)
        {
            _scaleSettingsChanged = true;
        }

        public ObservableCollection<DisplayPromotionItem> AvailableDisplayPromotions { get; set; } = new ObservableCollection<DisplayPromotionItem>();

        public SettingsViewModel(IDbContextFactory<AppDbContext> contextFactory, IDataMigrationService migrationService)
        {
            _contextFactory = contextFactory;
            _migrationService = migrationService;
            AvailablePrinters = new ObservableCollection<string>();
            AvailableScreens = new ObservableCollection<string>();

            WeakReferenceMessenger.Default.Register<PromotionsChangedMessage>(this, (r, m) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    using var context = _contextFactory.CreateDbContext();
                    LoadDisplayPromotionsSelection(context);
                });
            });
        }

        public async Task InitializeAsync()
        {
            await Task.Run(async () => 
            {
                LoadData();
                
                // Hardware discovery in background
                var printers = System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().ToList();
                var screens = SystemeCaisse.UI.Services.ScreenHelper.GetScreens();
                var screenNames = new List<string>();
                for (int i = 0; i < screens.Count; i++)
                {
                    var s = screens[i];
                    screenNames.Add($"Écran {i + 1} {(s.IsPrimary ? "(Principal)" : "")} - {s.Bounds.Width}x{s.Bounds.Height}");
                }

                // Discover COM ports
                var comPorts = SerialScaleService.GetAvailablePorts();

                await Application.Current.Dispatcher.InvokeAsync(() => 
                {
                    AvailablePrinters.Clear();
                    foreach (var p in printers) AvailablePrinters.Add(p);
                    
                    AvailableScreens.Clear();
                    foreach (var name in screenNames) AvailableScreens.Add(name);

                    AvailableComPorts.Clear();
                    foreach (var port in comPorts) AvailableComPorts.Add(port);
                });
            });
        }

        [RelayCommand]
        private async Task ImportData()
        {
            var openDlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQLite Database (database.db)|database.db|All Files (*.*)|*.*",
                Title = "Sélectionner la base de données Python (database.db)"
            };

            var mainWin = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is SystemeCaisse.UI.MainWindow);
            if (openDlg.ShowDialog(mainWin) == true)
            {
                try 
                {
                    await _migrationService.MigrateDataAsync(openDlg.FileName);
                    
                    // Refresh MainViewModel enterprise info
                    if (mainWin is SystemeCaisse.UI.MainWindow posWindow && posWindow.DataContext is MainViewModel mainVM)
                    {
                        mainVM.ReloadEntrepriseInfo();
                    }

                    if (MessageBox.Show(mainWin, "Migration réussie ! L'application doit redémarrer pour appliquer les changements. Redémarrer maintenant ?", 
                        "Succès", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        RestartApplication();
                    }
                    _ = Task.Run(() => LoadData());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(mainWin, $"Erreur migration : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadData()
        {
            using var context = _contextFactory.CreateDbContext();
            EntrepriseInfo = context.Entreprise.FirstOrDefault() ?? new Entreprise();
            
            // Default logo path if empty
            if (string.IsNullOrWhiteSpace(EntrepriseInfo.LogoPath))
            {
                var defaultLogo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "logo.png");
                if (File.Exists(defaultLogo))
                {
                    EntrepriseInfo.LogoPath = defaultLogo;
                }
            }

            // Load Configs
            var widthConfig = context.Configuration.Find("ticket_width");
            if (widthConfig != null && int.TryParse(widthConfig.Valeur, out int w)) TicketWidth = w;

            var printerConfig = context.Configuration.Find("imprimante_defaut");
            if (printerConfig != null) SelectedPrinter = printerConfig.Valeur;

            var trainingConfig = context.Configuration.Find("training_mode");
            if (trainingConfig != null && bool.TryParse(trainingConfig.Valeur, out bool mode)) IsTrainingMode = mode;
            
            var cdEnabled = context.Configuration.Find("customer_display_enabled");
            if (cdEnabled != null && bool.TryParse(cdEnabled.Valeur, out bool cdE)) IsCustomerDisplayEnabled = cdE;
            
            var cdScreen = context.Configuration.Find("customer_display_screen_index");
            if (cdScreen != null && int.TryParse(cdScreen.Valeur, out int cdS)) SelectedScreenIndex = cdS;
            
            var cdPromo = context.Configuration.Find("customer_display_show_promotions");
            if (cdPromo != null && bool.TryParse(cdPromo.Valeur, out bool cdP)) ShowCustomerDisplayPromotions = cdP;

            var cdCompact = context.Configuration.Find("customer_display_compact");
            if (cdCompact != null && bool.TryParse(cdCompact.Valeur, out bool cdCp)) IsCompactCustomerDisplay = cdCp;

            // Load Scale settings
            var scaleEnabled = context.Configuration.Find("scale_enabled");
            if (scaleEnabled != null && bool.TryParse(scaleEnabled.Valeur, out bool se)) IsScaleEnabled = se;

            var scalePort = context.Configuration.Find("scale_port_name");
            if (scalePort != null && !string.IsNullOrWhiteSpace(scalePort.Valeur)) ScalePortName = scalePort.Valeur;

            var scaleBaud = context.Configuration.Find("scale_baud_rate");
            if (scaleBaud != null && int.TryParse(scaleBaud.Valeur, out int sb)) ScaleBaudRate = sb;

            // Load Display Promotions for selection
            LoadDisplayPromotionsSelection(context);
        }

        private void LoadDisplayPromotionsSelection(AppDbContext context)
        {
            Application.Current.Dispatcher.Invoke(() => AvailableDisplayPromotions.Clear());
            var allPromos = context.Promotions.Where(p => p.Actif).ToList();

            var config = context.Configuration.Find("customer_display_promotions");
            List<int> selectedIds = new List<int>();
            bool hasExistingConfig = config != null && !string.IsNullOrWhiteSpace(config.Valeur);
            if (hasExistingConfig)
            {
                selectedIds = config!.Valeur!.Split(',').Select(id => int.TryParse(id, out int parsed) ? parsed : -1).ToList();
            }

            foreach (var promo in allPromos)
            {
                // Auto-check new promotions (those not in existing config) 
                bool isSelected = hasExistingConfig ? selectedIds.Contains(promo.Id) : true;
                Application.Current.Dispatcher.Invoke(() => {
                    AvailableDisplayPromotions.Add(new DisplayPromotionItem(promo, isSelected));
                });
            }
        }

        private void LoadScreens()
        {
            AvailableScreens.Clear();
            var screens = ScreenHelper.GetScreens();
            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i];
                string name = $"Écran {i + 1} {(s.IsPrimary ? "(Principal)" : "")} - {s.Bounds.Width}x{s.Bounds.Height}";
                AvailableScreens.Add(name);
            }
        }

        private void LoadPrinters()
        {
            AvailablePrinters.Clear();
            try
            {
                var server = new LocalPrintServer();
                foreach (var queue in server.GetPrintQueues())
                {
                    AvailablePrinters.Add(queue.Name);
                }

                if (string.IsNullOrEmpty(SelectedPrinter) && AvailablePrinters.Count > 0)
                {
                    SelectedPrinter = LocalPrintServer.GetDefaultPrintQueue().Name;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur lors du chargement des imprimantes : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void SelectLogo()
        {
            var openDlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Sélectionner le logo de l'entreprise"
            };

            if (openDlg.ShowDialog(Services.WindowHelper.GetAdminWindow()) == true)
            {
                EntrepriseInfo.LogoPath = openDlg.FileName;
                OnPropertyChanged(nameof(EntrepriseInfo));
            }
        }

        [RelayCommand]
        public async Task ApplyCustomerDisplay()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                UpdateConfig(context, "customer_display_enabled", IsCustomerDisplayEnabled.ToString());
                UpdateConfig(context, "customer_display_screen_index", SelectedScreenIndex.ToString());
                UpdateConfig(context, "customer_display_show_promotions", ShowCustomerDisplayPromotions.ToString());
                await context.SaveChangesAsync();

                // Refresh Customer Display robustly
                var screens = ScreenHelper.GetScreens();
                var owner = Services.WindowHelper.GetAdminWindow();
                if (screens.Count < 2)
                {
                    MessageBox.Show(owner, "Un seul écran détecté. L'affichage client nécessite un second écran en mode 'Étendre'.", 
                        "Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var mainWin = owner as SystemeCaisse.UI.MainWindow;
                if (mainWin != null)
                {
                    mainWin.ReinitializeCustomerDisplay();
                    MessageBox.Show(owner, "Affichage client rafraîchi !", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Fallback if mainWin is not MainWindow
                    var posWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext is MainViewModel);
                    if (posWindow != null && posWindow.DataContext is MainViewModel vm)
                    {
                        vm.InitializeCustomerDisplay();
                        MessageBox.Show(posWindow, "Affichage client rafraîchi !", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                var owner = Services.WindowHelper.GetAdminWindow();
                MessageBox.Show(owner, $"Erreur lors de l'application : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task SaveSettings()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                // Update Entreprise
                context.Update(EntrepriseInfo);

                // Update Configs
                UpdateConfig(context, "ticket_width", TicketWidth.ToString());
                UpdateConfig(context, "imprimante_defaut", SelectedPrinter);
                UpdateConfig(context, "training_mode", IsTrainingMode.ToString());
                UpdateConfig(context, "customer_display_enabled", IsCustomerDisplayEnabled.ToString());
                UpdateConfig(context, "customer_display_screen_index", SelectedScreenIndex.ToString());
                UpdateConfig(context, "customer_display_show_promotions", ShowCustomerDisplayPromotions.ToString());
                UpdateConfig(context, "customer_display_compact", IsCompactCustomerDisplay.ToString());

                // Save Scale settings
                UpdateConfig(context, "scale_enabled", IsScaleEnabled.ToString());
                UpdateConfig(context, "scale_port_name", ScalePortName);
                UpdateConfig(context, "scale_baud_rate", ScaleBaudRate.ToString());

                // Save Selected Promotions
                var selectedIds = AvailableDisplayPromotions.Where(p => p.IsSelected).Select(p => p.Promotion.Id);
                UpdateConfig(context, "customer_display_promotions", string.Join(",", selectedIds));

                await context.SaveChangesAsync();
                
                var owner = Services.WindowHelper.GetAdminWindow();
                
                // Refresh MainViewModel enterprise info
                var mainWin = owner as SystemeCaisse.UI.MainWindow;
                if (mainWin != null && mainWin.DataContext is MainViewModel mainVM)
                {
                    mainVM.ReloadEntrepriseInfo();
                }

                MessageBox.Show(owner, "Paramètres enregistrés avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);

                // Proposer un redémarrage si les paramètres balance ont changé
                if (_scaleSettingsChanged)
                {
                    _scaleSettingsChanged = false;
                    var result = MessageBox.Show(owner,
                        "Les paramètres de la balance ont été modifiés.\nVoulez-vous redémarrer l'application maintenant pour appliquer les changements ?",
                        "Redémarrage requis",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        RestartApplication();
                    }
                }
            }
            catch (Exception ex)
            {
                var owner = Services.WindowHelper.GetAdminWindow();
                MessageBox.Show(owner, $"Erreur lors de l'enregistrement : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateConfig(AppDbContext context, string key, string? value)
        {
            var config = context.Configuration.Find(key);
            if (config == null)
            {
                config = new SystemConfiguration { Cle = key, Valeur = value };
                context.Configuration.Add(config);
            }
            else
            {
                config.Valeur = value;
            }
        }

        [RelayCommand]
        private void BackupDatabase()
        {
            try
            {
                // Dynamic path for installed environment
                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "caisse.db");
                
                if (!File.Exists(dbPath))
                {
                    dbPath = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName) ?? "", "caisse.db");
                }

                if (!File.Exists(dbPath))
                {
                    MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Base de données introuvable : {dbPath}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var saveDlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"caisse_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
                    Filter = "SQLite Database (*.db)|*.db"
                };

                if (saveDlg.ShowDialog(Services.WindowHelper.GetAdminWindow()) == true)
                {
                     File.Copy(dbPath, saveDlg.FileName, true);
                     MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Sauvegarde effectuée !");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur sauvegarde : {ex.Message}");
            }
        }

        [RelayCommand]
        private void RestoreDatabase()
        {
            try
            {
                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "caisse.db");
                if (!File.Exists(dbPath))
                {
                    dbPath = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName) ?? "", "caisse.db");
                }

                var openDlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "SQLite Database (*.db)|*.db",
                    Title = "Sélectionner la sauvegarde à restaurer"
                };

                var mainWin = Services.WindowHelper.GetAdminWindow();
                if (openDlg.ShowDialog(mainWin) == true)
                {
                    var confirm = MessageBox.Show(mainWin, 
                        "⚠️ ATTENTION : Cela remplacera TOUTES les données actuelles par celles de la sauvegarde sélectionnée.\n\nÊtes-vous sûr de vouloir continuer ?", 
                        "Restauration de sauvegarde", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    
                    if (confirm == MessageBoxResult.Yes)
                    {
                        File.Copy(openDlg.FileName, dbPath, true);
                        
                        if (MessageBox.Show(mainWin, "Sauvegarde restaurée avec succès !\nL'application doit redémarrer pour appliquer les changements.\n\nRedémarrer maintenant ?", 
                            "Restauration terminée", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            RestartApplication();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur restauration : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        [RelayCommand]
        private void ResetDatabase()
        {
            var mainWin = Services.WindowHelper.GetAdminWindow();
            var result = MessageBox.Show(mainWin, "ÊTES-VOUS SÛR ? Cela effacera TOUTES les données (produits, ventes, historique). Cette action est irréversible.", 
                "AVERTISSEMENT CRITIQUE", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    using var context = _contextFactory.CreateDbContext();
                    context.Database.EnsureDeleted();
                    context.Database.Migrate();
                    
                    if (MessageBox.Show(mainWin, "Base de données réinitialisée ! Redémarrer l'application pour finaliser ?", 
                        "Réinitialisation terminée", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        RestartApplication();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(mainWin, $"Erreur lors de la réinitialisation : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RestartApplication()
        {
            var processPath = Process.GetCurrentProcess().MainModule?.FileName;
            if (processPath != null)
            {
                Process.Start(processPath);
                Application.Current.Shutdown();
            }
        }

        [RelayCommand]
        private void RefreshComPorts()
        {
            AvailableComPorts.Clear();
            foreach (var port in SerialScaleService.GetAvailablePorts())
            {
                AvailableComPorts.Add(port);
            }
            if (AvailableComPorts.Count == 0)
            {
                ScaleTestStatus = "❌ Aucun port COM détecté. Vérifiez la connexion USB.";
            }
            else
            {
                ScaleTestStatus = $"🔄 {AvailableComPorts.Count} port(s) COM détecté(s).";
            }
        }

        [RelayCommand]
        private async Task TestScaleConnection()
        {
            if (string.IsNullOrWhiteSpace(ScalePortName))
            {
                ScaleTestStatus = "❌ Veuillez sélectionner un port COM.";
                return;
            }

            ScaleTestStatus = "⏳ Test en cours...";

            // Récupérer le service actif depuis le MainViewModel
            SerialScaleService? activeService = null;
            var mainWin = Application.Current.MainWindow;
            if (mainWin?.DataContext is MainViewModel mainVM)
            {
                activeService = mainVM.ScaleService;
            }

            bool success = await Task.Run(() =>
            {
                try
                {
                    return SerialScaleService.TestConnection(ScalePortName, ScaleBaudRate, activeService);
                }
                catch
                {
                    return false;
                }
            });

            ScaleTestStatus = success
                ? $"✅ Connexion réussie sur {ScalePortName} à {ScaleBaudRate} baud !"
                : $"❌ Impossible de se connecter sur {ScalePortName}. Vérifiez le branchement et les paramètres.";
        }
    }
}
