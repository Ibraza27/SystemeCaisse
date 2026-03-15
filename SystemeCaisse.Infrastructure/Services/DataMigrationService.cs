using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Core.Interfaces;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.Infrastructure.Services
{
    public class DataMigrationService : IDataMigrationService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public DataMigrationService(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task MigrateDataAsync(string pythonDbPath)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            using var connection = new SqliteConnection($"Data Source={pythonDbPath}");
            await connection.OpenAsync();

            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                await MigrateEntreprise(connection, context);
                await MigrateProduits(connection, context);
                await MigrateVentes(connection, context);
                // Future: Migrate Promotions, Stocks if needed

                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Migration failed: {ex.Message}", ex);
            }
        }

        private async Task MigrateEntreprise(SqliteConnection connection, AppDbContext context)
        {
            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM entreprise LIMIT 1";
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var existing = await context.Entreprise.FirstOrDefaultAsync();
                if (existing == null)
                {
                    existing = new Entreprise();
                    context.Entreprise.Add(existing);
                }

                existing.Nom = reader["nom"].ToString()!;
                existing.Adresse = reader["adresse"].ToString();
                existing.Telephone = reader["telephone"].ToString();
                existing.HorairesSemaine = reader["horaires_semaine"].ToString();
                existing.HorairesDimanche = reader["horaires_dimanche"].ToString();
                existing.LogoPath = reader["logo_path"].ToString();
            }
        }

        private async Task MigrateProduits(SqliteConnection connection, AppDbContext context)
        {
            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM produits";
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string codeBarre = reader["code_barre"].ToString();
                
                // Check if exists
                var existing = await context.Produits.FirstOrDefaultAsync(p => p.CodeBarre == codeBarre);
                if (existing == null)
                {
                    var produit = new Produit
                    {
                        Nom = reader["nom"].ToString()!,
                        CodeBarre = codeBarre,
                        PrixVente = Convert.ToDecimal(reader["prix_vente"]),
                        PrixAchat = Convert.ToDecimal(reader["prix_achat"]),
                        TypeVente = reader["type_vente"].ToString()!,
                        Categorie = reader["categorie"].ToString(),
                        StockActuel = Convert.ToDecimal(reader["stock_actuel"]),
                        StockAlerte = Convert.ToDecimal(reader["stock_alerte"]),
                        Actif = Convert.ToInt32(reader["actif"]) == 1
                    };
                    context.Produits.Add(produit);
                }
            }
            await context.SaveChangesAsync(); 
        }

        private async Task MigrateVentes(SqliteConnection connection, AppDbContext context)
        {
            // 1. Build Product Map (Python ID -> C# Produit)
            // Use a safer way to build the dictionary to handle duplicate/empty barcodes
            var allProducts = await context.Produits.ToListAsync();
            var productCodeMap = new Dictionary<string, Produit>();
            foreach (var p in allProducts)
            {
                var key = p.CodeBarre ?? "";
                if (!productCodeMap.ContainsKey(key))
                {
                    productCodeMap[key] = p;
                }
            }
            var pythonIdToCodeMap = new Dictionary<int, string>();

            var prodCmd = connection.CreateCommand();
            prodCmd.CommandText = "SELECT id, code_barre FROM produits";
            using (var prodReader = await prodCmd.ExecuteReaderAsync())
            {
                while (await prodReader.ReadAsync())
                {
                    var code = prodReader["code_barre"].ToString() ?? "";
                    pythonIdToCodeMap[Convert.ToInt32(prodReader["id"])] = code;
                }
            }

            // 2. Read Ventes
            var tempVentes = new List<(int OldId, Vente Entity)>();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM ventes";
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    int oldId = Convert.ToInt32(reader["id"]);
                    string ticket = reader["numero_ticket"].ToString()!;

                    // Skip if already exists
                    if (await context.Ventes.AnyAsync(v => v.NumeroTicket == ticket)) continue;

                    var vente = new Vente
                    {
                        NumeroTicket = ticket,
                        Total = Convert.ToDecimal(reader["total"]),
                        TotalRemise = Convert.ToDecimal(reader["total_remise"]),
                        MoyenPaiement = reader["mode_paiement"].ToString() ?? "especes",
                        MontantEspeces = Convert.ToDecimal(reader["montant_especes"]),
                        MontantCB = Convert.ToDecimal(reader["montant_cb"]),
                        MonnaieRendue = Convert.ToDecimal(reader["monnaie_rendue"]),
                        NbArticles = Convert.ToInt32(reader["nb_articles"]),
                        Statut = reader["statut"].ToString() ?? "validee",
                        CreatedAt = DateTime.Parse(reader["created_at"].ToString()!)
                    };
                    tempVentes.Add((oldId, vente));
                }
            }

            // 3. Read Lignes
            var tempLignes = new List<(int OldVenteId, int OldProduitId, LigneVente Entity)>();
            var ligneCmd = connection.CreateCommand();
            ligneCmd.CommandText = "SELECT * FROM lignes_vente";
            using (var reader = await ligneCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    int oldVenteId = Convert.ToInt32(reader["vente_id"]);
                    int oldProduitId = Convert.ToInt32(reader["produit_id"]);

                    var ligne = new LigneVente
                    {
                        Quantite = Convert.ToDecimal(reader["quantite"]),
                        PrixUnitaire = Convert.ToDecimal(reader["prix_unitaire"]),
                        Remise = Convert.ToDecimal(reader["remise"]),
                        TotalLigne = Convert.ToDecimal(reader["total_ligne"]),
                        PromotionAppliquee = reader["promotion_appliquee"].ToString(),
                        ProduitNom = "" // Will be filled
                    };
                    tempLignes.Add((oldVenteId, oldProduitId, ligne));
                }
            }

            // 4. Process and Link
            var venteMap = new Dictionary<int, Vente>();
            foreach (var (oldId, vente) in tempVentes)
            {
                context.Ventes.Add(vente);
                venteMap[oldId] = vente;
            }

            foreach (var (oldVenteId, oldProduitId, ligne) in tempLignes)
            {
                if (venteMap.TryGetValue(oldVenteId, out var vente))
                {
                    ligne.Vente = vente;

                    if (pythonIdToCodeMap.TryGetValue(oldProduitId, out var code) && 
                        productCodeMap.TryGetValue(code, out var produit))
                    {
                        ligne.Produit = produit;
                        ligne.ProduitNom = produit.Nom;
                    }
                    else
                    {
                        ligne.ProduitNom = "Produit Inconnu";
                    }

                    context.LignesVente.Add(ligne);
                }
            }
        }
    }
}
