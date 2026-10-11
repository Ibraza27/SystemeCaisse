using Microsoft.EntityFrameworkCore;
using Npgsql;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.Infrastructure.Services;

namespace SystemeCaisse.Tests
{
    /// <summary>
    /// Fournit des bases de test jetables : SQLite (toujours) et PostgreSQL (si le serveur de test répond).
    /// Serveur PostgreSQL : variable SYSTEMECAISSE_TEST_PG (chaîne de connexion d'un rôle CREATEDB),
    /// par défaut Host=localhost;Port=5433;Username=caisse;Password=caisse.
    /// </summary>
    public static class TestDatabases
    {
        static TestDatabases()
        {
            var root = Path.Combine(Path.GetTempPath(), "SystemeCaisse_tests");
            Directory.CreateDirectory(root);
            AppPaths.OverrideRoot(root);
        }

        public static string PgAdminConnectionString =>
            Environment.GetEnvironmentVariable("SYSTEMECAISSE_TEST_PG")
            ?? "Host=localhost;Port=5433;Username=caisse;Password=caisse;Database=postgres;Timeout=3";

        public static async Task<CaisseDbContextFactory> CreateSqliteAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), "SystemeCaisse_tests", $"test_{Guid.NewGuid():N}.db");
            var factory = new CaisseDbContextFactory(new DatabaseSettings { Provider = DatabaseProvider.Sqlite, SqlitePath = path }, "Tests");
            var result = await new DatabaseBootstrapper(factory).InitializeAsync();
            Assert.True(result.IsReady, result.Message);
            return factory;
        }

        private static bool? _pgAvailable;

        public static async Task<bool> IsPostgresAvailableAsync()
        {
            if (_pgAvailable.HasValue) return _pgAvailable.Value;
            try
            {
                await using var conn = new NpgsqlConnection(PgAdminConnectionString);
                await conn.OpenAsync();
                _pgAvailable = true;
            }
            catch (Exception)
            {
                _pgAvailable = false;
            }
            return _pgAvailable.Value;
        }

        /// <summary>Crée une base PostgreSQL vide et migrée (null si le serveur de test est absent).</summary>
        public static async Task<CaisseDbContextFactory?> CreatePostgresAsync()
        {
            if (!await IsPostgresAvailableAsync()) return null;
            var admin = new NpgsqlConnectionStringBuilder(PgAdminConnectionString);
            var dbName = $"caisse_t_{Guid.NewGuid():N}"[..30];
            await using (var conn = new NpgsqlConnection(PgAdminConnectionString))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            var factory = new CaisseDbContextFactory(new DatabaseSettings
            {
                Provider = DatabaseProvider.PostgreSql,
                Host = admin.Host!,
                Port = admin.Port,
                Database = dbName,
                Username = admin.Username!,
                Password = admin.Password
            }, "Tests");
            var result = await new DatabaseBootstrapper(factory).InitializeAsync();
            Assert.True(result.IsReady, result.Message);
            return factory;
        }

        public static async Task<IEnumerable<CaisseDbContextFactory>> AllAsync()
        {
            var list = new List<CaisseDbContextFactory> { await CreateSqliteAsync() };
            var pg = await CreatePostgresAsync();
            if (pg != null) list.Add(pg);
            return list;
        }

        public static async Task<int> AddProductAsync(IDbContextFactory<AppDbContext> f, string nom, decimal stock, decimal prix = 2.5m)
        {
            await using var db = await f.CreateDbContextAsync();
            var p = new Core.Entities.Produit { Nom = nom, StockActuel = stock, PrixVente = prix, Categorie = "Test" };
            db.Produits.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        }
    }
}
