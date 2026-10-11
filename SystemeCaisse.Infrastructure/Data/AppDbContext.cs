using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;

namespace SystemeCaisse.Infrastructure.Data
{
    /// <summary>
    /// Contexte EF Core principal. Utilisé tel quel pour SQLite (mode Local),
    /// et via <see cref="PostgresAppDbContext"/> pour PostgreSQL (mode Serveur).
    ///
    /// RÈGLES (voir PROJECT_GUIDELINES §18) :
    /// - Le constructeur ne doit JAMAIS ouvrir de connexion ni exécuter de SQL :
    ///   un contexte est créé pour CHAQUE opération (durée de vie courte).
    /// - Les réglages de connexion (PRAGMA SQLite, timeouts) sont gérés par
    ///   <see cref="CaisseDbContextFactory"/> et <see cref="SqliteConnectionInterceptor"/>.
    /// - Les évolutions de schéma passent par des migrations EF pour LES DEUX moteurs.
    /// </summary>
    public class AppDbContext : DbContext
    {
        public DbSet<Produit> Produits { get; set; } = null!;
        public DbSet<Entreprise> Entreprise { get; set; } = null!;
        public DbSet<SystemConfiguration> Configuration { get; set; } = null!;
        public DbSet<Vente> Ventes { get; set; } = null!;
        public DbSet<LigneVente> LignesVente { get; set; } = null!;
        public DbSet<MouvementStock> MouvementsStock { get; set; } = null!;
        public DbSet<Promotion> Promotions { get; set; } = null!;
        public DbSet<PromotionTier> PromotionTiers { get; set; } = null!;
        public DbSet<PromotionBundleItem> PromotionBundleItems { get; set; } = null!;
        public DbSet<Inventaire> Inventaires { get; set; } = null!;
        public DbSet<LigneInventaire> LignesInventaire { get; set; } = null!;
        public DbSet<Fournisseur> Fournisseurs { get; set; } = null!;
        public DbSet<CommandeFournisseur> CommandesFournisseurs { get; set; } = null!;
        public DbSet<HistoriqueAction> HistoriqueActions { get; set; } = null!;
        public DbSet<PlanningPromotion> PlanningPromotions { get; set; } = null!;
        public DbSet<Commande> Commandes { get; set; } = null!;
        public DbSet<LigneCommande> LignesCommande { get; set; } = null!;
        public DbSet<DataVersion> DataVersions { get; set; } = null!;
        public DbSet<ImageStockee> ImagesStockees { get; set; } = null!;

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        /// <summary>Constructeur pour les contextes dérivés (PostgreSQL).</summary>
        protected AppDbContext(DbContextOptions options) : base(options)
        {
        }

        /// <summary>True si ce contexte cible PostgreSQL.</summary>
        public bool IsPostgres => Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // NOTE : plus de HasData ici. Le seed (Entreprise, Configuration) est réalisé
            // par DatabaseBootstrapper uniquement si la base est vide. HasData avec DateTime.Now
            // générait une migration parasite à chaque `migrations add`.

            modelBuilder.Entity<Vente>(e =>
            {
                e.HasIndex(v => v.CreatedAt);
                e.HasIndex(v => v.ClientUid).IsUnique();
            });

            modelBuilder.Entity<Commande>(e =>
            {
                e.HasIndex(c => c.CreatedAt);
                e.HasIndex(c => c.Statut);
                e.HasIndex(c => c.NumeroCommande).IsUnique();
                e.HasIndex(c => c.SyncId).IsUnique();
            });

            modelBuilder.Entity<Produit>(e =>
            {
                e.HasIndex(p => p.CodeBarre);
                e.HasIndex(p => p.Nom);
            });

            modelBuilder.Entity<MouvementStock>(e =>
            {
                e.HasIndex(m => m.DateMouvement);
            });

            modelBuilder.Entity<DataVersion>().ToTable("DataVersions");
            modelBuilder.Entity<ImageStockee>().ToTable("ImagesStockees");
        }
    }
}
