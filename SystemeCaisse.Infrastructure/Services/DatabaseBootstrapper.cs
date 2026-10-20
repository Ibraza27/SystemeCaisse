using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    public enum BootstrapStatus
    {
        /// <summary>Base prête à l'emploi.</summary>
        Ready,
        /// <summary>Serveur / fichier injoignable.</summary>
        Unreachable,
        /// <summary>La base a été mise à jour par une version plus récente de l'application.</summary>
        AppTooOld,
        /// <summary>Des migrations sont nécessaires mais n'ont pas été autorisées.</summary>
        MigrationRequired,
        /// <summary>Erreur inattendue.</summary>
        Failed
    }

    public sealed record BootstrapResult(BootstrapStatus Status, string Message, IReadOnlyList<string> PendingMigrations)
    {
        public bool IsReady => Status == BootstrapStatus.Ready;
    }

    /// <summary>
    /// Prépare la base au démarrage : test de connexion, contrôle de version du schéma,
    /// migrations (protégées par verrou consultatif sur PostgreSQL) et seed minimal.
    /// C'est le SEUL endroit où le schéma est modifié.
    /// </summary>
    public sealed class DatabaseBootstrapper
    {
        /// <summary>Identifiant arbitraire du verrou consultatif PostgreSQL réservé aux migrations.</summary>
        private const long MigrationLockKey = 72_7274_2026;

        private readonly CaisseDbContextFactory _factory;

        public DatabaseBootstrapper(CaisseDbContextFactory factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// Initialise la base.
        /// </summary>
        /// <param name="confirmMigration">Appelé si des migrations sont en attente sur une base non vide ;
        /// renvoie true pour autoriser. Null = autoriser automatiquement.</param>
        /// <param name="backupBeforeMigration">Copie de sécurité du fichier SQLite avant migration.</param>
        public async Task<BootstrapResult> InitializeAsync(Func<IReadOnlyList<string>, bool>? confirmMigration = null, CancellationToken ct = default,
            bool backupBeforeMigration = true)
        {
            if (_factory.Provider == DatabaseProvider.PostgreSql)
            {
                await EnsurePostgresDatabaseExistsAsync(ct);
            }

            var error = await _factory.TestConnectionAsync(ct);
            if (error != null)
            {
                AppLog.Warn("Bootstrap", $"Base injoignable ({_factory.Settings.DisplayName}) : {error}");
                return new BootstrapResult(BootstrapStatus.Unreachable, error, Array.Empty<string>());
            }

            try
            {
                if (_factory.Provider == DatabaseProvider.Sqlite)
                    await PrepareSqliteAsync(ct);
                else if (_factory.Provider == DatabaseProvider.PostgreSql)
                    await PreparePostgresAsync(ct);

                await using var db = await _factory.CreateDbContextAsync(ct);

                var known = db.Database.GetMigrations().ToHashSet();
                var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
                var unknown = applied.Where(m => !known.Contains(m)).ToList();
                if (unknown.Count > 0)
                {
                    var msg = $"La base a été mise à jour par une version plus récente de l'application ({unknown.Last()}). " +
                              "Mettez à jour l'application sur ce poste.";
                    AppLog.Warn("Bootstrap", msg);
                    return new BootstrapResult(BootstrapStatus.AppTooOld, msg, Array.Empty<string>());
                }

                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count > 0)
                {
                    bool isNewDatabase = applied.Count == 0;
                    if (!isNewDatabase && confirmMigration != null && !confirmMigration(pending))
                    {
                        return new BootstrapResult(BootstrapStatus.MigrationRequired,
                            "La base doit être mise à jour avant utilisation.", pending);
                    }

                    if (backupBeforeMigration && !isNewDatabase && _factory.Provider == DatabaseProvider.Sqlite && !_factory.IsSqliteOnNetworkShare)
                        BackupSqliteFileBeforeMigration();

                    await MigrateAsync(db, ct);
                    if (_factory.Provider == DatabaseProvider.Sqlite)
                        await PrepareSqliteAsync(ct);
                    else if (_factory.Provider == DatabaseProvider.PostgreSql)
                        await PreparePostgresAsync(ct);
                    AppLog.Info("Bootstrap", $"Migrations appliquées : {string.Join(", ", pending)}");
                }
                else if (_factory.Provider == DatabaseProvider.PostgreSql)
                {
                    await PreparePostgresAsync(ct);
                }

                await SeedAsync(ct);
                return new BootstrapResult(BootstrapStatus.Ready, "OK", pending);
            }
            catch (Exception ex)
            {
                AppLog.Error("Bootstrap", "Initialisation de la base impossible", ex);
                return new BootstrapResult(BootstrapStatus.Failed, ex.InnerException?.Message ?? ex.Message, Array.Empty<string>());
            }
        }

        /// <summary>
        /// Si le serveur PostgreSQL répond mais que la base cible (ex: "caisse") n'existe pas encore,
        /// tente de la créer automatiquement via la base de maintenance "postgres".
        /// </summary>
        private async Task EnsurePostgresDatabaseExistsAsync(CancellationToken ct)
        {
            var dbName = _factory.Settings.Database?.Trim();
            if (string.IsNullOrWhiteSpace(dbName)) return;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                await using var db = _factory.CreateDbContext();
                var conn = db.Database.GetDbConnection();
                await conn.OpenAsync(cts.Token);
                await conn.CloseAsync();
                return;
            }
            catch (PostgresException pex) when (pex.SqlState == "3D000")
            {
                // La base n'existe pas encore : on se connecte à "postgres" pour la créer.
                try
                {
                    var adminSettings = _factory.Settings.Clone();
                    adminSettings.Database = "postgres";
                    var adminCs = CaisseDbContextFactory.BuildPostgresConnectionString(adminSettings, "SystemeCaisse-AutoCreateDb");
                    await using var adminConn = new NpgsqlConnection(adminCs);
                    await adminConn.OpenAsync(ct);
                    var safeDbName = dbName.Replace("\"", "\"\"");
                    await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{safeDbName}\" ENCODING 'UTF8'", adminConn);
                    await cmd.ExecuteNonQueryAsync(ct);
                    AppLog.Info("Bootstrap", $"Base PostgreSQL « {dbName} » créée automatiquement.");
                }
                catch (Exception createEx)
                {
                    AppLog.Warn("Bootstrap", $"Création automatique de la base PostgreSQL « {dbName} » impossible : {createEx.Message}");
                }
            }
            catch
            {
                // Autre erreur de connexion : gérée par TestConnectionAsync.
            }
        }

        /// <summary>True si la base ne contient encore aucune donnée métier (proposer l'import).</summary>
        public async Task<bool> IsDatabaseEmptyAsync(CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            return !await db.Produits.AnyAsync(ct) && !await db.Ventes.AnyAsync(ct) && !await db.Commandes.AnyAsync(ct);
        }

        private async Task MigrateAsync(AppDbContext db, CancellationToken ct)
        {
            if (_factory.Provider != DatabaseProvider.PostgreSql)
            {
                await db.Database.MigrateAsync(ct);
                return;
            }

            // Verrou consultatif : deux postes qui démarrent en même temps ne migrent jamais en parallèle.
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await ExecAsync(db.Database.GetDbConnection(), $"SELECT pg_advisory_lock({MigrationLockKey})", ct);
                // Re-vérifier après obtention du verrou : un autre poste a pu migrer entre-temps.
                if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
                    await db.Database.MigrateAsync(ct);
            }
            finally
            {
                try { await ExecAsync(db.Database.GetDbConnection(), $"SELECT pg_advisory_unlock({MigrationLockKey})", CancellationToken.None); }
                catch (Exception ex) { AppLog.Warn("Bootstrap", $"Libération du verrou de migration : {ex.Message}"); }
                await db.Database.CloseConnectionAsync();
            }
        }

        /// <summary>
        /// Garantit que toutes les colonnes récentes existent sur PostgreSQL
        /// (Commandes.Commentaire, LignesCommande.ClientNom, LignesCommande.Unite, etc.).
        /// </summary>
        private async Task PreparePostgresAsync(CancellationToken ct)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync(ct);
            try
            {
                await ExecAsync(conn, @"ALTER TABLE IF EXISTS ""Commandes"" ADD COLUMN IF NOT EXISTS ""SyncId"" character varying(36) NULL;", ct);
                await ExecAsync(conn, @"ALTER TABLE IF EXISTS ""Commandes"" ADD COLUMN IF NOT EXISTS ""Source"" character varying(10) NOT NULL DEFAULT 'caisse';", ct);
                await ExecAsync(conn, @"ALTER TABLE IF EXISTS ""Commandes"" ADD COLUMN IF NOT EXISTS ""Email"" character varying(200) NULL;", ct);
                await ExecAsync(conn, @"ALTER TABLE IF EXISTS ""Commandes"" ADD COLUMN IF NOT EXISTS ""Commentaire"" character varying(500) NULL;", ct);
                await ExecAsync(conn, @"ALTER TABLE IF EXISTS ""LignesCommande"" ADD COLUMN IF NOT EXISTS ""ClientNom"" character varying(100) NULL;", ct);
                await ExecAsync(conn, @"ALTER TABLE IF EXISTS ""LignesCommande"" ADD COLUMN IF NOT EXISTS ""Unite"" character varying(10) NULL;", ct);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Bootstrap", $"PreparePostgresAsync avertissement : {ex.Message}");
            }
            finally
            {
                await conn.CloseAsync();
            }
        }

        /// <summary>
        /// Réglages SQLite à appliquer une seule fois (persistants dans le fichier) +
        /// rattrapage des colonnes historiquement ajoutées hors migration (SyncId, Source, Email).
        /// </summary>
        private async Task PrepareSqliteAsync(CancellationToken ct)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync(ct);
            try
            {
                // WAL en local (lecteurs non bloquants) ; DELETE obligatoire sur partage SMB.
                var mode = _factory.IsSqliteOnNetworkShare ? "DELETE" : "WAL";
                await ExecAsync(conn, $"PRAGMA journal_mode={mode};", ct);

                bool hasMigrationsTable = await TableExistsAsync(conn, "__EFMigrationsHistory", ct);

                if (await TableExistsAsync(conn, "Commandes", ct))
                {
                    var columns = await GetColumnsAsync(conn, "Commandes", ct);
                    if (!columns.Contains("SyncId")) await ExecAsync(conn, "ALTER TABLE Commandes ADD COLUMN SyncId TEXT NULL;", ct);
                    if (!columns.Contains("Source")) await ExecAsync(conn, "ALTER TABLE Commandes ADD COLUMN Source TEXT NOT NULL DEFAULT 'caisse';", ct);
                    if (!columns.Contains("Email")) await ExecAsync(conn, "ALTER TABLE Commandes ADD COLUMN Email TEXT NULL;", ct);
                    if (!columns.Contains("Commentaire")) await ExecAsync(conn, "ALTER TABLE Commandes ADD COLUMN Commentaire TEXT NULL;", ct);

                    if (hasMigrationsTable)
                    {
                        await ExecAsync(conn, "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260611174448_AddCommandes', '8.0.11');", ct);
                        if (columns.Contains("ModePaiement"))
                        {
                            await ExecAsync(conn, "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260612154906_AddModePaiement', '8.0.11');", ct);
                        }
                    }
                }

                if (await TableExistsAsync(conn, "LignesCommande", ct))
                {
                    var lineCols = await GetColumnsAsync(conn, "LignesCommande", ct);
                    if (!lineCols.Contains("ClientNom")) await ExecAsync(conn, "ALTER TABLE LignesCommande ADD COLUMN ClientNom TEXT NULL;", ct);
                    if (!lineCols.Contains("Unite")) await ExecAsync(conn, "ALTER TABLE LignesCommande ADD COLUMN Unite TEXT NULL;", ct);
                }
            }
            finally
            {
                await conn.CloseAsync();
            }
        }

        private void BackupSqliteFileBeforeMigration()
        {
            try
            {
                var src = _factory.SqliteFilePath;
                if (!File.Exists(src)) return;
                var dest = Path.Combine(AppPaths.BackupsDir, $"avant_migration_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                // Checkpoint WAL implicite : on copie via l'API de sauvegarde SQLite pour un fichier cohérent.
                using var source = new Microsoft.Data.Sqlite.SqliteConnection(
                    CaisseDbContextFactory.BuildSqliteConnectionString(src, false));
                using var target = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dest};Pooling=False");
                source.Open();
                target.Open();
                source.BackupDatabase(target);
                AppLog.Info("Bootstrap", $"Copie de sécurité avant migration : {dest}");
            }
            catch (Exception ex)
            {
                AppLog.Warn("Bootstrap", $"Copie de sécurité avant migration impossible : {ex.Message}");
            }
        }

        /// <summary>Données minimales indispensables (uniquement si absentes).</summary>
        private async Task SeedAsync(CancellationToken ct)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            if (!await db.Entreprise.AnyAsync(ct))
                db.Entreprise.Add(new Entreprise { Nom = "Mon Magasin", UpdatedAt = DateTime.Now });

            var defaults = new (string Cle, string Valeur, string? Description)[]
            {
                ("version", "1.0.0", "Version de l'application"),
                ("theme", "default", null),
                ("sync_enabled", "false", "Activer la sync SignalR avec TransitManager"),
                ("sync_hub_url", "", "URL du hub SignalR TransitManager"),
                ("sync_api_key", "", "Clé API pour authentification SignalR"),
            };
            var existingKeys = await db.Configuration.Select(c => c.Cle).ToListAsync(ct);
            foreach (var d in defaults.Where(d => !existingKeys.Contains(d.Cle)))
                db.Configuration.Add(new SystemConfiguration { Cle = d.Cle, Valeur = d.Valeur, Description = d.Description });

            var existingDomains = await db.DataVersions.Select(v => v.Domaine).ToListAsync(ct);
            foreach (var domain in DataDomains.All.Where(d => !existingDomains.Contains(d)))
                db.DataVersions.Add(new DataVersion { Domaine = domain, Version = 1, UpdatedAt = DateTime.Now });

            if (db.ChangeTracker.HasChanges())
            {
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex)
                {
                    // Deux postes ont seedé en même temps : sans gravité.
                    AppLog.Warn("Bootstrap", $"Seed concurrent ignoré : {ex.InnerException?.Message ?? ex.Message}");
                }
            }
        }

        /// <summary>
        /// Reprend une seule fois les réglages périphériques qui étaient (à tort) stockés dans la table
        /// Configuration partagée, pour les placer dans les réglages locaux du poste.
        /// </summary>
        public async Task ImportLegacyPeripheralSettingsAsync(LocalSettingsStore store, CancellationToken ct = default)
        {
            if (store.Current.PeripheriquesImportes) return;
            try
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                var cfg = await db.Configuration.AsNoTracking().ToDictionaryAsync(c => c.Cle, c => c.Valeur, ct);

                store.Update(s =>
                {
                    var p = s.Peripheriques;
                    if (cfg.TryGetValue("imprimante_defaut", out var printer) && !string.IsNullOrWhiteSpace(printer)) p.Imprimante = printer;
                    if (TryInt(cfg, "ticket_width", out var tw)) p.LargeurTicketMm = tw;
                    if (TryBool(cfg, "customer_display_enabled", out var cde)) p.EcranClientActif = cde;
                    if (TryInt(cfg, "customer_display_screen_index", out var cdi)) p.EcranClientIndex = cdi;
                    if (TryBool(cfg, "customer_display_show_promotions", out var cdp)) p.EcranClientPromotions = cdp;
                    if (TryBool(cfg, "customer_display_compact", out var cdc)) p.EcranClientCompact = cdc;
                    if (TryBool(cfg, "scale_enabled", out var se)) p.BalanceActive = se;
                    if (cfg.TryGetValue("scale_port_name", out var sp) && !string.IsNullOrWhiteSpace(sp)) p.BalancePort = sp!;
                    if (TryInt(cfg, "scale_baud_rate", out var sb)) p.BalanceBaud = sb;
                    if (TryBool(cfg, "tpe_enabled", out var te)) p.TpeActif = te;
                    if (cfg.TryGetValue("tpe_port_name", out var tp) && !string.IsNullOrWhiteSpace(tp)) p.TpePort = tp!;
                    if (TryInt(cfg, "tpe_baud_rate", out var tb)) p.TpeBaud = tb;
                    if (TryInt(cfg, "tpe_timeout", out var tt)) p.TpeTimeout = tt;
                    if (TryBool(cfg, "training_mode", out var tm)) p.ModeFormation = tm;
                    s.PeripheriquesImportes = true;
                });
                AppLog.Info("Bootstrap", "Réglages périphériques repris depuis l'ancienne table Configuration.");
            }
            catch (Exception ex)
            {
                AppLog.Warn("Bootstrap", $"Reprise des réglages périphériques impossible : {ex.Message}");
            }
        }

        private static bool TryInt(Dictionary<string, string?> cfg, string key, out int value)
        {
            value = 0;
            return cfg.TryGetValue(key, out var raw) && int.TryParse(raw, out value);
        }

        private static bool TryBool(Dictionary<string, string?> cfg, string key, out bool value)
        {
            value = false;
            return cfg.TryGetValue(key, out var raw) && bool.TryParse(raw, out value);
        }

        private static async Task ExecAsync(DbConnection conn, string sql, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<bool> TableExistsAsync(DbConnection conn, string table, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t";
            var p = cmd.CreateParameter();
            p.ParameterName = "$t";
            p.Value = table;
            cmd.Parameters.Add(p);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
        }

        private static async Task<HashSet<string>> GetColumnsAsync(DbConnection conn, string table, CancellationToken ct)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) set.Add(reader.GetString(1));
            return set;
        }
    }
}
