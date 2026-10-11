using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemeCaisse.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxTierToProduit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaxTier",
                table: "Produits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2026, 3, 25, 16, 17, 33, 692, DateTimeKind.Local).AddTicks(903));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxTier",
                table: "Produits");

            migrationBuilder.UpdateData(
                table: "Entreprise",
                keyColumn: "Id",
                keyValue: 1,
                column: "UpdatedAt",
                value: new DateTime(2026, 3, 14, 21, 4, 28, 575, DateTimeKind.Local).AddTicks(7250));
        }
    }
}
