using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemeCaisse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancedPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Categorie",
                table: "Promotions",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IemeArticle",
                table: "Promotions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProduitId",
                table: "Promotions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantiteOfferte",
                table: "Promotions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RemiseSurIeme",
                table: "Promotions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SeuilPanier",
                table: "Promotions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SeuilQuantite",
                table: "Promotions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PromotionTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PromotionId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantiteMin = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    PrixUnitaire = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
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

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2026, 3, 14, 12, 50, 30, 573, DateTimeKind.Local).AddTicks(7311));

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_ProduitId",
                table: "Promotions",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTiers_PromotionId",
                table: "PromotionTiers",
                column: "PromotionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Promotions_Produits_ProduitId",
                table: "Promotions",
                column: "ProduitId",
                principalTable: "Produits",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Promotions_Produits_ProduitId",
                table: "Promotions");

            migrationBuilder.DropTable(
                name: "PromotionTiers");

            migrationBuilder.DropIndex(
                name: "IX_Promotions_ProduitId",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "Categorie",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "IemeArticle",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "ProduitId",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "QuantiteOfferte",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "RemiseSurIeme",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "SeuilPanier",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "SeuilQuantite",
                table: "Promotions");

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2026, 2, 7, 17, 38, 7, 314, DateTimeKind.Local).AddTicks(2922));
        }
    }
}
