using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;

namespace SystemeCaisse.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        /// <summary>
        /// Static flag set by the UI layer at startup to indicate if we're using a network database.
        /// When true, WAL mode is disabled (incompatible with SMB network shares).
        /// </summary>
        public static bool IsNetworkDatabaseMode { get; set; }
        public DbSet<Produit> Produits { get; set; }
        public DbSet<Entreprise> Entreprise { get; set; }
        public DbSet<SystemConfiguration> Configuration { get; set; }
        public DbSet<Vente> Ventes { get; set; }
        public DbSet<LigneVente> LignesVente { get; set; }
        public DbSet<MouvementStock> MouvementsStock { get; set; }
        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<PromotionTier> PromotionTiers { get; set; }
        public DbSet<PromotionBundleItem> PromotionBundleItems { get; set; }
        public DbSet<Inventaire> Inventaires { get; set; }
        public DbSet<LigneInventaire> LignesInventaire { get; set; }
        public DbSet<Fournisseur> Fournisseurs { get; set; }
        public DbSet<CommandeFournisseur> CommandesFournisseurs { get; set; }
        public DbSet<HistoriqueAction> HistoriqueActions { get; set; }
        public DbSet<PlanningPromotion> PlanningPromotions { get; set; }
        public DbSet<Commande> Commandes { get; set; }
        public DbSet<LigneCommande> LignesCommande { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
            // Use DELETE journal mode for ALL modes (local AND network)
            // WAL mode creates -wal/-shm files using memory-mapped I/O which prevents
            // other machines from writing to the database over SMB network shares.
            // CRITICAL: Close connection after PRAGMAs to release file lock!
            // Without this, the main computer holds the lock and secondaries can't write.
            try
            {
                var connection = Database.GetDbConnection();
                connection.Open();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA busy_timeout=10000; PRAGMA synchronous=NORMAL;";
                    cmd.ExecuteNonQuery();
                }
                connection.Close(); // Release the lock! EF Core will reopen as needed.
            }
            catch
            {
                // Silently ignore if connection fails (will be caught elsewhere)
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Seed initial data for Entreprise
            modelBuilder.Entity<Entreprise>().HasData(new Entreprise { Id = 1, Nom = "Mon Magasin" });

            // Seed initial configuration
            modelBuilder.Entity<SystemConfiguration>().HasData(
                new SystemConfiguration { Cle = "version", Valeur = "1.0.0", Description = "Version de l'application" },
                new SystemConfiguration { Cle = "theme", Valeur = "default" }
            );
        }
    }
}
