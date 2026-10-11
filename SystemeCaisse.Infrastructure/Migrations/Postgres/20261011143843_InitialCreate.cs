using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SystemeCaisse.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Commandes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NumeroCommande = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Nom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Prenom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Telephone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SyncId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Adresse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Ville = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CodePostal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalRemise = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MontantPaye = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MontantLivraison = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AvecLivraison = table.Column<bool>(type: "boolean", nullable: false),
                    NbArticles = table.Column<int>(type: "integer", nullable: false),
                    Statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ModePaiement = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Poste = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commandes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Configuration",
                columns: table => new
                {
                    Cle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Valeur = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuration", x => x.Cle);
                });

            migrationBuilder.CreateTable(
                name: "DataVersions",
                columns: table => new
                {
                    Domaine = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataVersions", x => x.Domaine);
                });

            migrationBuilder.CreateTable(
                name: "Entreprise",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Adresse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Telephone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Siret = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    HorairesSemaine = table.Column<string>(type: "text", nullable: true),
                    HorairesDimanche = table.Column<string>(type: "text", nullable: true),
                    LogoPath = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entreprise", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fournisseurs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Contact = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Telephone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Adresse = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fournisseurs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HistoriqueActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TypeAction = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Utilisateur = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DonneesJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoriqueActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImagesStockees",
                columns: table => new
                {
                    Cle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Donnees = table.Column<byte[]>(type: "bytea", nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagesStockees", x => x.Cle);
                });

            migrationBuilder.CreateTable(
                name: "Inventaires",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DateCreation = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateValidation = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inventaires", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ventes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NumeroTicket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ClientUid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    Poste = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    NbArticles = table.Column<int>(type: "integer", nullable: false),
                    MoyenPaiement = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalRemise = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MontantEspeces = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MontantCB = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MonnaieRendue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ventes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommandesFournisseurs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FournisseurId = table.Column<int>(type: "integer", nullable: false),
                    NumeroCommande = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DateCommande = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateLivraisonPrevue = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandesFournisseurs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommandesFournisseurs_Fournisseurs_FournisseurId",
                        column: x => x.FournisseurId,
                        principalTable: "Fournisseurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Produits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TaxTier = table.Column<int>(type: "integer", nullable: false),
                    CodeBarre = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PrixVente = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PrixAchat = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TypeVente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Categorie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FournisseurId = table.Column<int>(type: "integer", nullable: true),
                    StockActuel = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    StockAlerte = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    TauxTVA = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ImagePath = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Produits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Produits_Fournisseurs_FournisseurId",
                        column: x => x.FournisseurId,
                        principalTable: "Fournisseurs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LignesCommande",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommandeId = table.Column<int>(type: "integer", nullable: false),
                    ProduitId = table.Column<int>(type: "integer", nullable: true),
                    ProduitNom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CategorieNom = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PrixUnitaire = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Quantite = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    TotalLigne = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Remise = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PromotionAppliquee = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TaxTier = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LignesCommande", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LignesCommande_Commandes_CommandeId",
                        column: x => x.CommandeId,
                        principalTable: "Commandes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LignesCommande_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LignesInventaire",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InventaireId = table.Column<int>(type: "integer", nullable: false),
                    ProduitId = table.Column<int>(type: "integer", nullable: false),
                    QuantiteSysteme = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    QuantiteReelle = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    ValeurEcart = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LignesInventaire", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LignesInventaire_Inventaires_InventaireId",
                        column: x => x.InventaireId,
                        principalTable: "Inventaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LignesInventaire_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LignesVente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VenteId = table.Column<int>(type: "integer", nullable: false),
                    ProduitId = table.Column<int>(type: "integer", nullable: true),
                    ProduitNom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CategorieNom = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PrixUnitaire = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Quantite = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    TotalLigne = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Remise = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PromotionAppliquee = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TaxTier = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LignesVente", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LignesVente_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LignesVente_Ventes_VenteId",
                        column: x => x.VenteId,
                        principalTable: "Ventes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MouvementsStock",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProduitId = table.Column<int>(type: "integer", nullable: false),
                    TypeMouvement = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantite = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    PrixUnitaire = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Commentaire = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DateMouvement = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ProduitNomSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MouvementsStock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MouvementsStock_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nom = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DateDebut = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DateFin = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TypePromotion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProduitId = table.Column<int>(type: "integer", nullable: true),
                    Categorie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Valeur = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsPourcentage = table.Column<bool>(type: "boolean", nullable: false),
                    SeuilQuantite = table.Column<decimal>(type: "numeric", nullable: true),
                    QuantiteOfferte = table.Column<decimal>(type: "numeric", nullable: true),
                    IemeArticle = table.Column<int>(type: "integer", nullable: true),
                    RemiseSurIeme = table.Column<decimal>(type: "numeric", nullable: true),
                    SeuilPanier = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Actif = table.Column<bool>(type: "boolean", nullable: false),
                    Cumulable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promotions_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlanningPromotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PromotionId = table.Column<int>(type: "integer", nullable: false),
                    DateDebut = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DateFin = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    NbVentes = table.Column<int>(type: "integer", nullable: false),
                    Commentaires = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningPromotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningPromotions_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromotionBundleItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PromotionId = table.Column<int>(type: "integer", nullable: false),
                    ProduitId = table.Column<int>(type: "integer", nullable: false),
                    QuantiteRequise = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionBundleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionBundleItems_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromotionBundleItems_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromotionTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PromotionId = table.Column<int>(type: "integer", nullable: false),
                    QuantiteMin = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    PrixUnitaire = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionTiers_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Commandes_CreatedAt",
                table: "Commandes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Commandes_NumeroCommande",
                table: "Commandes",
                column: "NumeroCommande",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commandes_Statut",
                table: "Commandes",
                column: "Statut");

            migrationBuilder.CreateIndex(
                name: "IX_Commandes_SyncId",
                table: "Commandes",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandesFournisseurs_FournisseurId",
                table: "CommandesFournisseurs",
                column: "FournisseurId");

            migrationBuilder.CreateIndex(
                name: "IX_LignesCommande_CommandeId",
                table: "LignesCommande",
                column: "CommandeId");

            migrationBuilder.CreateIndex(
                name: "IX_LignesCommande_ProduitId",
                table: "LignesCommande",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_LignesInventaire_InventaireId",
                table: "LignesInventaire",
                column: "InventaireId");

            migrationBuilder.CreateIndex(
                name: "IX_LignesInventaire_ProduitId",
                table: "LignesInventaire",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_LignesVente_ProduitId",
                table: "LignesVente",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_LignesVente_VenteId",
                table: "LignesVente",
                column: "VenteId");

            migrationBuilder.CreateIndex(
                name: "IX_MouvementsStock_DateMouvement",
                table: "MouvementsStock",
                column: "DateMouvement");

            migrationBuilder.CreateIndex(
                name: "IX_MouvementsStock_ProduitId",
                table: "MouvementsStock",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningPromotions_PromotionId",
                table: "PlanningPromotions",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_Produits_CodeBarre",
                table: "Produits",
                column: "CodeBarre");

            migrationBuilder.CreateIndex(
                name: "IX_Produits_FournisseurId",
                table: "Produits",
                column: "FournisseurId");

            migrationBuilder.CreateIndex(
                name: "IX_Produits_Nom",
                table: "Produits",
                column: "Nom");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBundleItems_ProduitId",
                table: "PromotionBundleItems",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBundleItems_PromotionId",
                table: "PromotionBundleItems",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_ProduitId",
                table: "Promotions",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTiers_PromotionId",
                table: "PromotionTiers",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_Ventes_ClientUid",
                table: "Ventes",
                column: "ClientUid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ventes_CreatedAt",
                table: "Ventes",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandesFournisseurs");

            migrationBuilder.DropTable(
                name: "Configuration");

            migrationBuilder.DropTable(
                name: "DataVersions");

            migrationBuilder.DropTable(
                name: "Entreprise");

            migrationBuilder.DropTable(
                name: "HistoriqueActions");

            migrationBuilder.DropTable(
                name: "ImagesStockees");

            migrationBuilder.DropTable(
                name: "LignesCommande");

            migrationBuilder.DropTable(
                name: "LignesInventaire");

            migrationBuilder.DropTable(
                name: "LignesVente");

            migrationBuilder.DropTable(
                name: "MouvementsStock");

            migrationBuilder.DropTable(
                name: "PlanningPromotions");

            migrationBuilder.DropTable(
                name: "PromotionBundleItems");

            migrationBuilder.DropTable(
                name: "PromotionTiers");

            migrationBuilder.DropTable(
                name: "Commandes");

            migrationBuilder.DropTable(
                name: "Inventaires");

            migrationBuilder.DropTable(
                name: "Ventes");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropTable(
                name: "Produits");

            migrationBuilder.DropTable(
                name: "Fournisseurs");
        }
    }
}
