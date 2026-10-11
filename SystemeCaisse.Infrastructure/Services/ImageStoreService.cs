using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    /// <summary>
    /// Images (produits, logo) stockées EN BASE (table ImagesStockees) et mises en cache sur chaque poste.
    /// Plus aucun partage de dossier réseau nécessaire.
    ///
    /// La clé d'une image est son ancien chemin relatif normalisé (ex : "Images/Produits/produit_42_123.png"),
    /// ce qui garde la colonne Produit.ImagePath compatible avec les données existantes.
    /// </summary>
    public sealed class ImageStoreService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly string _cacheDir;
        private readonly string _indexFile;
        private readonly ConcurrentDictionary<string, string> _localHashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _pendingFetch = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _syncGate = new(1, 1);

        /// <summary>Déclenché quand de nouvelles images sont arrivées dans le cache (rafraîchir l'affichage).</summary>
        public event Action? ImagesUpdated;

        public ImageStoreService(IDbContextFactory<AppDbContext> factory, string? cacheDir = null)
        {
            _factory = factory;
            _cacheDir = cacheDir ?? AppPaths.ImagesCacheDir;
            Directory.CreateDirectory(_cacheDir);
            _indexFile = Path.Combine(_cacheDir, "index.json");
            LoadIndex();
        }

        public static string NormalizeKey(string path)
        {
            var key = path.Trim().Replace('\\', '/');
            while (key.StartsWith("./", StringComparison.Ordinal)) key = key[2..];
            return key.TrimStart('/');
        }

        /// <summary>Chemin du fichier cache pour une clé (protégé contre les « .. »).</summary>
        public string GetCachePath(string key)
        {
            var safe = string.Join(Path.DirectorySeparatorChar,
                NormalizeKey(key).Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => s != "." && s != "..")
                    .Select(s => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c))));
            return Path.Combine(_cacheDir, safe);
        }

        /// <summary>
        /// Résolution SYNCHRONE et rapide pour l'affichage (Produit.ImagePathResolver).
        /// Renvoie le fichier en cache, sinon un fichier local historique, sinon null et lance le
        /// téléchargement en arrière-plan.
        /// </summary>
        public string? Resolve(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return null;
            try
            {
                if (Path.IsPathRooted(imagePath) && File.Exists(imagePath)) return imagePath;

                var key = NormalizeKey(imagePath);
                var cached = GetCachePath(key);
                if (File.Exists(cached)) return cached;

                // Ancien emplacement (dossier Images à côté de l'exe) en attendant la synchro.
                var legacy = Path.Combine(AppPaths.InstallDir, key.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(legacy)) return legacy;

                if (_pendingFetch.TryAdd(key, 0))
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (await FetchAsync(key, CancellationToken.None)) ImagesUpdated?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            AppLog.Warn("Images", $"Téléchargement de {key} impossible : {ex.Message}");
                        }
                        finally
                        {
                            _pendingFetch.TryRemove(key, out _);
                        }
                    });
            }
            catch (Exception ex)
            {
                AppLog.Warn("Images", $"Résolution de {imagePath} impossible : {ex.Message}");
            }
            return null;
        }

        /// <summary>Enregistre une image en base (et dans le cache local). Renvoie la clé à stocker dans ImagePath.</summary>
        public async Task<string> SaveAsync(string key, byte[] data, CancellationToken ct = default)
        {
            key = NormalizeKey(key);
            var hash = ComputeHash(data);
            await using (var db = await _factory.CreateDbContextAsync(ct))
            {
                var existing = await db.ImagesStockees.FirstOrDefaultAsync(i => i.Cle == key, ct);
                if (existing == null)
                    db.ImagesStockees.Add(new ImageStockee { Cle = key, Donnees = data, Hash = hash, UpdatedAt = DateTime.Now });
                else
                {
                    existing.Donnees = data;
                    existing.Hash = hash;
                    existing.UpdatedAt = DateTime.Now;
                }
                await db.SaveChangesAsync(ct);
                await DataVersionBump.BumpAsync(db, ct, DataDomains.Images);
            }
            WriteCache(key, data, hash);
            return key;
        }

        public async Task<string> SaveFileAsync(string key, string sourceFile, CancellationToken ct = default)
        {
            byte[] data;
            using (var fs = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream())
            {
                await fs.CopyToAsync(ms, ct);
                data = ms.ToArray();
            }
            return await SaveAsync(key, data, ct);
        }

        public async Task DeleteAsync(string? key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            key = NormalizeKey(key);
            await using var db = await _factory.CreateDbContextAsync(ct);
            await db.ImagesStockees.Where(i => i.Cle == key).ExecuteDeleteAsync(ct);
            await DataVersionBump.BumpAsync(db, ct, DataDomains.Images);
            _localHashes.TryRemove(key, out _);
            SaveIndex();
        }

        /// <summary>Met à jour le cache local depuis la base (images nouvelles ou modifiées uniquement).</summary>
        public async Task<int> SyncCacheAsync(CancellationToken ct = default)
        {
            if (!await _syncGate.WaitAsync(0, ct)) return 0;
            int downloaded = 0;
            try
            {
                List<(string Cle, string Hash)> remote;
                await using (var db = await _factory.CreateDbContextAsync(ct))
                {
                    remote = (await db.ImagesStockees.AsNoTracking().Select(i => new { i.Cle, i.Hash }).ToListAsync(ct))
                        .Select(x => (x.Cle, x.Hash)).ToList();
                }

                foreach (var (cle, hash) in remote)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_localHashes.TryGetValue(cle, out var local) && local == hash && File.Exists(GetCachePath(cle))) continue;
                    if (await FetchAsync(cle, ct)) downloaded++;
                }
            }
            finally
            {
                _syncGate.Release();
            }
            if (downloaded > 0)
            {
                AppLog.Info("Images", $"{downloaded} image(s) mise(s) à jour dans le cache local.");
                ImagesUpdated?.Invoke();
            }
            return downloaded;
        }

        /// <summary>
        /// Importe en base les images référencées (produits + logo) présentes dans un dossier historique
        /// (dossier contenant « Images/... »). Utilisé lors de la reprise des anciennes données.
        /// </summary>
        public async Task<int> ImportReferencedImagesAsync(string baseDir, CancellationToken ct = default)
        {
            List<string> paths;
            string? logo;
            await using (var db = await _factory.CreateDbContextAsync(ct))
            {
                paths = await db.Produits.AsNoTracking().Where(p => p.ImagePath != null && p.ImagePath != "")
                    .Select(p => p.ImagePath!).Distinct().ToListAsync(ct);
                logo = await db.Entreprise.AsNoTracking().Select(e => e.LogoPath).FirstOrDefaultAsync(ct);
            }

            int imported = 0;
            foreach (var p in paths)
            {
                var file = Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(file)) continue;
                try
                {
                    await SaveFileAsync(Path.IsPathRooted(p) ? $"Images/Produits/{Path.GetFileName(p)}" : p, file, ct);
                    imported++;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Images", $"Import de {file} impossible : {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(logo))
            {
                var logoFile = Path.IsPathRooted(logo) ? logo : Path.Combine(baseDir, logo);
                if (File.Exists(logoFile))
                {
                    try
                    {
                        var key = await SaveFileAsync($"Images/Logo/{Path.GetFileName(logoFile)}", logoFile, ct);
                        await using var db = await _factory.CreateDbContextAsync(ct);
                        await db.Entreprise.ExecuteUpdateAsync(s => s.SetProperty(e => e.LogoPath, key), ct);
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("Images", $"Import du logo impossible : {ex.Message}");
                    }
                }
            }
            return imported;
        }

        private async Task<bool> FetchAsync(string key, CancellationToken ct)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var img = await db.ImagesStockees.AsNoTracking().FirstOrDefaultAsync(i => i.Cle == key, ct);
            if (img == null) return false;
            WriteCache(key, img.Donnees, img.Hash);
            return true;
        }

        private void WriteCache(string key, byte[] data, string hash)
        {
            try
            {
                var path = GetCachePath(key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, data);
                File.Move(tmp, path, overwrite: true);
                _localHashes[key] = hash;
                SaveIndex();
            }
            catch (Exception ex)
            {
                // Fichier verrouillé par WPF : la prochaine synchro réessaiera.
                AppLog.Warn("Images", $"Écriture du cache pour {key} impossible : {ex.Message}");
            }
        }

        private void LoadIndex()
        {
            try
            {
                if (!File.Exists(_indexFile)) return;
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_indexFile));
                if (dict == null) return;
                foreach (var kv in dict) _localHashes[kv.Key] = kv.Value;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Images", $"Index du cache illisible, il sera reconstruit : {ex.Message}");
            }
        }

        private readonly object _indexLock = new();
        private void SaveIndex()
        {
            try
            {
                lock (_indexLock)
                {
                    var tmp = _indexFile + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(_localHashes.ToDictionary(k => k.Key, k => k.Value)));
                    File.Move(tmp, _indexFile, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Images", $"Sauvegarde de l'index du cache impossible : {ex.Message}");
            }
        }

        public static string ComputeHash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    }
}
