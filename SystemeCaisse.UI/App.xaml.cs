using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.UI.ViewModels;
using SystemeCaisse.UI.Services;
using SystemeCaisse.Core.Interfaces;
using SystemeCaisse.Infrastructure.Services;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace SystemeCaisse.UI
{
    public partial class App : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

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

        private static DateTime _lastErrorTime = DateTime.MinValue;

        public App()
        {
            System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Constructeur App lancé\n");
            // v30: GLOBAL SOFTWARE RENDERING
            // This is the NUCLEAR OPTION for stability. By forcing software rendering globally at startup,
            // we eliminate all GPU deadlocks, driver crashes, and "switching shocks" that freeze the UI.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

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
            System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Constructeur App: _host.Build() terminé.\n");

            // Setup global exception handling
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogException(e.ExceptionObject as Exception, "AppDomain");
            DispatcherUnhandledException += (s, e) => 
            { 
                LogException(e.Exception, "Dispatcher"); 
                
                // CRITICAL: We now force Handled = true to prevent the app from closing.
                // This allows the user to continue using other tabs even if an error occurred in Analysis.
                e.Handled = true;

                // v22: Silent Stability. We removed the MessageBox.Show here because it causes
                // a "Modal Loop" (the popup refresh triggers another error) which freezes the UI.
                // The error is still logged to crash_log.txt for developer diagnosis.
                System.Diagnostics.Debug.WriteLine($"SILENT STABILITY: Handled background error: {e.Exception.Message}");
            };
            TaskScheduler.UnobservedTaskException += (s, e) => { LogException(e.Exception, "TaskScheduler"); e.SetObserved(); };
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Set Culture to French for consistent decimal separator handling (, and .)
            var culture = new System.Globalization.CultureInfo("fr-FR");
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            try
            {
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] OnStartup: vérification _host...\n");
                if (_host == null)
                {
                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] [CRITICAL] _host est NULL dans OnStartup!\n");
                    throw new InvalidOperationException("_host is NULL in OnStartup");
                }
                await _host.StartAsync();

                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Création SplashScreen...\n");
                var splash = new Views.SplashScreen();
                splash.Show();
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] SplashScreen affiché. Démarrage Host...\n");

                await Task.Run(async () => 
                {
                    try 
                    {
                        System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Task.Run: Création dossier Images...\n");
                        // Ensure Images directory exists for logo and product images
                        string imagesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images");
                        if (!System.IO.Directory.Exists(imagesDir))
                        {
                            System.IO.Directory.CreateDirectory(imagesDir);
                        }

                        System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Task.Run: Migration DB...\n");
                        // Seed Data if empty
                        var factory = _host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
                        using (var context = factory.CreateDbContext())
                        {
                            await context.Database.MigrateAsync();
                            System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Task.Run: Migration terminée. Seed...\n");
                            if (!await context.Produits.AnyAsync())
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
                                entreprise = new Core.Entities.Entreprise
                                {
                                    Nom = "HIPPOCAMPE",
                                    Adresse = "5 - 7 Rue Pascal 33370 Tresses, France",
                                    Telephone = "06 00 00 00 00"
                                };
                                context.Entreprise.Add(entreprise);
                                await context.SaveChangesAsync();
                            }
                            
                            // v4.4: Removed hardcoded overwrite of entreprise data to respect user settings.
                            
                            // Update Splash Logo on UI Thread safely
                            var logoPath = !string.IsNullOrEmpty(entreprise.LogoPath) && System.IO.File.Exists(entreprise.LogoPath) 
                                ? entreprise.LogoPath 
                                : null;
                            
                            System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Task.Run: Splash Logo Update...\n");
                            await Application.Current.Dispatcher.InvokeAsync(() => {
                                try { splash.SetLogo(logoPath); } catch { /* Ignore splash errors */ }
                            });
                            System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Task.Run: Divers data fix...\n");
                            var diversLines = await context.LignesVente
                                .Where(l => l.CategorieNom == "Divers" || string.IsNullOrWhiteSpace(l.CategorieNom))
                                .ToListAsync();
                            
                            foreach (var line in diversLines)
                            {
                                line.CategorieNom = "Autre";
                            }

                            var diversProds = await context.Produits
                                .Where(p => p.Categorie == "Divers" || string.IsNullOrWhiteSpace(p.Categorie))
                                .ToListAsync();
                            
                            foreach (var prod in diversProds)
                            {
                                prod.Categorie = "Autre";
                            }

                            // Specific Fix: Reactivate "Tomates" if it's the top product but inactive
                            var tomateProd = await context.Produits.FirstOrDefaultAsync(p => p.Nom == "Tomates" && !p.Actif);
                            if (tomateProd != null)
                            {
                                tomateProd.Actif = true;
                            }

                            await context.SaveChangesAsync();
                        }
                    }
                    catch (Exception innerEx)
                    {
                        System.IO.File.AppendAllText("startup_log_v2.txt", $"[FAIL] Task Error: {innerEx}\n");
                        throw;
                    }
                    
                    // Artificial delay for Splash Screen effect
                    await Task.Delay(2000);
                });

                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                var mainVM = (MainViewModel)mainWindow.DataContext;
                
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Initialisation des données...\n");
                await mainVM.InitializeAsync();
                
                mainWindow.Show();
                splash.Close();
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Fenêtre Main affichée.\n");
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("startup_error.txt", ex.ToString());
                MessageBox.Show($"Une erreur fatale est survenue au démarrage : {ex.Message}\nVoir startup_error.txt pour les détails.", "Erreur Critique", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }

            System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Fin OnStartup. Fenêtre Main affichée.\n");
            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }

            base.OnExit(e);
        }
    }
}

