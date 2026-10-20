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
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.Infrastructure.Services;
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
        private readonly CaisseServices? _caisseServices;
        private readonly LocalSettingsStore _localStore;
        
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
        private int _topProductsCount = 20;

        [ObservableProperty]
        private bool _showAllTopProducts;
        
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

        // === Network & Multi-Postes PostgreSQL / Tailscale Properties ===
        [ObservableProperty]
        private bool _isNetworkDbEnabled;

        [ObservableProperty]
        private string _networkDbPath = @"\\100.x.y.z\SystemeCaisse\caisse.db";

        [ObservableProperty]
        private string _networkDbTestStatus = "";

        [ObservableProperty]
        private string _currentDbModeDisplay = "";

        [ObservableProperty]
        private string _nomPoste = Environment.MachineName;

        [ObservableProperty]
        private string _selectedPosteMode = "PrincipalPostgres";

        public bool IsPrincipalPostgresMode => SelectedPosteMode == "PrincipalPostgres";
        public bool IsSecondairePostgresMode => SelectedPosteMode == "SecondairePostgres";
        public bool IsLocalSqliteMode => SelectedPosteMode == "LocalSqlite";

        partial void OnSelectedPosteModeChanged(string value)
        {
            OnPropertyChanged(nameof(IsPrincipalPostgresMode));
            OnPropertyChanged(nameof(IsSecondairePostgresMode));
            OnPropertyChanged(nameof(IsLocalSqliteMode));
        }

        [ObservableProperty]
        private string _pgHost = "localhost";

        [ObservableProperty]
        private int _pgPort = 5432;

        [ObservableProperty]
        private string _pgDatabase = "caisse";

        [ObservableProperty]
        private string _pgUsername = "caisse";

        [ObservableProperty]
        private string _pgPassword = "caisse";

        [ObservableProperty]
        private string _tailscaleStatusDisplay = "⏳ Vérification du réseau Tailscale…";

        [ObservableProperty]
        private string _tailscaleIpAddress = "";

        public bool HasTailscaleIp => !string.IsNullOrWhiteSpace(TailscaleIpAddress);

        partial void OnTailscaleIpAddressChanged(string value)
        {
            OnPropertyChanged(nameof(HasTailscaleIp));
        }

        [ObservableProperty]
        private bool _isProvisioningBusy;

        [ObservableProperty]
        private string _pgProvisioningStatus = "";

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

        // === TPE Properties ===
        [ObservableProperty]
        private bool _isTPEEnabled;

        [ObservableProperty]
        private string _tpePortName = "COM1";

        [ObservableProperty]
        private int _tpeBaudRate = 9600;

        [ObservableProperty]
        private int _tpeTimeout = 60;

        [ObservableProperty]
        private string _tpeTestStatus = "";

        private bool _tpeSettingsChanged;

        partial void OnIsTPEEnabledChanged(bool value)
        {
            _tpeSettingsChanged = true;
        }

        partial void OnTpePortNameChanged(string value)
        {
            _tpeSettingsChanged = true;
        }

        partial void OnTpeBaudRateChanged(int value)
        {
            _tpeSettingsChanged = true;
        }

        partial void OnTpeTimeoutChanged(int value)
        {
            _tpeSettingsChanged = true;
        }

        // === Price Format Testing ===
        [ObservableProperty]
        private string _selectedPriceFormat = "$XX.XX";

        [ObservableProperty]
        private string _testPriceValue = "5.90";

        [ObservableProperty]
        private string _priceTestResult = "";

        [ObservableProperty]
        private string _customScaleCommand = "";

        public List<string> AvailablePriceFormats { get; } = new()
        {
            "$XX.XX",
            "U XX.XX",
            "UP XX.XX",
            "UXXXX (centimes)",
            "C1 XX.XX",
            "F XX.XX",
            "XX.XX (brut)",
            "STX $ XX.XX ETX",
            "ESC P XX.XX",
            "W1 XX.XX",
            "S XX.XX",
            "N XX.XX",
            "PLU XX.XX",
            "01XXXXXX (8 chiffres)"
        };

        public string PriceFormatDescription => SelectedPriceFormat switch
        {
            "$XX.XX" => "Envoi : $5.90\\r\\n — Format dollar courant Adam Equipment",
            "U XX.XX" => "Envoi : U 5.90\\r\\n — Commande Unit price avec espace",
            "UP XX.XX" => "Envoi : UP 5.90\\r\\n — Commande Unit Price préfixe",
            "UXXXX (centimes)" => "Envoi : U0590\\r\\n — Prix en centimes sans point décimal",
            "C1 XX.XX" => "Envoi : C1 5.90\\r\\n — Canal 1 prix",
            "F XX.XX" => "Envoi : F 5.90\\r\\n — Format F (certains modèles)",
            "XX.XX (brut)" => "Envoi : 5.90\\r\\n — Juste la valeur numérique",
            "STX $ XX.XX ETX" => "Envoi : \\x02$5.90\\x03 — Encapsulé STX/ETX",
            "ESC P XX.XX" => "Envoi : \\x1BP5.90\\r\\n — Séquence Escape + P",
            "W1 XX.XX" => "Envoi : W1 5.90\\r\\n — Write register 1",
            "S XX.XX" => "Envoi : S 5.90\\r\\n — Set price",
            "N XX.XX" => "Envoi : N 5.90\\r\\n — Numeric input",
            "PLU XX.XX" => "Envoi : PLU 5.90\\r\\n — Product Look Up prix",
            "01XXXXXX (8 chiffres)" => "Envoi : 01000590\\r\\n — Code 01 + 6 chiffres centimes",
            _ => ""
        };

        public string TestPricePreview
        {
            get
            {
                if (!decimal.TryParse(TestPriceValue?.Replace(',', '.'), System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                    return "⚠️ Prix invalide";
                return $"Aperçu : \"{FormatPriceCommand(SelectedPriceFormat, price)}\\r\\n\"";
            }
        }

        partial void OnSelectedPriceFormatChanged(string value)
        {
            OnPropertyChanged(nameof(PriceFormatDescription));
            OnPropertyChanged(nameof(TestPricePreview));
        }

        partial void OnTestPriceValueChanged(string value)
        {
            OnPropertyChanged(nameof(TestPricePreview));
        }

        private string FormatPriceCommand(string format, decimal price)
        {
            string priceStr = price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            int centimes = (int)(price * 100);

            return format switch
            {
                "$XX.XX" => $"${priceStr}",
                "U XX.XX" => $"U {priceStr}",
                "UP XX.XX" => $"UP {priceStr}",
                "UXXXX (centimes)" => $"U{centimes:D4}",
                "C1 XX.XX" => $"C1 {priceStr}",
                "F XX.XX" => $"F {priceStr}",
                "XX.XX (brut)" => priceStr,
                "STX $ XX.XX ETX" => $"\x02${priceStr}\x03",
                "ESC P XX.XX" => $"\x1BP{priceStr}",
                "W1 XX.XX" => $"W1 {priceStr}",
                "S XX.XX" => $"S {priceStr}",
                "N XX.XX" => $"N {priceStr}",
                "PLU XX.XX" => $"PLU {priceStr}",
                "01XXXXXX (8 chiffres)" => $"01{centimes:D6}",
                _ => priceStr
            };
        }

        private SerialScaleService? GetActiveScaleService()
        {
            var mainWin = Application.Current.MainWindow;
            if (mainWin?.DataContext is MainViewModel mainVM)
                return mainVM.ScaleService;
            return null;
        }

        [RelayCommand]
        private void SendTestPrice()
        {
            var service = GetActiveScaleService();
            if (service == null || !service.IsConnected)
            {
                PriceTestResult = "❌ Balance non connectée. Activez et redémarrez d'abord.";
                return;
            }

            if (!decimal.TryParse(TestPriceValue?.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal price))
            {
                PriceTestResult = "❌ Prix invalide.";
                return;
            }

            string cmd = FormatPriceCommand(SelectedPriceFormat, price);
            service.SendCommand(cmd);
            PriceTestResult = $"✅ Envoyé [{SelectedPriceFormat}] : \"{cmd}\\r\\n\" — Vérifiez l'afficheur !";
        }

        [RelayCommand]
        private async Task SendAllPriceFormats()
        {
            var service = GetActiveScaleService();
            if (service == null || !service.IsConnected)
            {
                PriceTestResult = "❌ Balance non connectée.";
                return;
            }

            if (!decimal.TryParse(TestPriceValue?.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal price))
            {
                PriceTestResult = "❌ Prix invalide.";
                return;
            }

            PriceTestResult = "⏳ Test de tous les formats en cours...";

            foreach (var format in AvailablePriceFormats)
            {
                string cmd = FormatPriceCommand(format, price);
                service.SendCommand(cmd);
                PriceTestResult = $"📤 [{format}] : \"{cmd}\" — Vérifiez l'afficheur !";
                await Task.Delay(2500);
            }

            PriceTestResult = "✅ Tous les formats ont été testés. Si aucun n'a fonctionné, essayez la commande personnalisée.";
        }

        [RelayCommand]
        private void SendCustomCommand()
        {
            var service = GetActiveScaleService();
            if (service == null || !service.IsConnected)
            {
                PriceTestResult = "❌ Balance non connectée.";
                return;
            }

            if (string.IsNullOrWhiteSpace(CustomScaleCommand))
            {
                PriceTestResult = "❌ Saisissez une commande.";
                return;
            }

            string cmd = CustomScaleCommand
                .Replace("\\x02", "\x02")
                .Replace("\\x03", "\x03")
                .Replace("\\x1B", "\x1B")
                .Replace("\\r", "\r")
                .Replace("\\n", "\n");

            service.SendCommand(cmd);
            PriceTestResult = $"✅ Envoyé : \"{CustomScaleCommand}\\r\\n\"";
        }

        public ObservableCollection<DisplayPromotionItem> AvailableDisplayPromotions { get; set; } = new ObservableCollection<DisplayPromotionItem>();


        public SettingsViewModel(IDbContextFactory<AppDbContext> contextFactory, IDataMigrationService migrationService, CaisseServices? caisseServices = null)
        {
            _contextFactory = contextFactory;
            _migrationService = migrationService;
            _caisseServices = caisseServices;
            _localStore = caisseServices?.Settings ?? new LocalSettingsStore();
            AvailablePrinters = new ObservableCollection<string>();
            AvailableScreens = new ObservableCollection<string>();

            WeakReferenceMessenger.Default.Register<PromotionsChangedMessage>(this, (r, m) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        using var context = _contextFactory.CreateDbContext();
                        LoadDisplayPromotionsSelection(context);
                    }
                    catch { }
                });
            });
        }

        public async Task InitializeAsync()
        {
            await Task.Run(async () => 
            {
                LoadData();
                
                var printers = System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().ToList();
                var screens = SystemeCaisse.UI.Services.ScreenHelper.GetScreens();
                var screenNames = new List<string>();
                for (int i = 0; i < screens.Count; i++)
                {
                    var s = screens[i];
                    screenNames.Add($"Écran {i + 1} {(s.IsPrimary ? "(Principal)" : "")} - {s.Bounds.Width}x{s.Bounds.Height}");
                }

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
                Filter = "SQLite Database (database.db;caisse.db)|database.db;caisse.db|All Files (*.*)|*.*",
                Title = "Sélectionner la base de données à importer"
            };

            var mainWin = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is SystemeCaisse.UI.MainWindow);
            if (openDlg.ShowDialog(mainWin) == true)
            {
                try 
                {
                    await _migrationService.MigrateDataAsync(openDlg.FileName);
                    
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
            // 1. Charger la configuration locale du poste (poste.json) et l'état Tailscale
            try
            {
                var poste = _localStore.Current;
                var db = poste.Database;
                var ts = PostgresProvisioningService.GetNetworkAndTailscaleStatus();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    NomPoste = poste.NomPoste;
                    PgHost = string.IsNullOrWhiteSpace(db.Host) ? "localhost" : db.Host;
                    PgPort = db.Port > 0 ? db.Port : 5432;
                    PgDatabase = string.IsNullOrWhiteSpace(db.Database) ? "caisse" : db.Database;
                    PgUsername = string.IsNullOrWhiteSpace(db.Username) ? "caisse" : db.Username;
                    PgPassword = string.IsNullOrWhiteSpace(db.Password) ? "caisse" : db.Password;

                    TailscaleStatusDisplay = ts.StatusSummary;
                    TailscaleIpAddress = ts.TailscaleIp ?? ts.LanIp ?? "";

                    if (db.Provider == DatabaseProvider.PostgreSql)
                    {
                        bool isPrincipal = poste.EstPostePrincipal ||
                                           string.Equals(db.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(db.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);

                        SelectedPosteMode = isPrincipal ? "PrincipalPostgres" : "SecondairePostgres";
                        bool online = _caisseServices?.IsOnline ?? true;
                        CurrentDbModeDisplay = isPrincipal
                            ? $"{(online ? "🟢" : "🟠")} Poste Principal (Serveur PostgreSQL) — Port {db.Port} / Base « {db.Database} »" +
                              (!string.IsNullOrEmpty(ts.TailscaleIp) ? $" — IP Tailscale : {ts.TailscaleIp}" : "")
                            : $"{(online ? "🟢 Connecté au Poste Principal" : "🟠 Hors-ligne (File locale active)")} — {db.Host}:{db.Port}/{db.Database}";
                    }
                    else
                    {
                        SelectedPosteMode = "LocalSqlite";
                        var networkService = NetworkDatabaseService.Instance;
                        var networkConfig = networkService.LoadConfig();
                        IsNetworkDbEnabled = networkConfig.Enabled;
                        NetworkDbPath = networkConfig.NetworkDbPath;
                        CurrentDbModeDisplay = $"🔵 Mode SQLite Local — {db.SqlitePath ?? AppPaths.DefaultSqlitePath}";
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn("Settings", $"Erreur chargement poste.json : {ex.Message}");
            }

            // 2. Charger la configuration métier en base
            try
            {
                using var context = _contextFactory.CreateDbContext();
                EntrepriseInfo = context.Entreprise.FirstOrDefault() ?? new Entreprise();
                
                if (string.IsNullOrWhiteSpace(EntrepriseInfo.LogoPath))
                {
                    var defaultLogo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "logo.png");
                    if (File.Exists(defaultLogo))
                    {
                        EntrepriseInfo.LogoPath = defaultLogo;
                    }
                }

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

                var scaleEnabled = context.Configuration.Find("scale_enabled");
                if (scaleEnabled != null && bool.TryParse(scaleEnabled.Valeur, out bool se)) IsScaleEnabled = se;

                var scalePort = context.Configuration.Find("scale_port_name");
                if (scalePort != null && !string.IsNullOrWhiteSpace(scalePort.Valeur)) ScalePortName = scalePort.Valeur;

                var scaleBaud = context.Configuration.Find("scale_baud_rate");
                if (scaleBaud != null && int.TryParse(scaleBaud.Valeur, out int sb)) ScaleBaudRate = sb;

                var tpeEnabled = context.Configuration.Find("tpe_enabled");
                if (tpeEnabled != null && bool.TryParse(tpeEnabled.Valeur, out bool tpe)) IsTPEEnabled = tpe;

                var tpePort = context.Configuration.Find("tpe_port_name");
                if (tpePort != null && !string.IsNullOrWhiteSpace(tpePort.Valeur)) TpePortName = tpePort.Valeur;

                var tpeBaud = context.Configuration.Find("tpe_baud_rate");
                if (tpeBaud != null && int.TryParse(tpeBaud.Valeur, out int tb)) TpeBaudRate = tb;

                var tpeTimeout = context.Configuration.Find("tpe_timeout");
                if (tpeTimeout != null && int.TryParse(tpeTimeout.Valeur, out int tt)) TpeTimeout = tt;

                var topCount = context.Configuration.Find("top_products_count");
                if (topCount != null && int.TryParse(topCount.Valeur, out int tc)) TopProductsCount = tc;

                var showAll = context.Configuration.Find("show_all_top_products");
                if (showAll != null && bool.TryParse(showAll.Valeur, out bool sa)) ShowAllTopProducts = sa;

                LoadDisplayPromotionsSelection(context);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Settings", $"Lecture base en mode hors-ligne ou erreur : {ex.Message}");
                if (EntrepriseInfo == null) EntrepriseInfo = new Entreprise { Nom = "Mon Magasin" };
            }
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

        [RelayCommand]
        private void RefreshScreens()
        {
            LoadScreens();
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
        private async Task SelectLogo()
        {
            var openDlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Sélectionner le logo de l'entreprise"
            };

            if (openDlg.ShowDialog(Services.WindowHelper.GetAdminWindow()) == true)
            {
                try
                {
                    if (_caisseServices != null)
                    {
                        var key = await _caisseServices.Images.SaveFileAsync($"Images/Logo/{Path.GetFileName(openDlg.FileName)}", openDlg.FileName);
                        var cached = _caisseServices.Images.Resolve(key);
                        EntrepriseInfo.LogoPath = cached ?? openDlg.FileName;
                    }
                    else
                    {
                        EntrepriseInfo.LogoPath = openDlg.FileName;
                    }
                }
                catch
                {
                    EntrepriseInfo.LogoPath = openDlg.FileName;
                }
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
                // Enregistrer aussi le nom du poste et les périphériques locaux dans poste.json
                _localStore.Update(s =>
                {
                    if (!string.IsNullOrWhiteSpace(NomPoste))
                        s.NomPoste = NomPoste.Trim();
                    s.Peripheriques.Imprimante = SelectedPrinter;
                    s.Peripheriques.LargeurTicketMm = TicketWidth;
                    s.Peripheriques.AfficherLogoTicket = ShowLogoOnTicket;
                    s.Peripheriques.EcranClientActif = IsCustomerDisplayEnabled;
                    s.Peripheriques.EcranClientIndex = SelectedScreenIndex;
                    s.Peripheriques.EcranClientPromotions = ShowCustomerDisplayPromotions;
                    s.Peripheriques.EcranClientCompact = IsCompactCustomerDisplay;
                    s.Peripheriques.BalanceActive = IsScaleEnabled;
                    s.Peripheriques.BalancePort = ScalePortName;
                    s.Peripheriques.BalanceBaud = ScaleBaudRate;
                    s.Peripheriques.TpeActif = IsTPEEnabled;
                    s.Peripheriques.TpePort = TpePortName;
                    s.Peripheriques.TpeBaud = TpeBaudRate;
                    s.Peripheriques.TpeTimeout = TpeTimeout;
                    s.Peripheriques.ModeFormation = IsTrainingMode;
                });

                using var context = await _contextFactory.CreateDbContextAsync();
                
                context.Update(EntrepriseInfo);

                UpdateConfig(context, "ticket_width", TicketWidth.ToString());
                UpdateConfig(context, "imprimante_defaut", SelectedPrinter);
                UpdateConfig(context, "training_mode", IsTrainingMode.ToString());
                UpdateConfig(context, "customer_display_enabled", IsCustomerDisplayEnabled.ToString());
                UpdateConfig(context, "customer_display_screen_index", SelectedScreenIndex.ToString());
                UpdateConfig(context, "customer_display_show_promotions", ShowCustomerDisplayPromotions.ToString());
                UpdateConfig(context, "customer_display_compact", IsCompactCustomerDisplay.ToString());
                UpdateConfig(context, "top_products_count", TopProductsCount.ToString());
                UpdateConfig(context, "show_all_top_products", ShowAllTopProducts.ToString());

                UpdateConfig(context, "scale_enabled", IsScaleEnabled.ToString());
                UpdateConfig(context, "scale_port_name", ScalePortName);
                UpdateConfig(context, "scale_baud_rate", ScaleBaudRate.ToString());

                UpdateConfig(context, "tpe_enabled", IsTPEEnabled.ToString());
                UpdateConfig(context, "tpe_port_name", TpePortName);
                UpdateConfig(context, "tpe_baud_rate", TpeBaudRate.ToString());
                UpdateConfig(context, "tpe_timeout", TpeTimeout.ToString());

                var selectedIds = AvailableDisplayPromotions.Where(p => p.IsSelected).Select(p => p.Promotion.Id);
                UpdateConfig(context, "customer_display_promotions", string.Join(",", selectedIds));

                await context.SaveChangesAsync();
                await DataVersionBump.BumpAsync(context, CancellationToken.None, DataDomains.Configuration);
                
                var owner = Services.WindowHelper.GetAdminWindow();
                
                var mainWin = owner as SystemeCaisse.UI.MainWindow;
                if (mainWin != null && mainWin.DataContext is MainViewModel mainVM)
                {
                    mainVM.ReloadEntrepriseInfo();
                }

                MessageBox.Show(owner, "Paramètres enregistrés avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);

                if (_scaleSettingsChanged || _tpeSettingsChanged)
                {
                    _scaleSettingsChanged = false;
                    _tpeSettingsChanged = false;
                    var result = MessageBox.Show(owner,
                        "Les paramètres des périphériques ont été modifiés.\nVoulez-vous redémarrer l'application maintenant pour appliquer les changements ?",
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
                MessageBox.Show(owner, $"Erreur lors de l'enregistrement : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private string ResolveActiveDbPath()
        {
            if (_contextFactory is CaisseDbContextFactory caisseFactory &&
                !string.IsNullOrWhiteSpace(caisseFactory.SqliteFilePath))
            {
                return caisseFactory.SqliteFilePath;
            }

            try
            {
                var netPath = NetworkDatabaseService.Instance.CurrentDbPath;
                if (!string.IsNullOrWhiteSpace(netPath))
                    return netPath;
            }
            catch { }

            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "caisse.db");
            if (!File.Exists(dbPath))
            {
                var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName) ?? AppDomain.CurrentDomain.BaseDirectory;
                dbPath = Path.Combine(exeDir, "caisse.db");
            }
            return dbPath;
        }

        [RelayCommand]
        private async Task BackupDatabase()
        {
            var owner = Services.WindowHelper.GetAdminWindow();
            try
            {
                if (_contextFactory is CaisseDbContextFactory caisseFactory && caisseFactory.Provider == DatabaseProvider.PostgreSql)
                {
                    var savePgDlg = new Microsoft.Win32.SaveFileDialog
                    {
                        FileName = $"caisse_backup_{DateTime.Now:yyyyMMdd_HHmmss}{DataTransferService.BackupExtension}",
                        Filter = $"Sauvegarde Système Caisse (*{DataTransferService.BackupExtension})|*{DataTransferService.BackupExtension}"
                    };

                    if (savePgDlg.ShowDialog(owner) == true)
                    {
                        var transfer = new DataTransferService(caisseFactory);
                        await transfer.ExportBackupAsync(savePgDlg.FileName);
                        MessageBox.Show(owner, $"Sauvegarde complète PostgreSQL (données + images) exportée avec succès :\n{savePgDlg.FileName}", "Sauvegarde effectuée", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                string dbPath = ResolveActiveDbPath();
                if (!File.Exists(dbPath))
                {
                    MessageBox.Show(owner, $"Base de données introuvable : {dbPath}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var saveDlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"caisse_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
                    Filter = $"SQLite Database (*.db)|*.db|Sauvegarde Système Caisse (*{DataTransferService.BackupExtension})|*{DataTransferService.BackupExtension}"
                };

                if (saveDlg.ShowDialog(owner) == true)
                {
                    if (saveDlg.FileName.EndsWith(DataTransferService.BackupExtension, StringComparison.OrdinalIgnoreCase) &&
                        _contextFactory is CaisseDbContextFactory cf)
                    {
                        var transfer = new DataTransferService(cf);
                        await transfer.ExportBackupAsync(saveDlg.FileName);
                    }
                    else
                    {
                        try
                        {
                            using var source = new Microsoft.Data.Sqlite.SqliteConnection(CaisseDbContextFactory.BuildSqliteConnectionString(dbPath, false));
                            using var dest = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={saveDlg.FileName};Pooling=False");
                            source.Open();
                            dest.Open();
                            source.BackupDatabase(dest);
                        }
                        catch
                        {
                            File.Copy(dbPath, saveDlg.FileName, true);
                        }
                    }
                    MessageBox.Show(owner, "Sauvegarde effectuée !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, $"Erreur sauvegarde : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task RestoreDatabase()
        {
            var mainWin = Services.WindowHelper.GetAdminWindow();
            try
            {
                var openDlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = $"Toutes les sauvegardes (*.db;*{DataTransferService.BackupExtension})|*.db;*{DataTransferService.BackupExtension}|SQLite Database (*.db)|*.db|Sauvegarde Caisse (*{DataTransferService.BackupExtension})|*{DataTransferService.BackupExtension}",
                    Title = "Sélectionner la sauvegarde à restaurer"
                };

                if (openDlg.ShowDialog(mainWin) != true) return;

                var confirm = MessageBox.Show(mainWin, 
                    "⚠️ ATTENTION : Cela remplacera TOUTES les données actuelles par celles de la sauvegarde sélectionnée.\n\nÊtes-vous sûr de vouloir continuer ?", 
                    "Restauration de sauvegarde", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (confirm != MessageBoxResult.Yes) return;

                // Cas 1 : Cible PostgreSQL ou fichier .caisse-backup -> utiliser DataTransferService
                if (_contextFactory is CaisseDbContextFactory activeFactory &&
                    (activeFactory.Provider == DatabaseProvider.PostgreSql ||
                     openDlg.FileName.EndsWith(DataTransferService.BackupExtension, StringComparison.OrdinalIgnoreCase)))
                {
                    var transfer = new DataTransferService(activeFactory);
                    TransferReport report;
                    if (openDlg.FileName.EndsWith(DataTransferService.BackupExtension, StringComparison.OrdinalIgnoreCase))
                        report = await transfer.RestoreBackupAsync(openDlg.FileName);
                    else
                        report = await transfer.ImportSqliteAsync(openDlg.FileName, replaceExisting: true);

                    try { RepairProductImagePaths(); } catch { }

                    if (Application.Current.MainWindow?.DataContext is MainViewModel mainVM)
                    {
                        await mainVM.ReloadAllDataAfterRestoreAsync();
                    }

                    if (MessageBox.Show(mainWin,
                        $"Sauvegarde restaurée avec succès ({report.TotalRows} enregistrements, {report.ImagesImported} images) !\n\nSouhaitez-vous redémarrer l'application maintenant ?",
                        "Restauration terminée", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        RestartApplication();
                    }
                    return;
                }

                // Cas 2 : Cible SQLite locale avec fichier .db
                string dbPath = ResolveActiveDbPath();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                try
                {
                    using var source = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={openDlg.FileName};Mode=ReadOnly;Pooling=False");
                    using var dest = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False");
                    source.Open();
                    dest.Open();
                    source.BackupDatabase(dest);
                }
                catch
                {
                    try { if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal"); } catch { }
                    try { if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm"); } catch { }
                    File.Copy(openDlg.FileName, dbPath, true);
                }

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                var dbSettings = new DatabaseSettings
                {
                    Provider = DatabaseProvider.Sqlite,
                    SqlitePath = dbPath
                };
                var caisseFactory = new CaisseDbContextFactory(dbSettings);
                var bootstrapper = new DatabaseBootstrapper(caisseFactory);
                await bootstrapper.InitializeAsync(backupBeforeMigration: false);

                try { RepairProductImagePaths(); } catch { }

                if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
                {
                    await vm.ReloadAllDataAfterRestoreAsync();
                }
                
                if (MessageBox.Show(mainWin, "Sauvegarde restaurée avec succès et données rechargées !\n\nSouhaitez-vous redémarrer l'application maintenant ?", 
                    "Restauration terminée", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    RestartApplication();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(mainWin, $"Erreur restauration : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RepairProductImagePaths()
        {
            try
            {
                string imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Produits");
                if (!Directory.Exists(imagesDir)) return;

                using var context = _contextFactory.CreateDbContext();
                var produits = context.Produits.ToList();
                var imageFiles = Directory.GetFiles(imagesDir);
                int repaired = 0;

                foreach (var produit in produits)
                {
                    if (!string.IsNullOrWhiteSpace(produit.ImagePath))
                    {
                        string fullPath = Path.IsPathRooted(produit.ImagePath) 
                            ? produit.ImagePath 
                            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, produit.ImagePath);
                        
                        if (File.Exists(fullPath)) continue;
                    }

                    var matchingFile = imageFiles.FirstOrDefault(f =>
                    {
                        var fileName = Path.GetFileNameWithoutExtension(f);
                        return fileName.StartsWith($"produit_{produit.Id}_") || fileName == $"produit_{produit.Id}";
                    });

                    if (matchingFile != null)
                    {
                        var relativePath = Path.Combine("Images", "Produits", Path.GetFileName(matchingFile));
                        produit.ImagePath = relativePath;
                        repaired++;
                    }
                }

                if (repaired > 0)
                {
                    context.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RepairProductImagePaths error: {ex.Message}");
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

        // === Multi-Postes PostgreSQL & Tailscale Commands ===

        [RelayCommand]
        private void SelectPosteMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return;
            SelectedPosteMode = mode;
            if (mode == "PrincipalPostgres")
            {
                PgHost = "localhost";
                if (PgPort <= 0) PgPort = 5432;
            }
            else if (mode == "SecondairePostgres" && string.Equals(PgHost, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                PgHost = "";
            }
        }

        [RelayCommand]
        private void RefreshTailscaleStatus()
        {
            var ts = PostgresProvisioningService.GetNetworkAndTailscaleStatus();
            TailscaleStatusDisplay = ts.StatusSummary;
            TailscaleIpAddress = ts.TailscaleIp ?? ts.LanIp ?? "";
            PgProvisioningStatus = $"🔄 Réseau actualisé : {ts.StatusSummary}";
        }

        [RelayCommand]
        private void CopyTailscaleIp()
        {
            if (string.IsNullOrWhiteSpace(TailscaleIpAddress)) return;
            try
            {
                Clipboard.SetText(TailscaleIpAddress);
                PgProvisioningStatus = $"📋 Adresse IP « {TailscaleIpAddress} » copiée dans le presse-papiers ! Saisissez-la sur les Postes Secondaires.";
            }
            catch { }
        }

        [RelayCommand]
        private async Task ProvisionPrincipalServer()
        {
            if (IsProvisioningBusy) return;
            var owner = Services.WindowHelper.GetAdminWindow();

            var confirm = MessageBox.Show(owner,
                "Ce poste va être configuré automatiquement comme POSTE PRINCIPAL (Serveur PostgreSQL Multi-Postes) :\n\n" +
                "1. Démarrage et maintien du service Tailscale\n" +
                "2. Détection ou installation automatique de PostgreSQL\n" +
                "3. Ouverture réseau (postgresql.conf, pg_hba.conf pour Tailscale & LAN, Pare-feu Windows)\n" +
                "4. Création de la base « caisse » et de toutes les tables\n" +
                "5. Reprise automatique de vos données et photos actuelles\n\n" +
                "Voulez-vous lancer la configuration autonome maintenant ?",
                "Configuration Autonome Poste Principal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsProvisioningBusy = true;
            PgProvisioningStatus = "⏳ Démarrage de la configuration autonome du Poste Principal…";

            try
            {
                if (!string.IsNullOrWhiteSpace(NomPoste))
                {
                    _localStore.Update(s => s.NomPoste = NomPoste.Trim());
                }

                var progress = new Progress<string>(msg =>
                {
                    PgProvisioningStatus = $"⏳ {msg}";
                });

                var provisioning = _caisseServices?.Provisioning ?? new PostgresProvisioningService(_localStore);
                var result = await Task.Run(() => provisioning.ProvisionPrincipalServerAsync(
                    PgDatabase,
                    PgUsername,
                    PgPassword,
                    progress));

                var ts = PostgresProvisioningService.GetNetworkAndTailscaleStatus();
                TailscaleStatusDisplay = ts.StatusSummary;
                TailscaleIpAddress = ts.TailscaleIp ?? ts.LanIp ?? "";

                if (result.Success)
                {
                    PgPort = result.Port;
                    PgHost = "localhost";
                    PgProvisioningStatus = result.Message;
                    CurrentDbModeDisplay = $"🟢 Poste Principal (Serveur PostgreSQL) — Port {result.Port} / Base « {PgDatabase} »" +
                                           (!string.IsNullOrEmpty(result.TailscaleIp) ? $" — IP Tailscale : {result.TailscaleIp}" : "");

                    var restart = MessageBox.Show(owner,
                        $"{result.Message}\n\nVoulez-vous redémarrer l'application maintenant pour basculer sur le serveur PostgreSQL ?",
                        "Poste Principal Configuré",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (restart == MessageBoxResult.Yes)
                    {
                        RestartApplication();
                    }
                }
                else
                {
                    PgProvisioningStatus = $"❌ {result.Message}";
                    MessageBox.Show(owner, result.Message, "Erreur de configuration", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                PgProvisioningStatus = $"❌ Erreur : {ex.Message}";
                MessageBox.Show(owner, $"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProvisioningBusy = false;
            }
        }

        [RelayCommand]
        private async Task TestPostgresConnection()
        {
            if (IsProvisioningBusy) return;
            var owner = Services.WindowHelper.GetAdminWindow();

            string targetHost = IsPrincipalPostgresMode ? "localhost" : PgHost?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(targetHost))
            {
                PgProvisioningStatus = "❌ Veuillez saisir l'adresse IP Tailscale (ex: 100.x.y.z) du Poste Principal.";
                return;
            }

            IsProvisioningBusy = true;
            PgProvisioningStatus = $"⏳ Test de connexion PostgreSQL vers {targetHost}:{PgPort}…";

            try
            {
                var provisioning = _caisseServices?.Provisioning ?? new PostgresProvisioningService(_localStore);
                var testRes = await provisioning.TestAndPrepareSecondaryConnectionAsync(
                    targetHost,
                    PgPort,
                    PgDatabase,
                    PgUsername,
                    PgPassword);

                if (testRes.Success)
                {
                    PgPort = testRes.ResolvedPort;
                    PgProvisioningStatus = testRes.Message;
                    MessageBox.Show(owner, testRes.Message, "Connexion PostgreSQL Réussie", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    PgProvisioningStatus = $"❌ {testRes.Message}";
                    MessageBox.Show(owner, testRes.Message, "Échec de connexion PostgreSQL", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                PgProvisioningStatus = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                IsProvisioningBusy = false;
            }
        }

        [RelayCommand]
        private async Task SavePostgresSettings()
        {
            if (IsProvisioningBusy) return;
            var owner = Services.WindowHelper.GetAdminWindow();

            bool isPrincipal = IsPrincipalPostgresMode;
            string targetHost = isPrincipal ? "localhost" : (PgHost ?? "").Trim();

            if (!isPrincipal && string.IsNullOrWhiteSpace(targetHost))
            {
                PgProvisioningStatus = "❌ Veuillez saisir l'adresse IP Tailscale (ex: 100.x.y.z) du Poste Principal.";
                MessageBox.Show(owner, PgProvisioningStatus, "Adresse IP requise", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsProvisioningBusy = true;
            PgProvisioningStatus = $"⏳ Vérification de la connexion PostgreSQL sur {targetHost}:{PgPort}…";

            try
            {
                var provisioning = _caisseServices?.Provisioning ?? new PostgresProvisioningService(_localStore);
                var testRes = await provisioning.TestAndPrepareSecondaryConnectionAsync(
                    targetHost,
                    PgPort,
                    PgDatabase,
                    PgUsername,
                    PgPassword);

                int finalPort = testRes.Success ? testRes.ResolvedPort : PgPort;

                if (!testRes.Success)
                {
                    var saveAnyway = MessageBox.Show(owner,
                        $"{testRes.Message}\n\nVoulez-vous quand même enregistrer cette configuration (l'application se connectera dès que le Poste Principal sera joignable) ?",
                        "Serveur actuellement injoignable",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (saveAnyway != MessageBoxResult.Yes)
                    {
                        PgProvisioningStatus = $"❌ {testRes.Message}";
                        return;
                    }
                }

                PgPort = finalPort;

                _localStore.Update(s =>
                {
                    if (!string.IsNullOrWhiteSpace(NomPoste))
                        s.NomPoste = NomPoste.Trim();
                    s.EstPostePrincipal = isPrincipal;
                    s.Database.Provider = DatabaseProvider.PostgreSql;
                    s.Database.Host = targetHost;
                    s.Database.Port = finalPort;
                    s.Database.Database = string.IsNullOrWhiteSpace(PgDatabase) ? "caisse" : PgDatabase.Trim();
                    s.Database.Username = string.IsNullOrWhiteSpace(PgUsername) ? "caisse" : PgUsername.Trim();
                    s.Database.Password = string.IsNullOrWhiteSpace(PgPassword) ? "caisse" : PgPassword;
                });

                try
                {
                    var legacyJson = Path.Combine(AppPaths.InstallDir, "network_db.json");
                    if (File.Exists(legacyJson)) File.Delete(legacyJson);
                }
                catch { }

                PgProvisioningStatus = $"✅ Configuration enregistrée ({targetHost}:{finalPort}/{PgDatabase}).";

                var restart = MessageBox.Show(owner,
                    $"Configuration Multi-Postes PostgreSQL enregistrée !\n\n" +
                    $"• Rôle : {(isPrincipal ? "Poste Principal (Serveur)" : "Poste Secondaire")}\n" +
                    $"• Nom du poste : {NomPoste}\n" +
                    $"• Serveur : {targetHost}:{finalPort}/{PgDatabase}\n\n" +
                    "L'application doit redémarrer pour appliquer la connexion. Redémarrer maintenant ?",
                    "Configuration Multi-Postes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (restart == MessageBoxResult.Yes)
                {
                    RestartApplication();
                }
            }
            catch (Exception ex)
            {
                PgProvisioningStatus = $"❌ Erreur : {ex.Message}";
                MessageBox.Show(owner, $"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProvisioningBusy = false;
            }
        }

        [RelayCommand]
        private void SwitchToLocalSqlite()
        {
            var owner = Services.WindowHelper.GetAdminWindow();
            var confirm = MessageBox.Show(owner,
                "Voulez-vous basculer ce poste en mode SQLite Local autonome (sans serveur PostgreSQL partagé) ?",
                "Mode SQLite Local",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            _localStore.Update(s =>
            {
                if (!string.IsNullOrWhiteSpace(NomPoste))
                    s.NomPoste = NomPoste.Trim();
                s.EstPostePrincipal = true;
                s.Database.Provider = DatabaseProvider.Sqlite;
                if (string.IsNullOrWhiteSpace(s.Database.SqlitePath) || s.Database.SqlitePath.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    var localCandidate = Path.Combine(AppPaths.InstallDir, "caisse.db");
                    s.Database.SqlitePath = File.Exists(localCandidate) ? localCandidate : AppPaths.DefaultSqlitePath;
                }
            });

            if (MessageBox.Show(owner,
                "Mode SQLite Local activé. Redémarrer l'application maintenant ?",
                "Redémarrage requis",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                RestartApplication();
            }
        }

        // === Legacy SMB Network Database Commands ===

        [RelayCommand]
        private void TestNetworkDb()
        {
            if (string.IsNullOrWhiteSpace(NetworkDbPath))
            {
                NetworkDbTestStatus = "❌ Veuillez saisir un chemin réseau.";
                return;
            }

            NetworkDbTestStatus = "⏳ Test en cours...";
            bool accessible = NetworkDatabaseService.TestNetworkPath(NetworkDbPath);
            if (accessible)
            {
                NetworkDbTestStatus = $"✅ Chemin accessible : {NetworkDbPath}";
                MessageBox.Show(Application.Current.MainWindow, $"Connexion réussie au dossier réseau :\n{NetworkDbPath}", "Test Réseau", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                NetworkDbTestStatus = $"❌ Chemin inaccessible : {NetworkDbPath}";
                MessageBox.Show(Application.Current.MainWindow, 
                    $"IMPOSSIBLE d'accéder au dossier réseau :\n{NetworkDbPath}",
                    "Erreur Réseau", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void BrowseNetworkDb()
        {
            var openDlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                Title = "Sélectionner la base de données réseau",
                FileName = "caisse.db"
            };

            try
            {
                string? dir = Path.GetDirectoryName(NetworkDbPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    openDlg.InitialDirectory = dir;
            }
            catch { }

            if (openDlg.ShowDialog(Services.WindowHelper.GetAdminWindow()) == true)
            {
                NetworkDbPath = openDlg.FileName;
                TestNetworkDb();
            }
        }

        [RelayCommand]
        private void SaveNetworkDb()
        {
            try
            {
                var networkService = NetworkDatabaseService.Instance;
                networkService.SaveConfig(new NetworkDbConfig
                {
                    Enabled = IsNetworkDbEnabled,
                    NetworkDbPath = NetworkDbPath
                });

                var owner = Services.WindowHelper.GetAdminWindow();
                var result = MessageBox.Show(owner,
                    $"Configuration sauvegardée !\nRedémarrer maintenant ?",
                    "Configuration Réseau",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    RestartApplication();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(),
                    $"Erreur : {ex.InnerException?.Message ?? ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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

        [RelayCommand]
        private async Task TestTPEConnection()
        {
            if (string.IsNullOrWhiteSpace(TpePortName))
            {
                TpeTestStatus = "❌ Veuillez sélectionner un port COM.";
                return;
            }

            TpeTestStatus = "⏳ Test de connexion au TPE en cours...";

            VerifonePaymentTerminalService? activeService = null;
            var mainWin = Application.Current.MainWindow;
            if (mainWin?.DataContext is MainViewModel mainVM)
            {
                activeService = mainVM.TPEService;
            }

            bool success = await Task.Run(() =>
            {
                try
                {
                    return VerifonePaymentTerminalService.TestConnection(TpePortName, TpeBaudRate, activeService);
                }
                catch
                {
                    return false;
                }
            });

            TpeTestStatus = success
                ? $"✅ TPE détecté sur {TpePortName} à {TpeBaudRate} baud !"
                : $"❌ Aucun TPE détecté sur {TpePortName}. Vérifiez le branchement USB et que le TPE est en mode Caisse.";
        }

        [RelayCommand]
        private async Task AutoDetectTPE()
        {
            TpeTestStatus = "🔎 Recherche de TPE sur tous les ports COM...";

            string? detectedPort = await Task.Run(() =>
            {
                try
                {
                    return VerifonePaymentTerminalService.DetectTerminal(TpeBaudRate);
                }
                catch
                {
                    return null;
                }
            });

            if (detectedPort != null)
            {
                TpePortName = detectedPort;
                TpeTestStatus = $"✅ TPE trouvé automatiquement sur {detectedPort} !";
            }
            else
            {
                TpeTestStatus = "❌ Aucun TPE détecté sur les ports COM disponibles. Vérifiez que le driver USB Verifone est installé.";
            }
        }
    }
}
