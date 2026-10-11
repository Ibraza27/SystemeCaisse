namespace SystemeCaisse.Infrastructure.Configuration
{
    /// <summary>
    /// Emplacements des fichiers de l'application (hors dossier d'installation, pour survivre aux mises à jour).
    /// La variable d'environnement SYSTEMECAISSE_DATA_DIR permet de rediriger tout (tests automatiques).
    /// </summary>
    public static class AppPaths
    {
        private static string? _overrideRoot = Environment.GetEnvironmentVariable("SYSTEMECAISSE_DATA_DIR");

        /// <summary>Force un dossier racine (tests).</summary>
        public static void OverrideRoot(string? path) => _overrideRoot = path;

        /// <summary>Données partagées de la machine : réglages du poste, base SQLite par défaut, sauvegardes.</summary>
        public static string MachineRoot => Ensure(_overrideRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SystemeCaisse"));

        /// <summary>Données locales : cache catalogue, file hors-ligne, cache images, journaux.</summary>
        public static string LocalRoot => Ensure(_overrideRoot != null
            ? Path.Combine(_overrideRoot, "local")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemeCaisse"));

        public static string SettingsFile => Path.Combine(MachineRoot, "poste.json");
        public static string DefaultSqlitePath => Path.Combine(MachineRoot, "caisse.db");
        public static string BackupsDir => Ensure(Path.Combine(MachineRoot, "Backups"));
        public static string CacheDir => Ensure(Path.Combine(LocalRoot, "cache"));
        public static string OfflineQueueDir => Ensure(Path.Combine(LocalRoot, "offline"));
        public static string ImagesCacheDir => Ensure(Path.Combine(LocalRoot, "images"));
        public static string LogsDir => Ensure(Path.Combine(LocalRoot, "logs"));

        /// <summary>Dossier d'installation (ancien emplacement de caisse.db / network_db.json / Images).</summary>
        public static string InstallDir => AppDomain.CurrentDomain.BaseDirectory;

        private static string Ensure(string dir)
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AppPaths] {dir} : {ex.Message}"); }
            return dir;
        }
    }

    /// <summary>
    /// Journal applicatif minimal, thread-safe, qui ne lève jamais d'exception.
    /// Fichier : %LocalAppData%\SystemeCaisse\logs\caisse_yyyyMMdd.log
    /// </summary>
    public static class AppLog
    {
        private static readonly object Gate = new();

        public static void Info(string source, string message) => Write("INFO", source, message);
        public static void Warn(string source, string message) => Write("WARN", source, message);
        public static void Error(string source, string message, Exception? ex = null)
            => Write("ERROR", source, ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}\n{ex}");

        private static void Write(string level, string source, string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{source}] {message}";
            System.Diagnostics.Debug.WriteLine(line);
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path.Combine(AppPaths.LogsDir, $"caisse_{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppLog] écriture impossible : {ex.Message}");
            }
        }
    }
}
