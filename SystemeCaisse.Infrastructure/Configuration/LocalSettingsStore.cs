using System.Text.Json;

namespace SystemeCaisse.Infrastructure.Configuration
{
    /// <summary>
    /// Lecture/écriture des réglages du poste (poste.json).
    /// Au premier lancement, reprend automatiquement l'ancienne configuration
    /// (network_db.json et caisse.db placés à côté de l'exécutable).
    /// </summary>
    public class LocalSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly object _gate = new();
        private PosteSettings _current;

        public LocalSettingsStore()
        {
            _current = LoadOrCreate();
        }

        /// <summary>Copie des réglages courants.</summary>
        public PosteSettings Current
        {
            get { lock (_gate) return Clone(_current); }
        }

        /// <summary>True si le fichier poste.json n'existait pas au démarrage (premier lancement).</summary>
        public bool IsFirstRun { get; private set; }

        public event Action<PosteSettings>? Changed;

        /// <summary>Modifie et enregistre les réglages de manière atomique.</summary>
        public void Update(Action<PosteSettings> mutate)
        {
            PosteSettings snapshot;
            lock (_gate)
            {
                var copy = Clone(_current);
                mutate(copy);
                WriteAtomic(copy);
                _current = copy;
                snapshot = Clone(copy);
            }
            Changed?.Invoke(snapshot);
        }

        private PosteSettings LoadOrCreate()
        {
            var file = AppPaths.SettingsFile;
            if (File.Exists(file))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<PosteSettings>(File.ReadAllText(file), JsonOptions);
                    if (loaded != null)
                    {
                        loaded.Database ??= new DatabaseSettings();
                        loaded.Peripheriques ??= new PeripheriquesSettings();
                        return loaded;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Settings", $"poste.json illisible, sauvegardé en .corrupt et recréé", ex);
                    try { File.Copy(file, file + ".corrupt", true); }
                    catch (Exception copyEx) { AppLog.Warn("Settings", $"Copie .corrupt impossible : {copyEx.Message}"); }
                }
            }

            IsFirstRun = true;
            var settings = MigrateLegacy();
            try { WriteAtomic(settings); }
            catch (Exception ex) { AppLog.Error("Settings", "Écriture initiale de poste.json impossible", ex); }
            return settings;
        }

        /// <summary>Reprend l'ancienne configuration (network_db.json / caisse.db à côté de l'exe).</summary>
        private static PosteSettings MigrateLegacy()
        {
            var settings = new PosteSettings();
            settings.Database.Provider = DatabaseProvider.Sqlite;
            try
            {
                var legacyJson = Path.Combine(AppPaths.InstallDir, "network_db.json");
                var legacyDb = Path.Combine(AppPaths.InstallDir, "caisse.db");

                if (File.Exists(legacyJson))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(legacyJson));
                    bool enabled = doc.RootElement.TryGetProperty("Enabled", out var e) && e.ValueKind == JsonValueKind.True;
                    string? path = doc.RootElement.TryGetProperty("NetworkDbPath", out var p) ? p.GetString() : null;
                    if (enabled && !string.IsNullOrWhiteSpace(path))
                    {
                        settings.Database.Provider = DatabaseProvider.Sqlite;
                        settings.Database.SqlitePath = path;
                        settings.EstPostePrincipal = false; // un poste qui pointait vers le partage était secondaire
                        AppLog.Info("Settings", $"Reprise de l'ancien mode réseau SMB : {path}");
                        return settings;
                    }
                }

                if (File.Exists(legacyDb))
                {
                    // Conserver la base existante à son emplacement actuel : aucune perte de données.
                    settings.Database.Provider = DatabaseProvider.Sqlite;
                    settings.Database.SqlitePath = legacyDb;
                    AppLog.Info("Settings", $"Reprise de la base locale existante : {legacyDb}");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Settings", "Reprise de l'ancienne configuration impossible", ex);
            }
            return settings;
        }

        private static void WriteAtomic(PosteSettings settings)
        {
            var file = AppPaths.SettingsFile;
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, file, overwrite: true);
        }

        private static PosteSettings Clone(PosteSettings s)
            => JsonSerializer.Deserialize<PosteSettings>(JsonSerializer.Serialize(s, JsonOptions), JsonOptions)!;
    }
}
