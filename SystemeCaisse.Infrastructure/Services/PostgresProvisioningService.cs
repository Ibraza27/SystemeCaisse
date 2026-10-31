using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SystemeCaisse.Infrastructure.Configuration;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    public sealed record TailscaleStatusInfo(
        bool IsInstalled,
        bool IsConnected,
        string? TailscaleIp,
        string? LanIp,
        string StatusSummary);

    public sealed record ProvisioningResult(
        bool Success,
        string Message,
        int Port,
        string? TailscaleIp,
        string? LanIp,
        int ImportedRows = 0,
        int ImportedImages = 0);

    public sealed record SecondaryConnectionTestResult(
        bool Success,
        int ResolvedPort,
        string Message,
        int ProductCount = 0);

    /// <summary>
    /// Service autonome de déploiement et de maintien réseau Multi-Postes PostgreSQL + Tailscale :
    /// - Détection de l'adresse IP Tailscale (100.x.y.z) et du réseau local (LAN)
    /// - Maintien du service Tailscale et reconnexion automatique
    /// - Installation / Configuration autonome de PostgreSQL sur le Poste Principal
    ///   (postgresql.conf, pg_hba.conf sans BOM UTF-8, pare-feu Windows, utilisateur/base 'caisse', tables EF Core, reprise SQLite + images)
    /// - Diagnostic et connexion assistée pour les Postes Secondaires
    /// </summary>
    public sealed class PostgresProvisioningService
    {
        private readonly LocalSettingsStore _settingsStore;

        public PostgresProvisioningService(LocalSettingsStore settingsStore)
        {
            _settingsStore = settingsStore;
        }

        // ═══════════════════════════ 1. Détection Tailscale & Réseau ═══════════════════════════

        public static TailscaleStatusInfo GetNetworkAndTailscaleStatus()
        {
            string? tailscaleIp = null;
            string? lanIp = null;
            bool tailscaleInstalled = File.Exists(@"C:\Program Files\Tailscale\tailscale.exe") ||
                                      File.Exists(@"C:\Program Files (x86)\Tailscale\tailscale.exe");

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    bool isTailscaleAdapter = ni.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                                              ni.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase);

                    if (isTailscaleAdapter) tailscaleInstalled = true;

                    var props = ni.GetIPProperties();
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var bytes = ua.Address.GetAddressBytes();

                        // Plage CGNAT Tailscale : 100.64.0.0/10 (100.64.x.x à 100.127.x.x)
                        if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                        {
                            tailscaleIp ??= ua.Address.ToString();
                            tailscaleInstalled = true;
                        }
                        else if (bytes[0] == 192 && bytes[1] == 168 ||
                                 bytes[0] == 10 ||
                                 (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31))
                        {
                            lanIp ??= ua.Address.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Tailscale", $"Lecture des interfaces réseau : {ex.Message}");
            }

            // Repli via CLI tailscale ip -4 si l'interface n'a pas encore remonté l'IP
            if (string.IsNullOrEmpty(tailscaleIp) && tailscaleInstalled)
            {
                try
                {
                    var exe = File.Exists(@"C:\Program Files\Tailscale\tailscale.exe")
                        ? @"C:\Program Files\Tailscale\tailscale.exe"
                        : @"C:\Program Files (x86)\Tailscale\tailscale.exe";
                    if (File.Exists(exe))
                    {
                        var psi = new ProcessStartInfo(exe, "ip -4")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null && proc.WaitForExit(2000))
                        {
                            var output = proc.StandardOutput.ReadToEnd().Trim()
                                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(output) && IPAddress.TryParse(output.Trim(), out _))
                            {
                                tailscaleIp = output.Trim();
                            }
                        }
                    }
                }
                catch { }
            }

            bool isConnected = !string.IsNullOrWhiteSpace(tailscaleIp);
            string summary = isConnected
                ? $"🟢 Tailscale connecté — IP : {tailscaleIp}" + (!string.IsNullOrEmpty(lanIp) ? $" (LAN : {lanIp})" : "")
                : tailscaleInstalled
                    ? $"🟠 Tailscale installé mais non connecté" + (!string.IsNullOrEmpty(lanIp) ? $" — IP LAN : {lanIp}" : "")
                    : $"⚪ Tailscale non détecté" + (!string.IsNullOrEmpty(lanIp) ? $" — IP Réseau local : {lanIp}" : "");

            return new TailscaleStatusInfo(tailscaleInstalled, isConnected, tailscaleIp, lanIp, summary);
        }

        // ═══════════════════════════ 2. Test & Connexion Poste Secondaire ═══════════════════════════

        public static async Task<bool> TestTcpPortAsync(string host, int port, int timeoutMs = 2500)
        {
            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(timeoutMs);
                await client.ConnectAsync(host, port, cts.Token);
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Teste la connexion depuis un Poste Secondaire vers le Poste Principal (via Tailscale ou LAN),
        /// détecte automatiquement le port PostgreSQL (5432 ou 5433) si besoin, et vérifie le schéma.
        /// </summary>
        public async Task<SecondaryConnectionTestResult> TestAndPrepareSecondaryConnectionAsync(
            string host,
            int port,
            string database,
            string username,
            string password,
            CancellationToken ct = default)
        {
            host = (host ?? "").Trim();
            if (string.IsNullOrWhiteSpace(host))
                return new SecondaryConnectionTestResult(false, port, "Veuillez saisir l'adresse IP Tailscale (ex: 100.x.y.z) ou LAN du Poste Principal.");

            if (port <= 0) port = 5432;
            database = string.IsNullOrWhiteSpace(database) ? "caisse" : database.Trim();
            username = string.IsNullOrWhiteSpace(username) ? "caisse" : username.Trim();
            password = string.IsNullOrWhiteSpace(password) ? "caisse" : password;

            // 1. Test TCP sur le port demandé, avec détection automatique du port alternatif (5432 <-> 5433)
            int resolvedPort = port;
            bool tcpOk = await TestTcpPortAsync(host, resolvedPort, 2500);
            if (!tcpOk)
            {
                int altPort = resolvedPort == 5432 ? 5433 : 5432;
                if (await TestTcpPortAsync(host, altPort, 2500))
                {
                    resolvedPort = altPort;
                    tcpOk = true;
                }
            }

            if (!tcpOk)
            {
                var ts = GetNetworkAndTailscaleStatus();
                string tsHint = !ts.IsConnected
                    ? "\n• Tailscale ne semble pas connecté sur CE poste (vérifiez l'icône Tailscale près de l'horloge)."
                    : $"\n• Ce poste est bien sur Tailscale ({ts.TailscaleIp}).";

                return new SecondaryConnectionTestResult(
                    false,
                    port,
                    $"Impossible de joindre le serveur PostgreSQL sur {host}:{port} (ni sur le port {(port == 5432 ? 5433 : 5432)})." +
                    tsHint +
                    "\n• Vérifiez que le Poste Principal est allumé et que le bouton « Configurer automatiquement le Poste Principal » y a été exécuté.");
            }

            // 2. Test d'authentification et initialisation/vérification du schéma PostgreSQL
            var testSettings = new DatabaseSettings
            {
                Provider = DatabaseProvider.PostgreSql,
                Host = host,
                Port = resolvedPort,
                Database = database,
                Username = username,
                Password = password
            };

            var factory = new CaisseDbContextFactory(testSettings, $"SystemeCaisse-Test-{Environment.MachineName}");
            var bootstrapper = new DatabaseBootstrapper(factory);
            var boot = await bootstrapper.InitializeAsync(null, ct, backupBeforeMigration: false);

            if (!boot.IsReady)
            {
                return new SecondaryConnectionTestResult(false, resolvedPort, $"Serveur joint sur {host}:{resolvedPort}, mais erreur PostgreSQL : {boot.Message}");
            }

            int productCount = 0;
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                productCount = await db.Produits.CountAsync(ct);
            }
            catch { }

            return new SecondaryConnectionTestResult(
                true,
                resolvedPort,
                $"✅ Connexion réussie au Poste Principal ({host}:{resolvedPort}/{database}) — {productCount} produit(s) disponibles.",
                productCount);
        }

        // ═══════════════════════════ 3. Provisioning Autonome Poste Principal ═══════════════════════════

        /// <summary>
        /// Configure de manière 100% autonome le Poste Principal :
        /// 1. Démarrage et maintien du service Tailscale
        /// 2. Détection ou installation automatique de PostgreSQL (service Windows)
        /// 3. Réparation/Configuration sans BOM de postgresql.conf (listen_addresses='*', keepalives) et pg_hba.conf (Tailscale 100.64.0.0/10 + LAN)
        /// 4. Ouverture des ports TCP 5432/5433 dans le Pare-feu Windows
        /// 5. Création du rôle 'caisse' et de la base 'caisse'
        /// 6. Création de toutes les tables/colonnes (DatabaseBootstrapper) et reprise automatique des données SQLite + images
        /// </summary>
        public async Task<ProvisioningResult> ProvisionPrincipalServerAsync(
            string databaseName = "caisse",
            string username = "caisse",
            string password = "caisse",
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            databaseName = string.IsNullOrWhiteSpace(databaseName) ? "caisse" : databaseName.Trim();
            username = string.IsNullOrWhiteSpace(username) ? "caisse" : username.Trim();
            password = string.IsNullOrWhiteSpace(password) ? "caisse" : password;

            try
            {
                progress?.Report("1/6 — Vérification du réseau Tailscale et des services Windows…");
                var netBefore = GetNetworkAndTailscaleStatus();

                // 1. Exécuter le script système (PostgreSQL + pg_hba.conf + postgresql.conf + Pare-feu + Tailscale)
                progress?.Report("2/6 — Configuration autonome du serveur PostgreSQL et du Pare-feu Windows…");
                var (sysOk, detectedPort, sysLog) = await RunSystemProvisioningScriptAsync(progress, ct);
                if (!sysOk)
                {
                    return new ProvisioningResult(false, sysLog, detectedPort, netBefore.TailscaleIp, netBefore.LanIp);
                }

                // 2. Créer/mettre à jour le rôle PostgreSQL 'caisse' et la base 'caisse'
                progress?.Report($"3/6 — Préparation du rôle « {username} » et de la base « {databaseName} » sur le port {detectedPort}…");
                var (roleOk, roleErr) = await EnsurePostgresRoleAndDatabaseAsync(detectedPort, databaseName, username, password, ct);
                if (!roleOk)
                {
                    return new ProvisioningResult(false, $"Le service PostgreSQL tourne sur le port {detectedPort}, mais la création du rôle/base a échoué : {roleErr}", detectedPort, netBefore.TailscaleIp, netBefore.LanIp);
                }

                // 3. Créer toutes les tables et appliquer les migrations EF Core sur PostgreSQL
                progress?.Report("4/6 — Création et mise à jour de toutes les tables PostgreSQL…");
                var pgSettings = new DatabaseSettings
                {
                    Provider = DatabaseProvider.PostgreSql,
                    Host = "localhost",
                    Port = detectedPort,
                    Database = databaseName,
                    Username = username,
                    Password = password,
                    SqlitePath = _settingsStore.Current.Database.SqlitePath
                };

                var pgFactory = new CaisseDbContextFactory(pgSettings, "SystemeCaisse-PrincipalSetup");
                var bootstrapper = new DatabaseBootstrapper(pgFactory);
                var bootResult = await bootstrapper.InitializeAsync(null, ct, backupBeforeMigration: false);
                if (!bootResult.IsReady)
                {
                    return new ProvisioningResult(false, $"Échec de création des tables PostgreSQL : {bootResult.Message}", detectedPort, netBefore.TailscaleIp, netBefore.LanIp);
                }

                // 4. Si la base PostgreSQL est vide, importer automatiquement l'ancienne base SQLite locale + les images
                int importedRows = 0;
                int importedImages = 0;
                if (await bootstrapper.IsDatabaseEmptyAsync(ct))
                {
                    var sqliteCandidate = FindExistingLocalSqliteDatabase();
                    if (!string.IsNullOrWhiteSpace(sqliteCandidate) && File.Exists(sqliteCandidate))
                    {
                        progress?.Report($"5/6 — Importation automatique des données existantes ({Path.GetFileName(sqliteCandidate)}) et des photos…");
                        try
                        {
                            var transfer = new DataTransferService(pgFactory);
                            var report = await transfer.ImportSqliteAsync(sqliteCandidate, replaceExisting: true, progress, ct);
                            importedRows = report.TotalRows;
                            importedImages = report.ImagesImported;
                        }
                        catch (Exception impEx)
                        {
                            AppLog.Warn("Provisioning", $"Import automatique SQLite vers PostgreSQL partiel : {impEx.Message}");
                        }
                    }
                    else
                    {
                        // Importer quand même les images du dossier d'installation si présentes
                        progress?.Report("5/6 — Vérification des images produits locales…");
                        try
                        {
                            var imgStore = new ImageStoreService(pgFactory);
                            importedImages = await imgStore.ImportReferencedImagesAsync(AppPaths.InstallDir, ct);
                        }
                        catch { }
                    }
                }
                else
                {
                    progress?.Report("5/6 — Synchronisation des images produits dans PostgreSQL…");
                    try
                    {
                        var imgStore = new ImageStoreService(pgFactory);
                        importedImages = await imgStore.ImportReferencedImagesAsync(AppPaths.InstallDir, ct);
                    }
                    catch { }
                }

                // 5. Enregistrer la configuration du Poste Principal dans poste.json
                progress?.Report("6/6 — Enregistrement de la configuration Poste Principal…");
                _settingsStore.Update(s =>
                {
                    s.EstPostePrincipal = true;
                    s.Database.Provider = DatabaseProvider.PostgreSql;
                    s.Database.Host = "localhost";
                    s.Database.Port = detectedPort;
                    s.Database.Database = databaseName;
                    s.Database.Username = username;
                    s.Database.Password = password;
                });

                // Désactiver l'ancien mode SMB network_db.json s'il existait pour éviter toute confusion
                try
                {
                    var legacyJson = Path.Combine(AppPaths.InstallDir, "network_db.json");
                    if (File.Exists(legacyJson)) File.Delete(legacyJson);
                }
                catch { }

                var netAfter = GetNetworkAndTailscaleStatus();

                var msg = new StringBuilder();
                msg.AppendLine($"✅ Poste Principal PostgreSQL opérationnel (Port {detectedPort}, Base « {databaseName} »).");
                msg.AppendLine($"• Toutes les tables et colonnes sont créées et prêtes.");
                if (importedRows > 0 || importedImages > 0)
                    msg.AppendLine($"• Données reprises : {importedRows} enregistrement(s), {importedImages} image(s).");
                if (!string.IsNullOrEmpty(netAfter.TailscaleIp))
                    msg.AppendLine($"• Adresse IP Tailscale à saisir sur les Postes Secondaires : {netAfter.TailscaleIp} (Port : {detectedPort})");
                else if (!string.IsNullOrEmpty(netAfter.LanIp))
                    msg.AppendLine($"• Adresse IP Réseau Local : {netAfter.LanIp} (Port : {detectedPort}) — Connectez Tailscale pour le multi-site.");

                AppLog.Info("Provisioning", msg.ToString().Trim());
                return new ProvisioningResult(true, msg.ToString().Trim(), detectedPort, netAfter.TailscaleIp, netAfter.LanIp, importedRows, importedImages);
            }
            catch (Exception ex)
            {
                AppLog.Error("Provisioning", "Erreur lors du provisioning du Poste Principal", ex);
                var net = GetNetworkAndTailscaleStatus();
                return new ProvisioningResult(false, $"Erreur : {ex.InnerException?.Message ?? ex.Message}", 5432, net.TailscaleIp, net.LanIp);
            }
        }

        private string? FindExistingLocalSqliteDatabase()
        {
            var candidates = new List<string?>
            {
                _settingsStore.Current.Database.SqlitePath,
                Path.Combine(AppPaths.InstallDir, "caisse.db"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "caisse.db"),
                AppPaths.DefaultSqlitePath
            };

            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                if (c.StartsWith(@"\\", StringComparison.Ordinal)) continue; // ignorer chemin SMB
                if (File.Exists(c) && new FileInfo(c).Length > 0) return c;
            }
            return null;
        }

        private static async Task<(bool Success, string? Error)> EnsurePostgresRoleAndDatabaseAsync(
            int port,
            string databaseName,
            string username,
            string password,
            CancellationToken ct)
        {
            // Essayer de se connecter en tant que superuser 'postgres' (trust local configuré par le script) ou directement avec 'username'
            var candidateUsers = new[]
            {
                ("postgres", "postgres"),
                ("postgres", ""),
                ("postgres", password),
                (username, password),
                ("postgres", "caisse"),
                (Environment.UserName, "")
            };
            NpgsqlConnection? openConn = null;
            Exception? lastEx = null;

            foreach (var (u, p) in candidateUsers)
            {
                if (string.IsNullOrWhiteSpace(u)) continue;
                try
                {
                    var csb = new NpgsqlConnectionStringBuilder
                    {
                        Host = "127.0.0.1",
                        Port = port,
                        Database = "postgres",
                        Username = u,
                        Password = string.IsNullOrEmpty(p) ? null : p,
                        Timeout = 5,
                        CommandTimeout = 30,
                        Pooling = false
                    };
                    var conn = new NpgsqlConnection(csb.ConnectionString);
                    await conn.OpenAsync(ct);
                    openConn = conn;
                    break;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (openConn == null)
            {
                return (false, lastEx?.Message ?? "Impossible de se connecter à l'instance PostgreSQL locale (127.0.0.1).");
            }

            await using (openConn)
            {
                var safeUser = username.Replace("'", "''").Replace("\"", "\"\"");
                var safePass = password.Replace("'", "''");
                var safeDb = databaseName.Replace("\"", "\"\"");

                // 1. Créer ou mettre à jour le rôle
                var roleSql = $@"
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '{safeUser}') THEN
        CREATE ROLE ""{safeUser}"" WITH LOGIN SUPERUSER CREATEDB PASSWORD '{safePass}';
    ELSE
        ALTER ROLE ""{safeUser}"" WITH LOGIN SUPERUSER CREATEDB PASSWORD '{safePass}';
    END IF;
END
$$;";
                await using (var roleCmd = new NpgsqlCommand(roleSql, openConn))
                {
                    await roleCmd.ExecuteNonQueryAsync(ct);
                }

                // 2. Créer la base si elle n'existe pas encore
                bool dbExists;
                await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @db", openConn))
                {
                    checkCmd.Parameters.AddWithValue("db", databaseName);
                    dbExists = await checkCmd.ExecuteScalarAsync(ct) != null;
                }

                if (!dbExists)
                {
                    await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{safeDb}\" OWNER \"{safeUser}\" ENCODING 'UTF8'", openConn);
                    await createCmd.ExecuteNonQueryAsync(ct);
                }
            }

            return (true, null);
        }

        private static bool IsRunningAsAdministrator()
        {
            if (!OperatingSystem.IsWindows()) return true;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static async Task<(bool Success, int DetectedPort, string LogOutput)> RunSystemProvisioningScriptAsync(
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "SystemeCaisse_pgsetup");
            Directory.CreateDirectory(tempDir);
            var scriptPath = Path.Combine(tempDir, "ProvisionPostgresPrincipal.ps1");
            var resultPath = Path.Combine(tempDir, "result.txt");
            if (File.Exists(resultPath)) File.Delete(resultPath);

            var scriptContent = BuildPowerShellProvisioningScript(resultPath);
            // IMPORTANT : UTF-8 avec BOM pour le script .ps1 lui-même afin que PowerShell 5.1 lise correctement les accents,
            // mais à l'intérieur du script nous utilisons UTF8Encoding($false) (SANS BOM) pour postgresql.conf et pg_hba.conf !
            await File.WriteAllTextAsync(scriptPath, scriptContent, new UTF8Encoding(true), ct);

            bool isAdmin = IsRunningAsAdministrator();
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = !isAdmin,
                Verb = isAdmin ? "" : "runas",
                CreateNoWindow = isAdmin,
                WindowStyle = isAdmin ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null)
                    return (false, 5432, "Impossible de lancer PowerShell pour configurer PostgreSQL.");

                await proc.WaitForExitAsync(ct);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (false, 5432, "Autorisation Administrateur refusée (UAC). Veuillez accepter l'invitation Administrateur pour configurer PostgreSQL et le Pare-feu.");
            }

            if (!File.Exists(resultPath))
            {
                return (false, 5432, "Le script de configuration PostgreSQL ne s'est pas terminé normalement.");
            }

            var lines = await File.ReadAllLinesAsync(resultPath, ct);
            bool ok = lines.Any(l => l.StartsWith("STATUS=OK", StringComparison.OrdinalIgnoreCase));
            int port = 5432;
            var portLine = lines.FirstOrDefault(l => l.StartsWith("PORT=", StringComparison.OrdinalIgnoreCase));
            if (portLine != null && int.TryParse(portLine["PORT=".Length..].Trim(), out int parsedPort) && parsedPort > 0)
            {
                port = parsedPort;
            }

            var messageLines = lines.Where(l => !l.StartsWith("STATUS=") && !l.StartsWith("PORT=")).ToList();
            return (ok, port, string.Join(Environment.NewLine, messageLines));
        }

        private static string BuildPowerShellProvisioningScript(string resultFilePath)
        {
            var escapedResultPath = resultFilePath.Replace("'", "''");
            return $@"
$ErrorActionPreference = 'Continue'
$resultFile = '{escapedResultPath}'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$logs = New-Object System.Collections.Generic.List[string]

function Log-Step($msg) {{
    Write-Host $msg
    $logs.Add($msg)
}}

function Write-ResultAndExit($status, $portVal, $exitCode) {{
    $logs.Insert(0, ""STATUS=$status"")
    $logs.Insert(1, ""PORT=$portVal"")
    [System.IO.File]::WriteAllLines($resultFile, $logs, $utf8NoBom)
    exit $exitCode
}}

function Strip-BomAndLeadingGarbage([string]$text) {{
    if ([string]::IsNullOrEmpty($text)) {{ return '' }}
    # Supprimer tout caractère BOM UTF-8 (0xFEFF) ou séquences UTF-8 BOM mal décodées (ï»¿)
    $clean = $text.Replace([string][char]0xFEFF, '').Replace('ï»¿', '')
    # Supprimer tout octet non-ASCII éventuellement présent avant le premier commentaire '#' ou la première lettre
    $clean = [regex]::Replace($clean, '^[^\x09\x0A\x0D\x20-\x7E]+', '')
    return $clean
}}

function Test-LocalTcpPort([int]$testPort) {{
    try {{
        $client = New-Object System.Net.Sockets.TcpClient
        $iar = $client.BeginConnect('127.0.0.1', $testPort, $null, $null)
        $wait = $iar.AsyncWaitHandle.WaitOne(600, $false)
        if ($wait -and $client.Connected) {{
            $client.Close()
            return $true
        }}
        $client.Close()
    }} catch {{}}
    return $false
}}

try {{
    # 1. S'assurer que le service Tailscale est démarré et en démarrage Automatique
    $tsSvc = Get-Service -Name 'Tailscale' -ErrorAction SilentlyContinue
    if ($tsSvc) {{
        Set-Service -Name 'Tailscale' -StartupType Automatic -ErrorAction SilentlyContinue
        if ($tsSvc.Status -ne 'Running') {{
            Start-Service -Name 'Tailscale' -ErrorAction SilentlyContinue
            Log-Step 'Service Tailscale démarré et configuré en démarrage automatique.'
        }}
    }}

    # 2. Rechercher les services PostgreSQL existants (en privilégiant celui déjà en cours d'exécution)
    $allPgSvcs = @(Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue | Sort-Object @{{Expression={{$_.Status -eq 'Running'}}; Descending=$true}}, Name -Descending)
    $pgSvc = $allPgSvcs | Select-Object -First 1
    $pgBinDir = $null
    $pgDataDir = $null

    if (-not $pgSvc) {{
        $searchRoots = @('C:\Program Files\PostgreSQL', 'C:\Program Files (x86)\PostgreSQL', 'C:\ProgramData\SystemeCaisse\pgsql')
        foreach ($root in $searchRoots) {{
            if (Test-Path $root) {{
                $foundCtl = Get-ChildItem -Path $root -Filter 'pg_ctl.exe' -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
                if ($foundCtl) {{
                    $pgBinDir = $foundCtl.DirectoryName
                    break
                }}
            }}
        }}
    }}

    # 3. Si PostgreSQL n'est pas installé, l'installer automatiquement via winget ou installeur officiel EDB
    if (-not $pgSvc -and -not $pgBinDir) {{
        Log-Step 'PostgreSQL non détecté : lancement de l installation automatique de PostgreSQL 16...'
        $wingetCmd = Get-Command winget.exe -ErrorAction SilentlyContinue
        if ($wingetCmd) {{
            & winget.exe install --id PostgreSQL.PostgreSQL.16 --silent --accept-package-agreements --accept-source-agreements --override ""--unattendedmodeui minimal --mode unattended --superpassword postgres --servicename postgresql-x64-16 --serverport 5432""
        }}

        $allPgSvcs = @(Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue | Sort-Object @{{Expression={{$_.Status -eq 'Running'}}; Descending=$true}}, Name -Descending)
        $pgSvc = $allPgSvcs | Select-Object -First 1
        if (-not $pgSvc -and -not (Test-Path 'C:\Program Files\PostgreSQL')) {{
            Log-Step 'Téléchargement de l installeur officiel PostgreSQL 16...'
            $installerUrl = 'https://get.enterprisedb.com/postgresql/postgresql-16.4-1-windows-x64.exe'
            $installerPath = Join-Path $env:TEMP 'postgresql-16-setup.exe'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing
            if (Test-Path $installerPath) {{
                Start-Process -FilePath $installerPath -ArgumentList '--unattendedmodeui minimal --mode unattended --superpassword postgres --servicename postgresql-x64-16 --serverport 5432' -Wait
            }}
        }}
        $allPgSvcs = @(Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue | Sort-Object @{{Expression={{$_.Status -eq 'Running'}}; Descending=$true}}, Name -Descending)
        $pgSvc = $allPgSvcs | Select-Object -First 1
    }}

    # 4. Identifier le dossier bin et le dossier data de PostgreSQL (via Registre Windows + WMI)
    if ($pgSvc) {{
        $svcImagePath = $null
        try {{
            $regKey = ""HKLM:\SYSTEM\CurrentControlSet\Services\$($pgSvc.Name)""
            if (Test-Path $regKey) {{
                $svcImagePath = (Get-ItemProperty -Path $regKey -ErrorAction SilentlyContinue).ImagePath
            }}
        }} catch {{}}

        if (-not $svcImagePath) {{
            $wmiSvc = Get-CimInstance Win32_Service -Filter ""Name='$($pgSvc.Name)'"" -ErrorAction SilentlyContinue
            if ($wmiSvc) {{ $svcImagePath = $wmiSvc.PathName }}
        }}

        if ($svcImagePath) {{
            if ($svcImagePath -match '-D\s+""([^""]+)""') {{
                $pgDataDir = $Matches[1]
            }} elseif ($svcImagePath -match '-D\s+(\S+)') {{
                $pgDataDir = $Matches[1]
            }}
            if ($svcImagePath -match '""([^""]+pg_ctl\.exe)""') {{
                $pgBinDir = Split-Path $Matches[1] -Parent
            }} elseif ($svcImagePath -match '(\S+pg_ctl\.exe)') {{
                $pgBinDir = Split-Path $Matches[1] -Parent
            }}
        }}
    }}

    if (-not $pgBinDir -and (Test-Path 'C:\Program Files\PostgreSQL')) {{
        $foundCtl = Get-ChildItem -Path 'C:\Program Files\PostgreSQL' -Filter 'pg_ctl.exe' -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($foundCtl) {{ $pgBinDir = $foundCtl.DirectoryName }}
    }}

    if (-not $pgDataDir -and $pgBinDir) {{
        $parentDir = Split-Path $pgBinDir -Parent
        $candidateData = Join-Path $parentDir 'data'
        if (Test-Path (Join-Path $candidateData 'postgresql.conf')) {{
            $pgDataDir = $candidateData
        }} else {{
            $pgDataDir = 'C:\ProgramData\SystemeCaisse\pgdata'
            if (-not (Test-Path (Join-Path $pgDataDir 'postgresql.conf'))) {{
                New-Item -ItemType Directory -Force -Path $pgDataDir | Out-Null
                & icacls.exe ""$pgDataDir"" /grant ""*S-1-5-20:(OI)(CI)F"" ""*S-1-5-18:(OI)(CI)F"" ""*S-1-5-32-544:(OI)(CI)F"" /T /C /Q | Out-Null
                $initDb = Join-Path $pgBinDir 'initdb.exe'
                & $initDb -D $pgDataDir -U postgres -E UTF8 --locale=C --auth-local=trust --auth-host=md5 | Out-Null
            }}
            if (-not $pgSvc) {{
                $pgCtl = Join-Path $pgBinDir 'pg_ctl.exe'
                & $pgCtl register -N 'postgresql-systemecaisse' -D $pgDataDir | Out-Null
                $pgSvc = Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue | Select-Object -First 1
            }}
        }}
    }}

    if (-not $pgDataDir -or -not (Test-Path (Join-Path $pgDataDir 'postgresql.conf'))) {{
        $logs.Add('Impossible de localiser ou initialiser le dossier data de PostgreSQL.')
        Write-ResultAndExit 'ERROR' 5432 1
    }}

    # 5. Nettoyer (réparer tout BOM UTF-8 éventuel) et configurer postgresql.conf SANS BOM
    $confPath = Join-Path $pgDataDir 'postgresql.conf'
    $rawConf = [System.IO.File]::ReadAllText($confPath, [System.Text.Encoding]::UTF8)
    $confText = Strip-BomAndLeadingGarbage $rawConf

    # Supprimer tout ancien bloc SystemeCaisse pour éviter les doublons
    $confText = [regex]::Replace($confText, '(?ms)# === SYSTEMECAISSE MULTI-POSTES ===.*?# === FIN SYSTEMECAISSE ===\r?\n?', '')
    $confText = [regex]::Replace($confText, '(?m)^.*# SystemeCaisse Multi-Postes.*$\r?\n?', '')
    $confText = [regex]::Replace($confText, '(?ms)# SystemeCaisse KeepAlive Tailscale.*?tcp_keepalives_count\s*=\s*\d+\r?\n?', '')

    # Lire le port configuré dans postgresql.conf
    $port = 5432
    if ($confText -match '(?m)^\s*port\s*=\s*(\d+)') {{
        $port = [int]$Matches[1]
    }}

    # Commenter les anciennes lignes listen_addresses actives pour éviter tout conflit
    $confText = [regex]::Replace($confText, '(?m)^(\s*listen_addresses\s*=.*)$', '#$1')

    $caisseConfBlock = @""

# === SYSTEMECAISSE MULTI-POSTES ===
listen_addresses = '*'
tcp_keepalives_idle = 60
tcp_keepalives_interval = 10
tcp_keepalives_count = 6
# === FIN SYSTEMECAISSE ===
""@
    $confText = $confText.TrimEnd() + ""`r`n"" + $caisseConfBlock + ""`r`n""
    [System.IO.File]::WriteAllText($confPath, $confText, $utf8NoBom)

    # 6. Nettoyer (réparer tout BOM UTF-8 éventuel) et configurer pg_hba.conf SANS BOM
    $hbaPath = Join-Path $pgDataDir 'pg_hba.conf'
    $rawHba = [System.IO.File]::ReadAllText($hbaPath, [System.Text.Encoding]::UTF8)
    $hbaText = Strip-BomAndLeadingGarbage $rawHba

    # Supprimer l'ancien bloc SystemeCaisse s'il existe déjà avant de le réinsérer proprement en tête
    $hbaText = [regex]::Replace($hbaText, '(?ms)# === SYSTEMECAISSE MULTI-POSTES \(TAILSCALE & LAN\) ===.*?# === FIN SYSTEMECAISSE ===\r?\n?', '')
    $hbaText = Strip-BomAndLeadingGarbage $hbaText

    $caisseHbaRules = @""
# === SYSTEMECAISSE MULTI-POSTES (TAILSCALE & LAN) ===
host    all             all             127.0.0.1/32            trust
host    all             all             ::1/128                 trust
host    all             all             100.64.0.0/10           md5
host    all             all             192.168.0.0/16          md5
host    all             all             10.0.0.0/8              md5
host    all             all             172.16.0.0/12           md5
# === FIN SYSTEMECAISSE ===
""@
    $hbaText = $caisseHbaRules + ""`r`n"" + $hbaText.TrimStart()
    [System.IO.File]::WriteAllText($hbaPath, $hbaText, $utf8NoBom)

    # 7. Garantir les permissions NTFS sur le dossier data et les fichiers .conf (NetworkService *S-1-5-20, SYSTEM *S-1-5-18, Admins *S-1-5-32-544)
    & icacls.exe ""$pgDataDir"" /grant ""*S-1-5-20:(OI)(CI)F"" ""*S-1-5-18:(OI)(CI)F"" ""*S-1-5-32-544:(OI)(CI)F"" /C /Q | Out-Null
    & icacls.exe ""$confPath"" /grant ""*S-1-5-20:F"" ""*S-1-5-18:F"" ""*S-1-5-32-544:F"" /C /Q | Out-Null
    & icacls.exe ""$hbaPath"" /grant ""*S-1-5-20:F"" ""*S-1-5-18:F"" ""*S-1-5-32-544:F"" /C /Q | Out-Null

    # 8. Démarrer / Redémarrer le service PostgreSQL avec auto-réparation en cas d'échec
    $serviceStarted = $false
    if ($pgSvc) {{
        Set-Service -Name $pgSvc.Name -StartupType Automatic -ErrorAction SilentlyContinue
        & sc.exe failure $pgSvc.Name reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

        # Arrêter proprement si en cours d'exécution pour prendre en compte listen_addresses = '*'
        $pgSvc.Refresh()
        if ($pgSvc.Status -eq 'Running') {{
            Stop-Service -Name $pgSvc.Name -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        }}

        # Si aucun processus postgres.exe ne tourne depuis ce dossier data, supprimer un éventuel postmaster.pid orphelin
        $pidFile = Join-Path $pgDataDir 'postmaster.pid'
        if (Test-Path $pidFile) {{
            $runningPg = Get-Process -Name 'postgres' -ErrorAction SilentlyContinue
            if (-not $runningPg) {{
                Remove-Item -Path $pidFile -Force -ErrorAction SilentlyContinue
            }}
        }}

        # Si le port configuré est déjà occupé par un autre processus alors que notre service est arrêté, basculer sur le port alternatif (5432 <-> 5433)
        if (Test-LocalTcpPort $port) {{
            $altPort = if ($port -eq 5432) {{ 5433 }} else {{ 5432 }}
            if (-not (Test-LocalTcpPort $altPort)) {{
                Log-Step ""Le port $port est déjà utilisé par un autre service : basculement de $($pgSvc.Name) sur le port $altPort.""
                $port = $altPort
                $confText = [regex]::Replace($confText, '(?m)^\s*port\s*=\s*\d+', ""port = $port"")
                if ($confText -notmatch '(?m)^\s*port\s*=') {{
                    $confText += ""`r`nport = $port`r`n""
                }}
                [System.IO.File]::WriteAllText($confPath, $confText, $utf8NoBom)
            }}
        }}

        # Tentative 1 : Démarrage normal du service Windows
        try {{
            Start-Service -Name $pgSvc.Name -ErrorAction Stop
            Start-Sleep -Seconds 2
            $pgSvc.Refresh()
            if ($pgSvc.Status -eq 'Running') {{ $serviceStarted = $true }}
        }} catch {{
            Log-Step ""Tentative 1 de démarrage de $($pgSvc.Name) échouée : $($_.Exception.Message). Auto-réparation du compte de service...""
        }}

        # Tentative 2 : Réparation du compte de service Windows (NT AUTHORITY\NetworkService) + permissions récursives sur data
        if (-not $serviceStarted) {{
            & sc.exe config $pgSvc.Name obj= ""NT AUTHORITY\NetworkService"" password= """" | Out-Null
            & icacls.exe ""$pgDataDir"" /grant ""*S-1-5-20:(OI)(CI)F"" ""*S-1-5-18:(OI)(CI)F"" ""*S-1-5-32-544:(OI)(CI)F"" /T /C /Q | Out-Null
            if (Test-Path $pidFile) {{ Remove-Item -Path $pidFile -Force -ErrorAction SilentlyContinue }}
            try {{
                Start-Service -Name $pgSvc.Name -ErrorAction Stop
                Start-Sleep -Seconds 2
                $pgSvc.Refresh()
                if ($pgSvc.Status -eq 'Running') {{ $serviceStarted = $true }}
            }} catch {{
                Log-Step ""Tentative 2 échouée : $($_.Exception.Message).""
            }}
        }}

        # Tentative 3 : Démarrage direct via pg_ctl.exe si le gestionnaire de services Windows bloque
        if (-not $serviceStarted -and $pgBinDir) {{
            $pgCtlExe = Join-Path $pgBinDir 'pg_ctl.exe'
            if (Test-Path $pgCtlExe) {{
                if (Test-Path $pidFile) {{ Remove-Item -Path $pidFile -Force -ErrorAction SilentlyContinue }}
                $pgLogFile = Join-Path $env:TEMP 'systemecaisse_pgctl.log'
                & $pgCtlExe start -D ""$pgDataDir"" -l ""$pgLogFile"" -w | Out-Null
                Start-Sleep -Seconds 2
                if (Test-LocalTcpPort $port) {{
                    $serviceStarted = $true
                    Log-Step ""Serveur PostgreSQL démarré avec succès via pg_ctl sur le port $port.""
                }}
            }}
        }}

        if (-not $serviceStarted) {{
            # Lire la dernière erreur du journal PostgreSQL pour un diagnostic précis
            $pgErrDetail = ''
            $logDir = Join-Path $pgDataDir 'log'
            if (-not (Test-Path $logDir)) {{ $logDir = Join-Path $pgDataDir 'pg_log' }}
            if (Test-Path $logDir) {{
                $latestLog = Get-ChildItem -Path $logDir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                if ($latestLog) {{
                    $lastLines = Get-Content -Path $latestLog.FullName -Tail 8 -ErrorAction SilentlyContinue
                    if ($lastLines) {{ $pgErrDetail = "" | Détail log PostgreSQL : "" + ($lastLines -join ' / ') }}
                }}
            }}
            $logs.Add(""Impossible de démarrer le service « $($pgSvc.Name) » sur le port $port.$pgErrDetail"")
            Write-ResultAndExit 'ERROR' $port 1
        }}
    }}

    # 9. Ouvrir les ports PostgreSQL dans le Pare-feu Windows (Tailscale + Réseau Local)
    & netsh advfirewall firewall delete rule name=""SystemeCaisse PostgreSQL Multi-Postes"" | Out-Null
    & netsh advfirewall firewall add rule name=""SystemeCaisse PostgreSQL Multi-Postes"" dir=in action=allow protocol=TCP localport=""$port,5432,5433"" profile=any | Out-Null

    $svcLabel = if ($pgSvc) {{ $pgSvc.Name }} else {{ 'pg_ctl' }}
    $logs.Add(""Service PostgreSQL ($svcLabel) actif sur le port $port et Pare-feu configuré."")
    Write-ResultAndExit 'OK' $port 0
}} catch {{
    $logs.Add(""Erreur script système : $($_.Exception.Message)"")
    Write-ResultAndExit 'ERROR' 5432 1
}}
";
        }
    }
}
