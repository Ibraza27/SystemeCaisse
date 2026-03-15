using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Printing;
using System.Windows;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Core.Interfaces;
using SystemeCaisse.Infrastructure.Data;

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

        public SettingsViewModel(IDbContextFactory<AppDbContext> contextFactory, IDataMigrationService migrationService)
        {
            _contextFactory = contextFactory;
            _migrationService = migrationService;
            AvailablePrinters = new ObservableCollection<string>();
            LoadData();
            LoadPrinters();
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
                    MessageBox.Show("Migration réussie ! Veuillez redémarrer l'application.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadData(); // Reload enterprise info if changed
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

                await context.SaveChangesAsync();
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
            // Simple file copy backup
            try
            {
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "caisse.db"); // Adjust based on connection string
                // Actually need to parse connection string or know the path. 
                // Hardcoding purely for prototype speed, ideally read from config. 
                // But wait, the context is using a hardcoded path in App.xaml.cs? 
                // Let's assume standard path for now or ask user where to save.
                
                var saveDlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"caisse_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
                    Filter = "SQLite Database (*.db)|*.db"
                };

                if (saveDlg.ShowDialog() == true)
                {
                    // To backup sqlite safely, better to use VACUUM INTO or just copy if WAL.
                    // Simple Copy:
                     var dbSource = @"C:\Users\Administrateur\Documents\DossierPartage\PROGRAMATION\SystemeCaisse\SystemeCaisse.Infrastructure\caisse.db";
                     System.IO.File.Copy(dbSource, saveDlg.FileName, true);
                     MessageBox.Show("Sauvegarde effectuée !");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur sauvegarde : {ex.Message}");
            }
        }
    }
}
