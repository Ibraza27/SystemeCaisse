using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace SystemeCaisse.Infrastructure.Configuration
{
    /// <summary>Moteur de base de données utilisé par le poste.</summary>
    public enum DatabaseProvider
    {
        /// <summary>Fichier SQLite (poste seul, développement, transition).</summary>
        Sqlite = 0,
        /// <summary>Serveur PostgreSQL partagé (multi-poste).</summary>
        PostgreSql = 1
    }

    /// <summary>Paramètres de connexion à la base de données (propres au poste).</summary>
    public class DatabaseSettings
    {
        public DatabaseProvider Provider { get; set; } = DatabaseProvider.PostgreSql;

        /// <summary>Chemin du fichier SQLite. Null = emplacement par défaut (ProgramData).</summary>
        public string? SqlitePath { get; set; }

        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5433;
        public string Database { get; set; } = "caisse";
        public string Username { get; set; } = "caisse";

        /// <summary>Mot de passe chiffré DPAPI (portée machine), encodé en Base64.</summary>
        public string? PasswordProtected { get; set; }

        /// <summary>Mot de passe en clair (jamais sérialisé).</summary>
        [JsonIgnore]
        public string Password
        {
            get => string.IsNullOrEmpty(Unprotect(PasswordProtected)) ? "caisse" : Unprotect(PasswordProtected);
            set => PasswordProtected = Protect(value);
        }

        public DatabaseSettings Clone() => (DatabaseSettings)MemberwiseClone();

        /// <summary>Description lisible pour l'interface (sans mot de passe).</summary>
        [JsonIgnore]
        public string DisplayName => Provider == DatabaseProvider.PostgreSql
            ? $"Serveur PostgreSQL {Host}:{Port}/{Database}"
            : $"SQLite {SqlitePath ?? "(par défaut)"}";

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SystemeCaisse.Hippocampe.Db");

        private static string? Protect(string? plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            if (!OperatingSystem.IsWindows()) return Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(bytes);
        }

        private static string Unprotect(string? protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue)) return string.Empty;
            try
            {
                var raw = Convert.FromBase64String(protectedValue);
                if (!OperatingSystem.IsWindows()) return Encoding.UTF8.GetString(raw);
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.LocalMachine));
            }
            catch (Exception ex)
            {
                // Fichier copié depuis une autre machine : DPAPI ne peut pas déchiffrer.
                System.Diagnostics.Debug.WriteLine($"[Settings] Mot de passe illisible : {ex.Message}");
                return string.Empty;
            }
        }
    }

    /// <summary>Réglages des périphériques, propres à CHAQUE poste (ne jamais les mettre en base partagée).</summary>
    public class PeripheriquesSettings
    {
        public string? Imprimante { get; set; }
        public int LargeurTicketMm { get; set; } = 80;
        public bool AfficherLogoTicket { get; set; } = true;

        public bool EcranClientActif { get; set; } = true;
        public int EcranClientIndex { get; set; } = -1;
        public bool EcranClientPromotions { get; set; } = true;
        public bool EcranClientCompact { get; set; }

        public bool BalanceActive { get; set; }
        public string BalancePort { get; set; } = "COM3";
        public int BalanceBaud { get; set; } = 9600;

        public bool TpeActif { get; set; }
        public string TpePort { get; set; } = "COM1";
        public int TpeBaud { get; set; } = 9600;
        public int TpeTimeout { get; set; } = 60;

        public bool ModeFormation { get; set; }
    }

    /// <summary>
    /// Réglages locaux du poste, stockés dans C:\ProgramData\SystemeCaisse\poste.json.
    /// Ce fichier survit aux réinstallations (contrairement à l'ancien network_db.json
    /// placé à côté de l'exécutable — voir CONTEXT.md §2).
    /// </summary>
    public class PosteSettings
    {
        public int FormatVersion { get; set; } = 1;

        /// <summary>Nom du poste affiché dans l'en-tête et enregistré sur les ventes.</summary>
        public string NomPoste { get; set; } = Environment.MachineName;

        /// <summary>
        /// Poste principal : seul ce poste exécute la synchro TransitManager
        /// et les sauvegardes automatiques quotidiennes.
        /// </summary>
        public bool EstPostePrincipal { get; set; } = true;

        public DatabaseSettings Database { get; set; } = new();

        public PeripheriquesSettings Peripheriques { get; set; } = new();

        /// <summary>True une fois les réglages périphériques repris depuis l'ancienne table Configuration.</summary>
        public bool PeripheriquesImportes { get; set; }

        /// <summary>Préfixe court utilisé dans les numéros de ticket (dérivé du nom du poste).</summary>
        [JsonIgnore]
        public string CodePoste
        {
            get
            {
                var letters = new string((NomPoste ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
                if (letters.Length == 0) return "P";
                return letters.Length <= 4 ? letters : letters[..2] + letters[^2..];
            }
        }
    }
}
