using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SystemeCaisse.Infrastructure.Configuration;

namespace SystemeCaisse.Infrastructure.Data
{
    /// <summary>
    /// Fabrique de contextes utilisée par TOUTE l'application (enregistrée comme IDbContextFactory&lt;AppDbContext&gt;).
    /// Construit un contexte SQLite ou PostgreSQL selon les réglages du poste.
    ///
    /// Usage obligatoire : un contexte par opération, toujours dans un using.
    /// <code>await using var db = await factory.CreateDbContextAsync(ct);</code>
    /// </summary>
    public sealed class CaisseDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext>? _sqliteOptions;
        private readonly DbContextOptions<PostgresAppDbContext>? _pgOptions;

        public DatabaseSettings Settings { get; }
        public DatabaseProvider Provider => Settings.Provider;

        /// <summary>True si la base SQLite est sur un partage réseau (chemin UNC).</summary>
        public bool IsSqliteOnNetworkShare { get; }

        /// <summary>Chemin complet du fichier SQLite (vide en mode PostgreSQL).</summary>
        public string SqliteFilePath { get; } = string.Empty;

        public CaisseDbContextFactory(DatabaseSettings settings, string applicationName = "SystemeCaisse")
        {
            Settings = settings.Clone();

            if (Settings.Provider == DatabaseProvider.PostgreSql)
            {
                var builder = new DbContextOptionsBuilder<PostgresAppDbContext>();
                builder.UseNpgsql(BuildPostgresConnectionString(Settings, applicationName), o =>
                {
                    // Reconnexion automatique sur coupure réseau passagère (Tailscale).
                    // Les services qui ouvrent une transaction utilisent CreateExecutionStrategy().
                    o.EnableRetryOnFailure(maxRetryCount: 2, maxRetryDelay: TimeSpan.FromSeconds(1), errorCodesToAdd: null);
                    o.CommandTimeout(30);
                });
                builder.ConfigureWarnings(w => w.Ignore(CoreEventId.SensitiveDataLoggingEnabledWarning));
                _pgOptions = builder.Options;
            }
            else
            {
                SqliteFilePath = string.IsNullOrWhiteSpace(Settings.SqlitePath) ? AppPaths.DefaultSqlitePath : Settings.SqlitePath!;
                IsSqliteOnNetworkShare = SqliteFilePath.StartsWith(@"\\", StringComparison.Ordinal);
                try
                {
                    var dir = Path.GetDirectoryName(SqliteFilePath);
                    if (!IsSqliteOnNetworkShare && !string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("DbFactory", $"Création du dossier SQLite impossible : {ex.Message}");
                }

                var builder = new DbContextOptionsBuilder<AppDbContext>();
                builder.UseSqlite(BuildSqliteConnectionString(SqliteFilePath, IsSqliteOnNetworkShare), o => o.CommandTimeout(30));
                builder.AddInterceptors(new SqliteConnectionInterceptor(IsSqliteOnNetworkShare));
                _sqliteOptions = builder.Options;
            }
        }

        public AppDbContext CreateDbContext()
            => _pgOptions != null ? new PostgresAppDbContext(_pgOptions) : new AppDbContext(_sqliteOptions!);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());

        /// <summary>Teste la connexion (timeout court). Renvoie null si OK, sinon un message d'erreur lisible.</summary>
        public async Task<string?> TestConnectionAsync(CancellationToken ct = default)
        {
            try
            {
                if (Provider == DatabaseProvider.Sqlite)
                {
                    var dir = Path.GetDirectoryName(SqliteFilePath);
                    if (IsSqliteOnNetworkShare && (dir == null || !Directory.Exists(dir)))
                        return $"Dossier réseau inaccessible : {dir}";
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(6));
                await using var db = CreateDbContext();
                var conn = db.Database.GetDbConnection();
                await conn.OpenAsync(cts.Token);
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1";
                    await cmd.ExecuteScalarAsync(cts.Token);
                }
                await conn.CloseAsync();
                return null;
            }
            catch (OperationCanceledException)
            {
                return "Délai dépassé : le serveur ne répond pas (vérifiez qu'il est allumé et que Tailscale est connecté).";
            }
            catch (PostgresException pex) when (pex.SqlState == "28P01")
            {
                return "Identifiant ou mot de passe PostgreSQL incorrect.";
            }
            catch (PostgresException pex) when (pex.SqlState == "3D000")
            {
                return $"La base « {Settings.Database} » n'existe pas sur le serveur.";
            }
            catch (Exception ex)
            {
                return ex.InnerException?.Message ?? ex.Message;
            }
        }

        public static string BuildPostgresConnectionString(DatabaseSettings s, string applicationName)
        {
            var b = new NpgsqlConnectionStringBuilder
            {
                Host = s.Host,
                Port = s.Port,
                Database = s.Database,
                Username = s.Username,
                Password = s.Password,
                Timeout = 5,               // connexion : échec rapide si serveur injoignable
                CommandTimeout = 30,
                KeepAlive = 30,            // détecte les connexions mortes (coupure VPN)
                Pooling = true,
                MaxPoolSize = 20,
                ApplicationName = Truncate(applicationName, 60)
            };
            return b.ConnectionString;
        }

        public static string BuildSqliteConnectionString(string path, bool onNetworkShare)
        {
            var b = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                // Sur partage SMB, le pool garderait le fichier ouvert indéfiniment : on le désactive.
                Pooling = !onNetworkShare,
                DefaultTimeout = 30
            };
            return b.ConnectionString;
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
    }

    /// <summary>
    /// Applique les PRAGMA SQLite une fois par connexion physique (et non plus à chaque contexte).
    /// </summary>
    public sealed class SqliteConnectionInterceptor : DbConnectionInterceptor
    {
        private readonly string _pragmas;

        public SqliteConnectionInterceptor(bool onNetworkShare)
        {
            // busy_timeout : attente si un autre processus écrit.
            // synchronous=NORMAL est sûr en WAL (local) ; on garde FULL (défaut) en mode DELETE (partage).
            _pragmas = onNetworkShare
                ? "PRAGMA busy_timeout=10000;"
                : "PRAGMA busy_timeout=10000; PRAGMA synchronous=NORMAL;";
        }

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = _pragmas;
            cmd.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = _pragmas;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
