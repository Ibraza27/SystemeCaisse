using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SystemeCaisse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Configuration",
                columns: table => new
                {
                    Cle = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Valeur = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuration", x => x.Cle);
                });

            migrationBuilder.CreateTable(
                name: "Entreprise",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Adresse = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Telephone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Siret = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    HorairesSemaine = table.Column<string>(type: "TEXT", nullable: true),
                    HorairesDimanche = table.Column<string>(type: "TEXT", nullable: true),
                    LogoPath = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entreprise", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Produits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CodeBarre = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    PrixVente = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PrixAchat = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TypeVente = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Categorie = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    StockActuel = table.Column<int>(type: "INTEGER", nullable: false),
                    StockAlerte = table.Column<int>(type: "INTEGER", nullable: false),
                    Actif = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Produits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ventes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NumeroTicket = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NbArticles = table.Column<int>(type: "INTEGER", nullable: false),
                    MoyenPaiement = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Statut = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ventes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LignesVente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VenteId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProduitId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProduitNom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PrixUnitaire = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Quantite = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    TotalLigne = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
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

            migrationBuilder.InsertData(
                table: "Configuration",
                columns: new[] { "Cle", "Description", "Type", "Valeur" },
                values: new object[,]
                {
                    { "theme", null, "text", "default" },
                    { "version", "Version de l'application", "text", "1.0.0" }
                });

            migrationBuilder.InsertData(
                table: "Entreprise",
                columns: new[] { "Id", "Adresse", "HorairesDimanche", "HorairesSemaine", "LogoPath", "Nom", "Siret", "Telephone", "UpdatedAt" },
                values: new object[] { 1, null, null, null, null, "Mon Magasin", null, null, new DateTime(2025, 12, 21, 16, 28, 6, 968, DateTimeKind.Local).AddTicks(5194) });

            migrationBuilder.CreateIndex(
                name: "IX_LignesVente_ProduitId",
                table: "LignesVente",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_LignesVente_VenteId",
                table: "LignesVente",
                column: "VenteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Configuration");

            migrationBuilder.DropTable(
                name: "Entreprise");

            migrationBuilder.DropTable(
                name: "LignesVente");

            migrationBuilder.DropTable(
                name: "Produits");

            migrationBuilder.DropTable(
                name: "Ventes");
        }
    }
}
