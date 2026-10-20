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
using Microsoft.Data.Sqlite;
using SystemeCaisse.Infrastructure.Configuration;

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

            // Resolve database path (network or local) BEFORE creating Host
            var networkDbService = new NetworkDatabaseService();
            string dbPath = networkDbService.ResolveDbPath();

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(networkDbService);

                    // Add LocalSettingsStore and CaisseDbContextFactory (single source of truth for entire app)
                    services.AddSingleton<LocalSettingsStore>();
                    services.AddSingleton(sp => 
                    {
                        var store = sp.GetRequiredService<LocalSettingsStore>();
                        if (store.Current.Database.Provider == DatabaseProvider.Sqlite &&
                            !string.Equals(store.Current.Database.SqlitePath, dbPath, StringComparison.OrdinalIgnoreCase))
                        {
                            store.Update(s => s.Database.SqlitePath = dbPath);
                        }
                        return new CaisseDbContextFactory(store.Current.Database, $"SystemeCaisse-{store.Current.NomPoste}");
                    });
                    services.AddSingleton<IDbContextFactory<AppDbContext>>(sp => sp.GetRequiredService<CaisseDbContextFactory>());
                    services.AddSingleton<CaisseServices>();

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

            var networkSvc = _host.Services.GetRequiredService<NetworkDatabaseService>();
            Core.Entities.Produit.ImagePathResolver = networkSvc.ResolveImagePath;

            bool setupPrincipalRequested = e.Args.Any(a => string.Equals(a, "--setup-principal", StringComparison.OrdinalIgnoreCase));

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
                        string imagesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images");
                        if (!System.IO.Directory.Exists(imagesDir))
                        {
                            System.IO.Directory.CreateDirectory(imagesDir);
                        }

                        var store = _host.Services.GetRequiredService<LocalSettingsStore>();

                        // Si l'installateur a demandé la configuration autonome du Poste Principal (--setup-principal)
                        if (setupPrincipalRequested)
                        {
                            System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Task.Run: Provisioning autonome Poste Principal (--setup-principal)...\n");
                            var provisioning = new PostgresProvisioningService(store);
                            var provRes = await provisioning.ProvisionPrincipalServerAsync();
                            System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Task.Run: Provisioning result: {provRes.Success} - {provRes.Message}\n");
                        }

                        System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Task.Run: Bootstrap DB...\n");
                        var dbFactory = _host.Services.GetRequiredService<CaisseDbContextFactory>();
                        var caisseServices = _host.Services.GetRequiredService<CaisseServices>();

                        // Résolution des images : cache/base PostgreSQL prioritaire, puis dossier local/SMB en repli
                        Core.Entities.Produit.ImagePathResolver = path =>
                            caisseServices.Images.Resolve(path) ?? networkSvc.ResolveImagePath(path);

                        var bootstrapper = new DatabaseBootstrapper(dbFactory);
                        var bootResult = await bootstrapper.InitializeAsync();
                        System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Task.Run: Bootstrap result: {bootResult.Status}, {bootResult.Message}\n");

                        if (bootResult.IsReady)
                        {
                            caisseServices.StartBackground(true);

                            // Si la base est totalement vide (nouveau serveur ou premier lancement),
                            // on importe automatiquement l'ancienne base SQLite si elle existe.
                            if (await bootstrapper.IsDatabaseEmptyAsync())
                            {
                                var legacyCandidate = store.Current.Database.SqlitePath;
                                if (string.IsNullOrWhiteSpace(legacyCandidate) || !System.IO.File.Exists(legacyCandidate))
                                    legacyCandidate = AppPaths.DefaultSqlitePath;
                                if (!System.IO.File.Exists(legacyCandidate))
                                    legacyCandidate = System.IO.Path.Combine(AppPaths.InstallDir, "caisse.db");

                                if (System.IO.File.Exists(legacyCandidate))
                                {
                                    System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Base vide : import automatique depuis {legacyCandidate}...\n");
                                    try
                                    {
                                        var transfer = new DataTransferService(dbFactory);
                                        var report = await transfer.ImportSqliteAsync(legacyCandidate, replaceExisting: true);
                                        System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Import automatique terminé : {report.TotalRows} lignes.\n");
                                    }
                                    catch (Exception impEx)
                                    {
                                        System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Erreur import automatique : {impEx.Message}\n");
                                    }
                                }
                            }

                            // Lecture des coordonnées et logo pour le splash
                            await using var context = await dbFactory.CreateDbContextAsync();
                            var entreprise = await context.Entreprise.AsNoTracking().FirstOrDefaultAsync();
                            if (entreprise != null)
                            {
                                var resolvedLogo = !string.IsNullOrEmpty(entreprise.LogoPath)
                                    ? (caisseServices.Images.Resolve(entreprise.LogoPath) ?? entreprise.LogoPath)
                                    : null;
                                var logoPath = !string.IsNullOrEmpty(resolvedLogo) && System.IO.File.Exists(resolvedLogo) 
                                    ? resolvedLogo : null;
                                await Application.Current.Dispatcher.InvokeAsync(() => {
                                    try { splash.SetLogo(logoPath); } catch { }
                                });
                            }
                        }
                        else
                        {
                            caisseServices.StartedOffline = true;
                            caisseServices.StartBackground(false);
                            System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Démarrage en mode dégradé / hors-ligne\n");
                        }
                    }
                    catch (Exception innerEx)
                    {
                        System.IO.File.AppendAllText("startup_log_v2.txt", $"[FAIL] Task Error: {innerEx}\n");
                        throw;
                    }
                });

                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                var mainVM = (MainViewModel)mainWindow.DataContext;
                
                System.IO.File.AppendAllText("startup_log_v2.txt", $"[{DateTime.Now}] Initialisation des données...\n");
                await mainVM.InitializeAsync();
                
                mainWindow.Show();
                
                // CRITICAL: Explicitly set MainWindow to prevent WPF from auto-assigning
                // it to CustomerDisplayWindow or SplashScreen
                Application.Current.MainWindow = mainWindow;
                
                // Admin window opens maximized
                mainWindow.WindowState = WindowState.Maximized;
                
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
