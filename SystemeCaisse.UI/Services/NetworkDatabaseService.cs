using System;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace SystemeCaisse.UI.Services
{
    /// <summary>
    /// Manages network vs local database path resolution.
    /// Config is stored in a local JSON file (not in the DB, since the DB isn't loaded yet at startup).
    /// Also manages shared Images path for product photos.
    /// </summary>
    public class NetworkDatabaseService
    {
        private static readonly string ConfigFileName = "network_db.json";

        /// <summary>True if the app is currently using the network database.</summary>
        public bool IsNetworkMode { get; private set; }

        /// <summary>The resolved database path (network or local).</summary>
        public string CurrentDbPath { get; private set; } = string.Empty;

        /// <summary>The configured network DB path (from JSON config).</summary>
        public string NetworkDbPath { get; private set; } = string.Empty;

        /// <summary>Whether network mode is enabled in config.</summary>
        public bool IsEnabled { get; private set; }

        /// <summary>The resolved base directory for Images (network or local).</summary>
        public string ImagesBasePath { get; private set; } = string.Empty;

        /// <summary>Fired when network connectivity changes (true = connected, false = lost).</summary>
        public event Action<bool>? NetworkStatusChanged;

        private DispatcherTimer? _checkTimer;
        private bool _lastCheckResult = true;

        // Singleton for easy access throughout the app
        private static NetworkDatabaseService? _instance;
        public static NetworkDatabaseService Instance => _instance ?? throw new InvalidOperationException("NetworkDatabaseService not initialized");

        public NetworkDatabaseService()
        {
            _instance = this;
        }

        /// <summary>
        /// Resolves the database path at startup. Call this BEFORE creating the Host.
        /// Returns the path to use for the SQLite connection string.
        /// </summary>
        public string ResolveDbPath()
        {
            string localDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "caisse.db");
            string localImagesPath = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                var config = LoadConfig();
                IsEnabled = config.Enabled;
                NetworkDbPath = config.NetworkDbPath;

                if (config.Enabled && !string.IsNullOrWhiteSpace(config.NetworkDbPath))
                {
                    LogStartup($"Mode réseau activé. Test du chemin : {config.NetworkDbPath}");

                    if (TestNetworkPath(config.NetworkDbPath))
                    {
                        IsNetworkMode = true;
                        CurrentDbPath = config.NetworkDbPath;

                        // Resolve Images path from the network share directory
                        string networkDir = Path.GetDirectoryName(config.NetworkDbPath) ?? "";
                        string networkImagesPath = Path.Combine(networkDir, "Images");
                        if (Directory.Exists(networkImagesPath))
                        {
                            ImagesBasePath = networkDir;
                            LogStartup($"Images réseau trouvées : {networkImagesPath}");
                        }
                        else
                        {
                            ImagesBasePath = localImagesPath;
                            LogStartup($"Dossier Images réseau introuvable, utilisation local");
                        }
                        // Try to clean up leftover WAL/SHM files from the main computer's old WAL mode
                        // These prevent SMB network access
                        CleanupWalFiles(config.NetworkDbPath);

                        LogStartup($"✅ Mode RÉSEAU — BDD : {CurrentDbPath}");
                        return CurrentDbPath;
                    }
                    else
                    {
                        LogStartup($"❌ Chemin réseau inaccessible, fallback en mode LOCAL");
                    }
                }
            }
            catch (Exception ex)
            {
                LogStartup($"Erreur résolution réseau : {ex.Message}");
            }

            // Fallback: local mode
            IsNetworkMode = false;
            CurrentDbPath = localDbPath;
            ImagesBasePath = localImagesPath;
            LogStartup($"Mode LOCAL — BDD : {CurrentDbPath}");
            return CurrentDbPath;
        }

        /// <summary>
        /// Starts a periodic connectivity check (every 30 seconds).
        /// Must be called from the UI thread after the app is loaded.
        /// </summary>
        public void StartPeriodicCheck()
        {
            if (!IsNetworkMode || !IsEnabled) return;

            _lastCheckResult = true;
            _checkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _checkTimer.Tick += (s, e) => CheckNetworkConnectivity();
            _checkTimer.Start();
        }

        /// <summary>Stops the periodic check.</summary>
        public void StopPeriodicCheck()
        {
            _checkTimer?.Stop();
            _checkTimer = null;
        }

        private void CheckNetworkConnectivity()
        {
            if (!IsNetworkMode || string.IsNullOrWhiteSpace(NetworkDbPath)) return;

            bool isAccessible = false;
            try
            {
                isAccessible = File.Exists(NetworkDbPath);
            }
            catch
            {
                isAccessible = false;
            }

            if (isAccessible != _lastCheckResult)
            {
                _lastCheckResult = isAccessible;
                NetworkStatusChanged?.Invoke(isAccessible);
            }
        }

        /// <summary>
        /// Tests if a network path is accessible (file exists and can be read).
        /// </summary>
        public static bool TestNetworkPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return false;

                // Check if the directory exists first
                string? dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir)) return false;

                // If the file exists, try to open it briefly to verify access
                if (File.Exists(path))
                {
                    using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    return true;
                }

                // File doesn't exist yet — check if we can write to the directory
                if (dir != null && Directory.Exists(dir))
                {
                    // The DB file doesn't exist but the directory is accessible
                    // It will be created by EF Core migration
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Resolves the full image path for a product, using network base if available.</summary>
        public string? ResolveImagePath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            if (Path.IsPathRooted(relativePath)) return File.Exists(relativePath) ? relativePath : null;

            // Try network path first if in network mode
            if (IsNetworkMode && !string.IsNullOrWhiteSpace(ImagesBasePath))
            {
                var networkPath = Path.Combine(ImagesBasePath, relativePath);
                if (File.Exists(networkPath)) return networkPath;
            }

            // Fallback to local
            var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
            return File.Exists(localPath) ? localPath : null;
        }

        /// <summary>
        /// Tries to delete leftover -wal and -shm files that prevent SMB access.
        /// These are created by the main computer if it was running in WAL mode.
        /// </summary>
        private void CleanupWalFiles(string dbPath)
        {
            string walFile = dbPath + "-wal";
            string shmFile = dbPath + "-shm";

            try
            {
                if (File.Exists(walFile))
                {
                    var walInfo = new FileInfo(walFile);
                    if (walInfo.Length == 0)
                    {
                        File.Delete(walFile);
                        LogStartup($"Supprimé fichier WAL vide : {walFile}");
                    }
                    else
                    {
                        LogStartup($"⚠ Fichier WAL non vide ({walInfo.Length} octets) — la BDD principale doit être convertie en mode DELETE");
                    }
                }

                if (File.Exists(shmFile))
                {
                    File.Delete(shmFile);
                    LogStartup($"Supprimé fichier SHM : {shmFile}");
                }
            }
            catch (Exception ex)
            {
                LogStartup($"Impossible de nettoyer les fichiers WAL : {ex.Message}");
            }
        }

        #region Config JSON

        public NetworkDbConfig LoadConfig()
        {
            string configPath = GetConfigPath();
            if (!File.Exists(configPath))
            {
                return new NetworkDbConfig
                {
                    Enabled = false,
                    NetworkDbPath = @"\\100.113.56.25\SystemeCaisse\caisse.db"
                };
            }

            try
            {
                string json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<NetworkDbConfig>(json) ?? new NetworkDbConfig();
            }
            catch
            {
                return new NetworkDbConfig();
            }
        }

        public void SaveConfig(NetworkDbConfig config)
        {
            string configPath = GetConfigPath();
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(configPath, json);

            IsEnabled = config.Enabled;
            NetworkDbPath = config.NetworkDbPath;
        }

        private static string GetConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
        }

        #endregion

        private static void LogStartup(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_log_v2.txt"),
                    $"[{DateTime.Now}] [NetworkDB] {message}\n");
            }
            catch { }
        }
    }

    public class NetworkDbConfig
    {
        public bool Enabled { get; set; }
        public string NetworkDbPath { get; set; } = @"\\100.113.56.25\SystemeCaisse\caisse.db";
    }
}
