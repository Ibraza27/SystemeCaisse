using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    /// <summary>Résultat d'un transfert de données (import, restauration).</summary>
    public sealed class TransferReport
    {
        public Dictionary<string, int> Counts { get; } = new();
        public List<string> Warnings { get; } = new();
        public int ImagesImported { get; set; }
        public int TotalRows => Counts.Values.Sum();

        public string Summary()
        {
            var lines = Counts.Where(c => c.Value > 0).Select(c => $"• {c.Key} : {c.Value}").ToList();
            if (ImagesImported > 0) lines.Add($"• Images reprises : {ImagesImported}");
            if (Warnings.Count > 0) lines.Add($"⚠ {Warnings.Count} avertissement(s) (voir le journal)");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// Transferts de données identiques pour les deux moteurs :
    /// - reprise d'une ancienne base SQLite (caisse.db) vers la base courante (SQLite ou PostgreSQL) ;
    /// - sauvegarde / restauration au format portable .caisse-backup (zip de JSON) ;
    /// - sauvegarde automatique quotidienne.
    ///
    /// Le fichier d'origine n'est JAMAIS modifié : on travaille sur une copie.
    /// </summary>
    public sealed class DataTransferService
    {
        public const string BackupExtension = ".caisse-backup";
        private const int BackupFormatVersion = 1;
        private const int BatchSize = 500;

        /// <summary>Tables qui ne sont pas transférées (gérées par l'application).</summary>
        private static readonly HashSet<string> SkippedEntities = new() { nameof(Core.Entities.DataVersion) };

        private readonly CaisseDbContextFactory _target;

        public DataTransferService(CaisseDbContextFactory target)
        {
            _target = target;
        }

        // ═══════════════════════════ Reprise SQLite ═══════════════════════════

        /// <summary>
        /// Importe une ancienne base SQLite dans la base courante.
        /// </summary>
        /// <param name="sourceDbPath">Fichier caisse.db (local ou \\serveur\partage\caisse.db).</param>
        /// <param name="replaceExisting">True : vide d'abord la base cible. False : la cible doit être vide.</param>
        public async Task<TransferReport> ImportSqliteAsync(string sourceDbPath, bool replaceExisting,
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (!File.Exists(sourceDbPath)) throw new FileNotFoundException("Base source introuvable.", sourceDbPath);

            var tempDir = Path.Combine(Path.GetTempPath(), "SystemeCaisse_import_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var copy = Path.Combine(tempDir, "source.db");
            try
            {
                progress?.Report("Copie de la base source…");
                await Task.Run(() => CopySqliteConsistent(sourceDbPath, copy), ct);

                progress?.Report("Mise à niveau du schéma de la copie…");
                var sourceFactory = new CaisseDbContextFactory(new DatabaseSettings
                {
                    Provider = DatabaseProvider.Sqlite,
                    SqlitePath = copy
                }, "SystemeCaisse-Import");
                var boot = await new DatabaseBootstrapper(sourceFactory).InitializeAsync(null, ct, backupBeforeMigration: false);
                if (!boot.IsReady)
                    throw new InvalidOperationException($"La base source ne peut pas être lue : {boot.Message}");

                var report = await TransferAsync(new DbContextSource(sourceFactory), replaceExisting, progress, ct);

                // Images : dossier « Images » situé à côté de la base source.
                var sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourceDbPath));
                if (!string.IsNullOrEmpty(sourceDir))
                {
                    progress?.Report("Reprise des images…");
                    var images = new ImageStoreService(_target);
                    report.ImagesImported = await images.ImportReferencedImagesAsync(sourceDir, ct);
                    if (report.ImagesImported == 0 && !string.Equals(sourceDir.TrimEnd('\\'), AppPaths.InstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        report.ImagesImported = await images.ImportReferencedImagesAsync(AppPaths.InstallDir, ct);
                }

                AppLog.Info("Transfer", $"Import SQLite terminé depuis {sourceDbPath} : {report.TotalRows} lignes.");
                return report;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) { AppLog.Warn("Transfer", $"Nettoyage du dossier temporaire impossible : {ex.Message}"); }
            }
        }

        /// <summary>Copie cohérente d'une base SQLite (inclut le contenu du journal WAL), en lecture seule sur la source.</summary>
        private static void CopySqliteConsistent(string source, string dest)
        {
            var srcCs = new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            var dstCs = new SqliteConnectionStringBuilder { DataSource = dest, Pooling = false }.ToString();
            try
            {
                using var src = new SqliteConnection(srcCs);
                using var dst = new SqliteConnection(dstCs);
                src.Open();
                dst.Open();
                src.BackupDatabase(dst);
            }
            catch (SqliteException ex)
            {
                // Repli : copie brute du fichier (ex. partage réseau en lecture seule sans verrous).
                AppLog.Warn("Transfer", $"API de sauvegarde SQLite indisponible ({ex.Message}), copie de fichier.");
                File.Copy(source, dest, true);
                foreach (var ext in new[] { "-wal", "-shm" })
                    if (File.Exists(source + ext)) File.Copy(source + ext, dest + ext, true);
            }
        }

        // ═══════════════════════════ Sauvegarde / restauration ═══════════════════════════

        /// <summary>Exporte toute la base (y compris les images) dans un fichier .caisse-backup.</summary>
        public async Task<string> ExportBackupAsync(string destinationFile, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            var tmp = destinationFile + ".tmp";
            await using (var db = await _target.CreateDbContextAsync(ct))
            {
                var counts = new Dictionary<string, int>();
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (var et in OrderedEntityTypes(db.Model))
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report($"Export : {et.ClrType.Name}…");
                        var rows = await LoadAllAsync(db, et, ct);
                        var props = et.GetProperties().Where(p => p.PropertyInfo != null).ToList();
                        var entry = zip.CreateEntry($"tables/{et.ClrType.Name}.json", CompressionLevel.Optimal);
                        await using (var es = entry.Open())
                        await using (var writer = new Utf8JsonWriter(es))
                        {
                            writer.WriteStartArray();
                            foreach (var row in rows)
                            {
                                writer.WriteStartObject();
                                foreach (var p in props)
                                {
                                    writer.WritePropertyName(p.Name);
                                    JsonSerializer.Serialize(writer, p.PropertyInfo!.GetValue(row), p.ClrType);
                                }
                                writer.WriteEndObject();
                            }
                            writer.WriteEndArray();
                        }
                        counts[et.ClrType.Name] = rows.Count;
                    }

                    var manifest = new BackupManifest
                    {
                        FormatVersion = BackupFormatVersion,
                        CreatedAt = DateTime.Now,
                        Machine = Environment.MachineName,
                        Provider = _target.Provider.ToString(),
                        AppVersion = typeof(DataTransferService).Assembly.GetName().Version?.ToString() ?? "",
                        Counts = counts
                    };
                    var m = zip.CreateEntry("manifest.json");
                    await using var ms = m.Open();
                    await JsonSerializer.SerializeAsync(ms, manifest, new JsonSerializerOptions { WriteIndented = true }, ct);
                }
            }
            File.Move(tmp, destinationFile, overwrite: true);
            AppLog.Info("Transfer", $"Sauvegarde exportée : {destinationFile}");
            return destinationFile;
        }

        /// <summary>Lit l'en-tête d'une sauvegarde (pour affichage avant restauration).</summary>
        public static BackupManifest ReadManifest(string backupFile)
        {
            using var zip = ZipFile.OpenRead(backupFile);
            var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Fichier de sauvegarde invalide (manifest absent).");
            using var s = entry.Open();
            return JsonSerializer.Deserialize<BackupManifest>(s) ?? throw new InvalidDataException("Manifest illisible.");
        }

        /// <summary>Restaure une sauvegarde .caisse-backup dans la base courante (remplace TOUTES les données).</summary>
        public async Task<TransferReport> RestoreBackupAsync(string backupFile, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            var manifest = ReadManifest(backupFile);
            if (manifest.FormatVersion > BackupFormatVersion)
                throw new InvalidDataException("Cette sauvegarde provient d'une version plus récente de l'application.");

            using var zip = ZipFile.OpenRead(backupFile);
            var report = await TransferAsync(new ZipSource(zip), replaceExisting: true, progress, ct);
            AppLog.Info("Transfer", $"Sauvegarde restaurée : {backupFile} ({report.TotalRows} lignes).");
            return report;
        }

        /// <summary>
        /// Sauvegarde automatique (poste principal) : une par jour, 14 conservées.
        /// </summary>
        public async Task<string?> RunAutoBackupIfDueAsync(CancellationToken ct = default)
        {
            var dir = AppPaths.BackupsDir;
            var existing = new DirectoryInfo(dir).GetFiles("auto_*" + BackupExtension)
                .OrderByDescending(f => f.CreationTime).ToList();
            if (existing.Count > 0 && DateTime.Now - existing[0].CreationTime < TimeSpan.FromHours(20)) return null;

            var file = Path.Combine(dir, $"auto_{DateTime.Now:yyyyMMdd_HHmm}{BackupExtension}");
            await ExportBackupAsync(file, null, ct);

            foreach (var old in new DirectoryInfo(dir).GetFiles("auto_*" + BackupExtension)
                         .OrderByDescending(f => f.CreationTime).Skip(14))
            {
                try { old.Delete(); }
                catch (Exception ex) { AppLog.Warn("Transfer", $"Suppression de l'ancienne sauvegarde {old.Name} impossible : {ex.Message}"); }
            }
            return file;
        }

        // ═══════════════════════════ Cœur du transfert ═══════════════════════════

        private interface IRowSource
        {
            Task<IList> LoadAsync(IEntityType targetType, CancellationToken ct);
        }

        private sealed class DbContextSource : IRowSource
        {
            private readonly CaisseDbContextFactory _factory;
            public DbContextSource(CaisseDbContextFactory factory) => _factory = factory;

            public async Task<IList> LoadAsync(IEntityType targetType, CancellationToken ct)
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                var et = db.Model.FindEntityType(targetType.ClrType);
                return et == null ? new List<object>() : await LoadAllAsync(db, et, ct);
            }
        }

        private sealed class ZipSource : IRowSource
        {
            private readonly ZipArchive _zip;
            public ZipSource(ZipArchive zip) => _zip = zip;

            public async Task<IList> LoadAsync(IEntityType targetType, CancellationToken ct)
            {
                var list = new List<object>();
                var entry = _zip.GetEntry($"tables/{targetType.ClrType.Name}.json");
                if (entry == null) return list;
                await using var s = entry.Open();
                using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
                var props = targetType.GetProperties().Where(p => p.PropertyInfo != null).ToDictionary(p => p.Name);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var obj = Activator.CreateInstance(targetType.ClrType)!;
                    foreach (var jp in el.EnumerateObject())
                    {
                        if (!props.TryGetValue(jp.Name, out var p)) continue;
                        p.PropertyInfo!.SetValue(obj, jp.Value.Deserialize(p.ClrType));
                    }
                    list.Add(obj);
                }
                return list;
            }
        }

        private async Task<TransferReport> TransferAsync(IRowSource source, bool replaceExisting, IProgress<string>? progress, CancellationToken ct)
        {
            var report = new TransferReport();
            List<IEntityType> types;
            await using (var db = await _target.CreateDbContextAsync(ct))
            {
                types = OrderedEntityTypes(db.Model).ToList();
                if (!replaceExisting && await HasBusinessDataAsync(db, ct))
                    throw new InvalidOperationException("La base de destination contient déjà des données. Choisissez « Remplacer ».");
            }

            progress?.Report("Préparation de la base de destination…");
            await ClearTargetAsync(types, ct);

            // Ids insérés par type (pour écarter les références orphelines).
            var insertedKeys = new Dictionary<Type, HashSet<object>>();

            foreach (var et in types)
            {
                ct.ThrowIfCancellationRequested();
                var name = et.ClrType.Name;
                progress?.Report($"Transfert : {name}…");
                var rows = await source.LoadAsync(et, ct);
                var accepted = new List<object>(rows.Count);
                foreach (var row in rows)
                {
                    if (Sanitize(et, row, insertedKeys, report)) accepted.Add(row);
                }

                for (int i = 0; i < accepted.Count; i += BatchSize)
                {
                    await using var db = await _target.CreateDbContextAsync(ct);
                    db.ChangeTracker.AutoDetectChangesEnabled = false;
                    foreach (var row in accepted.Skip(i).Take(BatchSize))
                        db.Add(row);
                    await db.SaveChangesAsync(ct);
                }

                var pk = et.FindPrimaryKey();
                if (pk != null && pk.Properties.Count == 1)
                {
                    var keyProp = pk.Properties[0].PropertyInfo!;
                    insertedKeys[et.ClrType] = accepted.Select(r => keyProp.GetValue(r)!).ToHashSet();
                }
                report.Counts[name] = accepted.Count;
            }

            if (_target.Provider == DatabaseProvider.PostgreSql)
            {
                progress?.Report("Recalage des compteurs d'identifiants…");
                await ResetPostgresSequencesAsync(types, ct);
            }

            // Tous les postes doivent tout recharger.
            await using (var db = await _target.CreateDbContextAsync(ct))
                await DataVersionBump.BumpAsync(db, ct, Core.Entities.DataDomains.All);

            foreach (var w in report.Warnings) AppLog.Warn("Transfer", w);
            return report;
        }

        /// <summary>Corrige une ligne avant insertion. Renvoie false si elle doit être écartée.</summary>
        private static bool Sanitize(IEntityType et, object row, Dictionary<Type, HashSet<object>> insertedKeys, TransferReport report)
        {
            foreach (var p in et.GetProperties())
            {
                if (p.PropertyInfo == null || p.ClrType != typeof(string)) continue;
                var value = (string?)p.PropertyInfo.GetValue(row);
                if (value == null && !p.IsNullable)
                {
                    p.PropertyInfo.SetValue(row, string.Empty);
                    continue;
                }
                var max = p.GetMaxLength();
                if (value != null && max.HasValue && value.Length > max.Value)
                {
                    p.PropertyInfo.SetValue(row, value[..max.Value]);
                    report.Warnings.Add($"{et.ClrType.Name}.{p.Name} tronqué à {max} caractères (« {value[..Math.Min(30, value.Length)]}… »).");
                }
            }

            foreach (var fk in et.GetForeignKeys())
            {
                if (fk.Properties.Count != 1) continue;
                var fkProp = fk.Properties[0].PropertyInfo;
                if (fkProp == null) continue;
                var value = fkProp.GetValue(row);
                if (value == null) continue;
                if (!insertedKeys.TryGetValue(fk.PrincipalEntityType.ClrType, out var keys)) continue;
                if (keys.Contains(value)) continue;

                if (fk.Properties[0].IsNullable)
                {
                    fkProp.SetValue(row, null);
                    report.Warnings.Add($"{et.ClrType.Name} : référence orpheline {fk.Properties[0].Name}={value} remise à vide.");
                }
                else
                {
                    report.Warnings.Add($"{et.ClrType.Name} : ligne écartée (référence {fk.Properties[0].Name}={value} inexistante).");
                    return false;
                }
            }

            // Les navigations ne doivent pas être ré-insérées.
            foreach (var nav in et.GetNavigations())
            {
                var pi = nav.PropertyInfo;
                if (pi == null || !pi.CanWrite) continue;
                if (nav.IsCollection)
                {
                    if (pi.GetValue(row) is IList list) list.Clear();
                }
                else pi.SetValue(row, null);
            }
            return true;
        }

        private async Task ClearTargetAsync(List<IEntityType> orderedTypes, CancellationToken ct)
        {
            await using var db = await _target.CreateDbContextAsync(ct);
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async token =>
            {
                await using var ctx = await _target.CreateDbContextAsync(token);
                await using var tx = await ctx.Database.BeginTransactionAsync(token);
                foreach (var et in Enumerable.Reverse(orderedTypes))
                {
                    var table = et.GetTableName();
                    if (table == null) continue;
                    var schema = et.GetSchema();
                    var qualified = schema == null ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
#pragma warning disable EF1002 // noms de tables issus du modèle EF, pas d'une saisie utilisateur
                    await ctx.Database.ExecuteSqlRawAsync($"DELETE FROM {qualified}", token);
#pragma warning restore EF1002
                }
                await tx.CommitAsync(token);
            }, ct);
        }

        private async Task ResetPostgresSequencesAsync(List<IEntityType> types, CancellationToken ct)
        {
            await using var db = await _target.CreateDbContextAsync(ct);
            foreach (var et in types)
            {
                var pk = et.FindPrimaryKey();
                if (pk == null || pk.Properties.Count != 1) continue;
                var key = pk.Properties[0];
                if (key.ClrType != typeof(int) && key.ClrType != typeof(long)) continue;
                if (key.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never) continue;
                var table = et.GetTableName();
                var column = key.GetColumnName(StoreObjectIdentifier.Table(table!, et.GetSchema()));
                var sql = $"SELECT setval(pg_get_serial_sequence('\"{table}\"', '{column}'), " +
                          $"COALESCE((SELECT MAX(\"{column}\") FROM \"{table}\"), 1), " +
                          $"(SELECT MAX(\"{column}\") FROM \"{table}\") IS NOT NULL)";
                try
                {
#pragma warning disable EF1002
                    await db.Database.ExecuteSqlRawAsync(sql, ct);
#pragma warning restore EF1002
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Transfer", $"Recalage de la séquence {table}.{column} impossible : {ex.Message}");
                }
            }
        }

        private static async Task<bool> HasBusinessDataAsync(AppDbContext db, CancellationToken ct)
            => await db.Produits.AnyAsync(ct) || await db.Ventes.AnyAsync(ct) || await db.Commandes.AnyAsync(ct);

        /// <summary>Types d'entités dans l'ordre d'insertion (principaux avant dépendants).</summary>
        private static IEnumerable<IEntityType> OrderedEntityTypes(IModel model)
        {
            var all = model.GetEntityTypes()
                .Where(t => !t.IsOwned() && t.GetTableName() != null && !SkippedEntities.Contains(t.ClrType.Name))
                .OrderBy(t => t.ClrType.Name, StringComparer.Ordinal)
                .ToList();
            var result = new List<IEntityType>();
            var visiting = new HashSet<IEntityType>();
            void Visit(IEntityType t)
            {
                if (result.Contains(t) || !visiting.Add(t)) return;
                foreach (var fk in t.GetForeignKeys())
                    if (fk.PrincipalEntityType != t && all.Contains(fk.PrincipalEntityType)) Visit(fk.PrincipalEntityType);
                visiting.Remove(t);
                result.Add(t);
            }
            foreach (var t in all) Visit(t);
            return result;
        }

        private static readonly MethodInfo LoadAllGeneric = typeof(DataTransferService)
            .GetMethod(nameof(LoadAllTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static async Task<IList> LoadAllAsync(AppDbContext db, IEntityType et, CancellationToken ct)
        {
            var task = (Task<IList>)LoadAllGeneric.MakeGenericMethod(et.ClrType).Invoke(null, new object[] { db, ct })!;
            return await task;
        }

        private static async Task<IList> LoadAllTyped<T>(AppDbContext db, CancellationToken ct) where T : class
            => await db.Set<T>().AsNoTracking().ToListAsync(ct);
    }

    public sealed class BackupManifest
    {
        public int FormatVersion { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Machine { get; set; } = "";
        public string Provider { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public Dictionary<string, int> Counts { get; set; } = new();
    }
}
