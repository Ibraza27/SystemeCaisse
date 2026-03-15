using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemeCaisse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Inventaires",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DateCreation = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateValidation = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Statut = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inventaires", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LignesInventaire",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InventaireId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProduitId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantiteSysteme = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    QuantiteReelle = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ValeurEcart = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
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

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2025, 12, 22, 21, 17, 12, 340, DateTimeKind.Local).AddTicks(5739));

            migrationBuilder.CreateIndex(
                name: "IX_LignesInventaire_InventaireId",
                table: "LignesInventaire",
                column: "InventaireId");

            migrationBuilder.CreateIndex(
                name: "IX_LignesInventaire_ProduitId",
                table: "LignesInventaire",
                column: "ProduitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LignesInventaire");

            migrationBuilder.DropTable(
                name: "Inventaires");

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2025, 12, 22, 20, 36, 21, 672, DateTimeKind.Local).AddTicks(1941));
        }
    }
}
