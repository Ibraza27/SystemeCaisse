using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.Infrastructure.Services;

namespace SystemeCaisse.Tests
{
    /// <summary>
    /// Reprise d'une vraie base caisse.db (sur une COPIE) et aller-retour sauvegarde / restauration
    /// entre SQLite et PostgreSQL.
    /// Base d'origine : variable SYSTEMECAISSE_TEST_LEGACY_DB, sinon la plus grosse caisse.db trouvée
    /// dans SystemeCaisse.UI\bin. Le test est ignoré (réussi sans vérification) si aucune base n'existe.
    /// </summary>
    public class TransferTests
    {
        private static string? FindLegacyDb()
        {
            var env = Environment.GetEnvironmentVariable("SYSTEMECAISSE_TEST_LEGACY_DB");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !dir.GetFiles("*.sln").Any()) dir = dir.Parent;
            if (dir == null) return null;
            var bin = Path.Combine(dir.FullName, "SystemeCaisse.UI", "bin");
            if (!Directory.Exists(bin)) return null;
            return new DirectoryInfo(bin).GetFiles("caisse.db", SearchOption.AllDirectories)
                .Where(f => f.Length > 0)
                .OrderByDescending(f => f.Length)
                .FirstOrDefault()?.FullName;
        }

        private static string CopyToTemp(string source)
        {
            var dest = Path.Combine(Path.GetTempPath(), "SystemeCaisse_tests", $"legacy_{Guid.NewGuid():N}.db");
            using (var src = new SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False"))
            using (var dst = new SqliteConnection($"Data Source={dest};Pooling=False"))
            {
                src.Open();
                dst.Open();
                src.BackupDatabase(dst);
            }
            return dest;
        }

        private static long CountRaw(string dbPath, string table)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        [Fact]
        public async Task Reprise_d_une_vraie_base_sqlite_puis_aller_retour_sauvegarde()
        {
            var legacy = FindLegacyDb();
            if (legacy == null) return;

            var copy = CopyToTemp(legacy);
            var hashBefore = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(copy)));
            long produits = CountRaw(copy, "Produits");
            long ventes = CountRaw(copy, "Ventes");
            long lignes = CountRaw(copy, "LignesVente");

            foreach (var target in await TestDatabases.AllAsync())
            {
                var transfer = new DataTransferService(target);
                var report = await transfer.ImportSqliteAsync(copy, replaceExisting: true);

                await using (var db = await target.CreateDbContextAsync())
                {
                    Assert.Equal(produits, await db.Produits.LongCountAsync());
                    Assert.Equal(ventes, await db.Ventes.LongCountAsync());
                    Assert.Equal(lignes, await db.LignesVente.LongCountAsync());
                }

                // Une nouvelle vente après import doit obtenir un Id libre (séquences PostgreSQL recalées).
                var pid = await TestDatabases.AddProductAsync(target, "Après import", 5);
                var sale = await new SalesService(target).RecordSaleAsync(new Core.Entities.Vente
                {
                    ClientUid = Guid.NewGuid().ToString("N"),
                    NumeroTicket = "T-IMPORT",
                    Total = 2.5m,
                    MoyenPaiement = "CB",
                    Statut = "validee",
                    CreatedAt = DateTime.Now,
                    Lignes = { new Core.Entities.LigneVente { ProduitId = pid, ProduitNom = "Après import", Quantite = 1, PrixUnitaire = 2.5m, TotalLigne = 2.5m } }
                });
                Assert.False(sale.AlreadyRecorded);

                // Aller-retour : export .caisse-backup puis restauration dans une base neuve de l'autre moteur.
                var backup = Path.Combine(Path.GetTempPath(), "SystemeCaisse_tests", $"bk_{Guid.NewGuid():N}{DataTransferService.BackupExtension}");
                await transfer.ExportBackupAsync(backup);
                var manifest = DataTransferService.ReadManifest(backup);
                Assert.Equal(produits + 1, manifest.Counts["Produit"]);

                var other = target.Provider == DatabaseProvider.Sqlite
                    ? await TestDatabases.CreatePostgresAsync()
                    : await TestDatabases.CreateSqliteAsync();
                if (other == null) continue;
                await new DataTransferService(other).RestoreBackupAsync(backup);
                await using (var db = await other.CreateDbContextAsync())
                {
                    Assert.Equal(produits + 1, await db.Produits.LongCountAsync());
                    Assert.Equal(ventes + 1, await db.Ventes.LongCountAsync());
                }
            }

            SqliteConnection.ClearAllPools();
            var hashAfter = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(copy)));
            Assert.Equal(hashBefore, hashAfter); // la source n'est jamais modifiée
        }
    }
}
