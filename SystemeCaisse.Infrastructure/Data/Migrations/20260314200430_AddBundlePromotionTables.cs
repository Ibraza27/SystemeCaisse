using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemeCaisse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBundlePromotionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromotionBundleItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PromotionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProduitId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantiteRequise = table.Column<decimal>(type: "TEXT", nullable: false)
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

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2026, 3, 14, 21, 4, 28, 575, DateTimeKind.Local).AddTicks(7250));

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBundleItems_ProduitId",
                table: "PromotionBundleItems",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBundleItems_PromotionId",
                table: "PromotionBundleItems",
                column: "PromotionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromotionBundleItems");

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2026, 3, 14, 12, 50, 30, 573, DateTimeKind.Local).AddTicks(7311));
        }
    }
}
