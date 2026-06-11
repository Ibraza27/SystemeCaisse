using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemeCaisse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Commandes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NumeroCommande = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Prenom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Telephone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Adresse = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Ville = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CodePostal = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalRemise = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MontantPaye = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MontantLivraison = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AvecLivraison = table.Column<bool>(type: "INTEGER", nullable: false),
                    NbArticles = table.Column<int>(type: "INTEGER", nullable: false),
                    Statut = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commandes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LignesCommande",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CommandeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProduitId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProduitNom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CategorieNom = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PrixUnitaire = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Quantite = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    TotalLigne = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Remise = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PromotionAppliquee = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TaxTier = table.Column<int>(type: "INTEGER", nullable: false)
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

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2026, 6, 11, 19, 44, 46, 507, DateTimeKind.Local).AddTicks(282));

            migrationBuilder.CreateIndex(
                name: "IX_LignesCommande_CommandeId",
                table: "LignesCommande",
                column: "CommandeId");

            migrationBuilder.CreateIndex(
                name: "IX_LignesCommande_ProduitId",
                table: "LignesCommande",
                column: "ProduitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LignesCommande");

            migrationBuilder.DropTable(
                name: "Commandes");

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2026, 3, 29, 21, 35, 46, 906, DateTimeKind.Local).AddTicks(4518));
        }
    }
}
