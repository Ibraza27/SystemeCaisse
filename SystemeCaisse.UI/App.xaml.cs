using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.UI.ViewModels;
using SystemeCaisse.UI.Services;
using SystemeCaisse.Core.Interfaces;
using SystemeCaisse.Infrastructure.Services;

namespace SystemeCaisse.UI
{
    public partial class App : Application
    {
        private readonly IHost _host;

        private static DateTime _lastLogTime = DateTime.MinValue;

        private void LogException(Exception? ex, string source)
        {
            if (ex == null) return;
            
            // Throttle logging to once every 2 seconds to prevent disk/UI saturation during loops
            if ((DateTime.Now - _lastLogTime).TotalSeconds < 2) return;
            _lastLogTime = DateTime.Now;

            var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
            var message = $"[{DateTime.Now}] Source: {source}\nException: {ex}\nInner: {ex.InnerException}\nStack: {ex.StackTrace}\n\n";
            System.IO.File.AppendAllText(logPath, message);
        }

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Use a path relative to the application executable
                    string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "caisse.db");
                    string connectionString = $"Data Source={dbPath}";

                    services.AddDbContextFactory<AppDbContext>(options =>
                    {
                        options.UseSqlite(connectionString);
                    });

                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<PrintService>();
                    services.AddTransient<MainViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<DashboardViewModel>();
                    services.AddTransient<PromotionsViewModel>();
                    services.AddTransient<ProductsViewModel>();
                    services.AddTransient<StocksViewModel>();
                    services.AddTransient<InventoryViewModel>();
                    services.AddTransient<HistoryViewModel>();
                    services.AddTransient<AnalysisViewModel>();

                    services.AddSingleton<IScaleService, SimulatedScaleService>();
                    services.AddSingleton<IPaymentTerminalService, SimulatedPaymentTerminalService>();
                    services.AddTransient<IDataMigrationService, DataMigrationService>();
                })
                .Build();

            // Setup global exception handling
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogException(e.ExceptionObject as Exception, "AppDomain");
            DispatcherUnhandledException += (s, e) => 
            { 
                LogException(e.Exception, "Dispatcher"); 
                
                // CRITICAL: We now force Handled = true to prevent the app from closing.
                // This allows the user to continue using other tabs even if an error occurred in Analysis.
                e.Handled = true;

                // Show a non-blocking error message
                MessageBox.Show(
                    "Une erreur mineure est survenue lors de l'affichage des graphiques. " +
                    "Vous pouvez continuer à utiliser l'application.\n\nDétails de l'erreur : " + e.Exception.Message,
                    "Information de Stabilité", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
            };
            TaskScheduler.UnobservedTaskException += (s, e) => { LogException(e.Exception, "TaskScheduler"); e.SetObserved(); };
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                // Force French Culture for Currency (€)
                var cultureInfo = new System.Globalization.CultureInfo("fr-FR");
                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
                FrameworkElement.LanguageProperty.OverrideMetadata(
                    typeof(FrameworkElement),
                    new FrameworkPropertyMetadata(
                        System.Windows.Markup.XmlLanguage.GetLanguage(cultureInfo.IetfLanguageTag)));

                await _host.StartAsync();

                // Show Splash Screen
                var splash = new Views.SplashScreen();
                splash.Show();

                await Task.Run(async () => 
                {
                    try 
                    {
                        // Seed Data if empty
                        var factory = _host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
                        using (var context = factory.CreateDbContext())
                        {
                            await context.Database.MigrateAsync();
                            
                            if (!context.Produits.Any())
                            {
                                context.Produits.AddRange(
                                    new Core.Entities.Produit { Nom = "Pomme Golden", PrixVente = 1.99m, StockActuel = 100, CodeBarre = "1001" },
                                    new Core.Entities.Produit { Nom = "Banane Cavendish", PrixVente = 0.99m, StockActuel = 50, CodeBarre = "1002" },
                                    new Core.Entities.Produit { Nom = "Tomate Grappe", PrixVente = 2.50m, StockActuel = 30, CodeBarre = "1003" },
                                    new Core.Entities.Produit { Nom = "Courgette Bio", PrixVente = 1.50m, StockActuel = 20, CodeBarre = "1004" },
                                    new Core.Entities.Produit { Nom = "Poivron Rouge", PrixVente = 3.00m, StockActuel = 40, CodeBarre = "1005" },
                                    new Core.Entities.Produit { Nom = "Salade Batavia", PrixVente = 1.20m, StockActuel = 15, CodeBarre = "1006" }
                                );
                                await context.SaveChangesAsync();
                            }

                            // Ensure Enterprise data is correct
                            var entreprise = await context.Entreprise.FirstOrDefaultAsync();
                            if (entreprise == null)
                            {
                                entreprise = new Core.Entities.Entreprise();
                                context.Entreprise.Add(entreprise);
                            }
                            
                            entreprise.Nom = "HIPPOCAMPE IMPORT-EXPORT";
                            entreprise.Adresse = "5 - 7 Rue Pascal 33370 Tresses, France\nLUN - SAM : 9h - 18h | DIMANCHE : 10H - 12H"; 
                            entreprise.Telephone = "06 99 79 16 98 / 06 99 56 93 58";
                            entreprise.LogoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "logo.png");
                            
                            await context.SaveChangesAsync();

                            // Update Splash Logo on UI Thread
                            var logoPath = entreprise.LogoPath;
                            Application.Current.Dispatcher.Invoke(() => splash.SetLogo(logoPath));
                        }
                    }
                    catch (Exception innerEx)
                    {
                        System.IO.File.WriteAllText("startup_inner_error.txt", innerEx.ToString());
                        throw;
                    }
                    
                    // Artificial delay for Splash Screen effect
                    await Task.Delay(2000);
                });

                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
                splash.Close();
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("startup_error.txt", ex.ToString());
                MessageBox.Show($"Une erreur fatale est survenue au démarrage : {ex.Message}\nVoir startup_error.txt pour les détails.", "Erreur Critique", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }

            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await _host.StopAsync();
            _host.Dispose();

            base.OnExit(e);
        }
    }
}

