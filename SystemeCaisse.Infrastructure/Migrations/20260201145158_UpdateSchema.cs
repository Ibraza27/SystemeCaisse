using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemeCaisse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MonnaieRendue",
                table: "Ventes",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MontantCB",
                table: "Ventes",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MontantEspeces",
                table: "Ventes",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalRemise",
                table: "Ventes",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "FournisseurId",
                table: "Produits",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromotionAppliquee",
                table: "LignesVente",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Remise",
                table: "LignesVente",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Fournisseurs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Contact = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Telephone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Adresse = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fournisseurs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HistoriqueActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TypeAction = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Utilisateur = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    DonneesJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoriqueActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanningPromotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PromotionId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateDebut = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DateFin = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NbVentes = table.Column<int>(type: "INTEGER", nullable: false),
                    Commentaires = table.Column<string>(type: "TEXT", nullable: true)
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
                name: "CommandesFournisseurs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FournisseurId = table.Column<int>(type: "INTEGER", nullable: false),
                    NumeroCommande = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DateCommande = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateLivraisonPrevue = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Statut = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2026, 2, 1, 15, 51, 53, 562, DateTimeKind.Local).AddTicks(6115));

            migrationBuilder.CreateIndex(
                name: "IX_Produits_FournisseurId",
                table: "Produits",
                column: "FournisseurId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandesFournisseurs_FournisseurId",
                table: "CommandesFournisseurs",
                column: "FournisseurId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningPromotions_PromotionId",
                table: "PlanningPromotions",
                column: "PromotionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Produits_Fournisseurs_FournisseurId",
                table: "Produits",
                column: "FournisseurId",
                principalTable: "Fournisseurs",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Produits_Fournisseurs_FournisseurId",
                table: "Produits");

            migrationBuilder.DropTable(
                name: "CommandesFournisseurs");

            migrationBuilder.DropTable(
                name: "HistoriqueActions");

            migrationBuilder.DropTable(
                name: "PlanningPromotions");

            migrationBuilder.DropTable(
                name: "Fournisseurs");

            migrationBuilder.DropIndex(
                name: "IX_Produits_FournisseurId",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "MonnaieRendue",
                table: "Ventes");

            migrationBuilder.DropColumn(
                name: "MontantCB",
                table: "Ventes");

            migrationBuilder.DropColumn(
                name: "MontantEspeces",
                table: "Ventes");

            migrationBuilder.DropColumn(
                name: "TotalRemise",
                table: "Ventes");

            migrationBuilder.DropColumn(
                name: "FournisseurId",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "PromotionAppliquee",
                table: "LignesVente");

            migrationBuilder.DropColumn(
                name: "Remise",
                table: "LignesVente");

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2025, 12, 22, 21, 17, 12, 340, DateTimeKind.Local).AddTicks(5739));
        }
    }
}
