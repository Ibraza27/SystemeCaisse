using Microsoft.EntityFrameworkCore;

namespace SystemeCaisse.Infrastructure.Data
{
    /// <summary>
    /// Contexte PostgreSQL (mode Serveur multi-poste). Même modèle que <see cref="AppDbContext"/>,
    /// mais avec ses propres migrations (dossier Migrations/Postgres).
    /// </summary>
    public class PostgresAppDbContext : AppDbContext
    {
        public PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : base(options)
        {
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);

            // L'application manipule des heures LOCALES (DateTime.Now) comme avec SQLite.
            // Npgsql mappe par défaut DateTime vers "timestamp with time zone" et refuse
            // les DateTime Kind=Local : on force donc "timestamp without time zone".
            configurationBuilder.Properties<DateTime>().HaveColumnType("timestamp without time zone")
                .HaveConversion<LocalDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>().HaveColumnType("timestamp without time zone")
                .HaveConversion<LocalDateTimeConverter>();
        }
    }

    /// <summary>
    /// Écrit toujours une heure locale « sans fuseau » : un DateTime Kind=Utc est converti en heure locale
    /// (sinon Npgsql refuse l'écriture dans une colonne timestamp without time zone).
    /// </summary>
    public sealed class LocalDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
    {
        public LocalDateTimeConverter()
            : base(
                v => DateTime.SpecifyKind(v.Kind == DateTimeKind.Utc ? v.ToLocalTime() : v, DateTimeKind.Unspecified),
                v => v)
        {
        }
    }
}
