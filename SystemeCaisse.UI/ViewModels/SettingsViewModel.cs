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
using System.Linq;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.ViewModels
{
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
        private ObservableCollection<string> _availableScreens = new();

        public SettingsViewModel(IDbContextFactory<AppDbContext> contextFactory, IDataMigrationService migrationService)
        {
            _contextFactory = contextFactory;
            _migrationService = migrationService;
            AvailablePrinters = new ObservableCollection<string>();
            AvailableScreens = new ObservableCollection<string>();
            LoadData();
            LoadPrinters();
            LoadScreens();
        }

        [RelayCommand]
        private async Task ImportData()
        {
            var openDlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQLite Database (database.db)|database.db|All Files (*.*)|*.*",
                Title = "Sélectionner la base de données Python (database.db)"
            };

            if (openDlg.ShowDialog() == true)
            {
                try 
                {
                    await _migrationService.MigrateDataAsync(openDlg.FileName);
                    if (MessageBox.Show("Migration réussie ! L'application doit redémarrer pour appliquer les changements. Redémarrer maintenant ?", 
                        "Succès", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        RestartApplication();
                    }
                    LoadData(); 
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur migration : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadData()
        {
            using var context = _contextFactory.CreateDbContext();
            EntrepriseInfo = context.Entreprise.FirstOrDefault() ?? new Entreprise();
            
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
                MessageBox.Show($"Erreur lors du chargement des imprimantes : {ex.Message}");
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
                var mainWin = Application.Current.Windows.OfType<SystemeCaisse.UI.MainWindow>().FirstOrDefault();
                if (mainWin != null)
                {
                    mainWin.ReinitializeCustomerDisplay();
                }
                
                MessageBox.Show("Affichage client rafraîchi !", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'application : {ex.Message}");
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

                await context.SaveChangesAsync();
                
                // Refresh Customer Display
                // Refresh Customer Display
                var mainWin = Application.Current.Windows.OfType<SystemeCaisse.UI.MainWindow>().FirstOrDefault();
                if (mainWin != null)
                {
                    mainWin.ReinitializeCustomerDisplay();
                }

                MessageBox.Show("Paramètres enregistrés avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'enregistrement : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show($"Base de données introuvable : {dbPath}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var saveDlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"caisse_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
                    Filter = "SQLite Database (*.db)|*.db"
                };

                if (saveDlg.ShowDialog() == true)
                {
                     File.Copy(dbPath, saveDlg.FileName, true);
                     MessageBox.Show("Sauvegarde effectuée !");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur sauvegarde : {ex.Message}");
            }
        }
        [RelayCommand]
        private void ResetDatabase()
        {
            var result = MessageBox.Show("ÊTES-VOUS SÛR ? Cela effacera TOUTES les données (produits, ventes, historique). Cette action est irréversible.", 
                "AVERTISSEMENT CRITIQUE", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    using var context = _contextFactory.CreateDbContext();
                    context.Database.EnsureDeleted();
                    context.Database.Migrate();
                    
                    if (MessageBox.Show("Base de données réinitialisée ! Redémarrer l'application pour finaliser ?", 
                        "Réinitialisation terminée", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        RestartApplication();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de la réinitialisation : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}
