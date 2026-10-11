using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SystemeCaisse.Infrastructure.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MultiPosteRefonte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE (édition manuelle) :
            // - Les DeleteData du seed (Entreprise/Configuration) générés par la suppression de HasData
            //   ont été retirés : les données existantes doivent être conservées.
            // - Les colonnes Commandes.SyncId/Source/Email existent déjà : elles sont créées par
            //   AddCommandes (bases neuves) ou ajoutées par DatabaseBootstrapper (anciennes bases).

            migrationBuilder.AddColumn<string>(
                name: "ClientUid",
                table: "Ventes",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Poste",
                table: "Ventes",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Poste",
                table: "Commandes",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            // Dédoublonnage AVANT la création des index uniques (bugs historiques de synchro multi-poste).
            migrationBuilder.Sql("UPDATE Commandes SET SyncId = NULL WHERE SyncId = '';");
            migrationBuilder.Sql(
                "DELETE FROM LignesCommande WHERE CommandeId IN (" +
                "SELECT Id FROM Commandes WHERE SyncId IS NOT NULL AND Id NOT IN " +
                "(SELECT MIN(Id) FROM Commandes WHERE SyncId IS NOT NULL GROUP BY SyncId));");
            migrationBuilder.Sql(
                "DELETE FROM Commandes WHERE SyncId IS NOT NULL AND Id NOT IN " +
                "(SELECT MIN(Id) FROM Commandes WHERE SyncId IS NOT NULL GROUP BY SyncId);");
            migrationBuilder.Sql(
                "UPDATE Commandes SET NumeroCommande = NumeroCommande || '-' || Id " +
                "WHERE Id NOT IN (SELECT MIN(Id) FROM Commandes GROUP BY NumeroCommande);");

            migrationBuilder.CreateTable(
                name: "DataVersions",
                columns: table => new
                {
                    Domaine = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataVersions", x => x.Domaine);
                });

            migrationBuilder.CreateTable(
                name: "ImagesStockees",
                columns: table => new
                {
                    Cle = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Donnees = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagesStockees", x => x.Cle);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ventes_ClientUid",
                table: "Ventes",
                column: "ClientUid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ventes_CreatedAt",
                table: "Ventes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Produits_CodeBarre",
                table: "Produits",
                column: "CodeBarre");

            migrationBuilder.CreateIndex(
                name: "IX_Produits_Nom",
                table: "Produits",
                column: "Nom");

            migrationBuilder.CreateIndex(
                name: "IX_MouvementsStock_DateMouvement",
                table: "MouvementsStock",
                column: "DateMouvement");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataVersions");

            migrationBuilder.DropTable(
                name: "ImagesStockees");

            migrationBuilder.DropIndex(
                name: "IX_Ventes_ClientUid",
                table: "Ventes");

            migrationBuilder.DropIndex(
                name: "IX_Ventes_CreatedAt",
                table: "Ventes");

            migrationBuilder.DropIndex(
                name: "IX_Produits_CodeBarre",
                table: "Produits");

            migrationBuilder.DropIndex(
                name: "IX_Produits_Nom",
                table: "Produits");

            migrationBuilder.DropIndex(
                name: "IX_MouvementsStock_DateMouvement",
                table: "MouvementsStock");

            migrationBuilder.DropIndex(
                name: "IX_Commandes_CreatedAt",
                table: "Commandes");

            migrationBuilder.DropIndex(
                name: "IX_Commandes_NumeroCommande",
                table: "Commandes");

            migrationBuilder.DropIndex(
                name: "IX_Commandes_Statut",
                table: "Commandes");

            migrationBuilder.DropIndex(
                name: "IX_Commandes_SyncId",
                table: "Commandes");

            migrationBuilder.DropColumn(
                name: "ClientUid",
                table: "Ventes");

            migrationBuilder.DropColumn(
                name: "Poste",
                table: "Ventes");

            migrationBuilder.DropColumn(
                name: "Poste",
                table: "Commandes");
        }
    }
}
