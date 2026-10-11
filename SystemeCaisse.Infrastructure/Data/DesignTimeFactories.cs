using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SystemeCaisse.Infrastructure.Configuration;

namespace SystemeCaisse.Infrastructure.Data
{
    /// <summary>
    /// Fabrique « design-time » pour `dotnet ef` (migrations SQLite).
    /// Exemple : dotnet ef migrations add NomMigration --context AppDbContext --output-dir Migrations/Sqlite
    /// </summary>
    public class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var path = Path.Combine(Path.GetTempPath(), "systemecaisse_design.db");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            return new AppDbContext(options);
        }
    }

    /// <summary>
    /// Fabrique « design-time » pour `dotnet ef` (migrations PostgreSQL).
    /// Exemple : dotnet ef migrations add NomMigration --context PostgresAppDbContext --output-dir Migrations/Postgres
    /// La connexion n'est utilisée que pour `database update` (variable SYSTEMECAISSE_PG).
    /// </summary>
    public class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
    {
        public PostgresAppDbContext CreateDbContext(string[] args)
        {
            var cs = Environment.GetEnvironmentVariable("SYSTEMECAISSE_PG")
                     ?? "Host=localhost;Port=5433;Database=caisse_design;Username=caisse;Password=caisse";
            var options = new DbContextOptionsBuilder<PostgresAppDbContext>()
                .UseNpgsql(cs)
                .Options;
            return new PostgresAppDbContext(options);
        }
    }
}
