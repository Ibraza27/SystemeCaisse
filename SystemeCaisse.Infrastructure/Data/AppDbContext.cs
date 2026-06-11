using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;

namespace SystemeCaisse.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
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
