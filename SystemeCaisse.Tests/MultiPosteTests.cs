using Microsoft.EntityFrameworkCore;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Data;
using SystemeCaisse.Infrastructure.Services;

namespace SystemeCaisse.Tests
{
    public class MultiPosteTests
    {
        private static Vente NewSale(int productId, decimal qty, string? clientUid = null) => new()
        {
            NumeroTicket = SalesService.GenerateTicketNumber("TEST", DateTime.Now),
            CreatedAt = DateTime.Now,
            ClientUid = clientUid ?? Guid.NewGuid().ToString("D"),
            Poste = "TEST",
            Total = qty * 2.5m,
            NbArticles = (int)qty,
            MoyenPaiement = "CB",
            MontantCB = qty * 2.5m,
            Statut = "validee",
            Lignes =
            {
                new LigneVente { ProduitId = productId, ProduitNom = "PRODUIT", CategorieNom = "Test", PrixUnitaire = 2.5m, Quantite = qty, TotalLigne = qty * 2.5m }
            }
        };

        private static async Task<decimal> StockOf(IDbContextFactory<AppDbContext> f, int id)
        {
            await using var db = await f.CreateDbContextAsync();
            return await db.Produits.AsNoTracking().Where(p => p.Id == id).Select(p => p.StockActuel).FirstAsync();
        }

        [Fact]
        public async Task Ventes_concurrentes_ne_perdent_aucun_decrement_de_stock()
        {
            foreach (var f in await TestDatabases.AllAsync())
            {
                var pid = await TestDatabases.AddProductAsync(f, "CONCURRENT", 100m);
                var sales = new SalesService(f);

                // 5 « postes » qui vendent 4 fois chacun en parallèle.
                var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
                {
                    for (int i = 0; i < 4; i++) await sales.RecordSaleAsync(NewSale(pid, 1.5m));
                }));
                await Task.WhenAll(tasks);

                Assert.Equal(100m - 20 * 1.5m, await StockOf(f, pid));
                await using var db = await f.CreateDbContextAsync();
                Assert.Equal(20, await db.Ventes.CountAsync());
                Assert.Equal(20, await db.MouvementsStock.CountAsync(m => m.ProduitId == pid));
            }
        }

        [Fact]
        public async Task Renvoi_d_une_vente_est_idempotent()
        {
            foreach (var f in await TestDatabases.AllAsync())
            {
                var pid = await TestDatabases.AddProductAsync(f, "IDEMPOTENT", 10m);
                var sales = new SalesService(f);
                var uid = Guid.NewGuid().ToString("D");

                var first = await sales.RecordSaleAsync(NewSale(pid, 2m, uid));
                var second = await sales.RecordSaleAsync(NewSale(pid, 2m, uid));

                Assert.False(first.AlreadyRecorded);
                Assert.True(second.AlreadyRecorded);
                Assert.Equal(first.Vente.Id, second.Vente.Id);
                Assert.Equal(8m, await StockOf(f, pid));
            }
        }

        [Fact]
        public async Task Vente_d_un_produit_supprime_entre_temps_est_enregistree_sans_reference()
        {
            foreach (var f in await TestDatabases.AllAsync())
            {
                var sales = new SalesService(f);
                var result = await sales.RecordSaleAsync(NewSale(987654, 1m));
                Assert.Null(result.Vente.Lignes.Single().ProduitId);
            }
        }

        [Fact]
        public async Task Numeros_de_commande_uniques_en_creation_concurrente()
        {
            foreach (var f in await TestDatabases.AllAsync())
            {
                var svc = new CommandeService(f);
                var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() => svc.CreateAsync(new Commande
                {
                    Nom = "CLIENT" + i,
                    Prenom = "Test",
                    Telephone = "0600000000",
                    Total = 10,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                })));
                var created = await Task.WhenAll(tasks);
                Assert.Equal(8, created.Select(c => c.NumeroCommande).Distinct().Count());
            }
        }

        [Fact]
        public async Task Commande_traitee_decremente_le_stock_une_seule_fois()
        {
            foreach (var f in await TestDatabases.AllAsync())
            {
                var pid = await TestDatabases.AddProductAsync(f, "CMD", 50m);
                var svc = new CommandeService(f);
                var cmd = await svc.CreateAsync(new Commande
                {
                    Nom = "A", Prenom = "B", Telephone = "1", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
                    Lignes = { new LigneCommande { ProduitId = pid, ProduitNom = "CMD", Quantite = 3m, PrixUnitaire = 1m, TotalLigne = 3m } }
                });
                await svc.ChangeStatusAsync(new[] { cmd.Id }, "traitee");
                await svc.ChangeStatusAsync(new[] { cmd.Id }, "traitee");
                Assert.Equal(47m, await StockOf(f, pid));

                await svc.AddPaymentAsync(cmd.Id, 4.25m, "cb");
                var after = await svc.AddPaymentAsync(cmd.Id, 1.10m, null);
                Assert.Equal(5.35m, after!.MontantPaye);
            }
        }

        [Fact]
        public async Task Modification_produit_n_ecrase_pas_les_ventes_des_autres_postes()
        {
            foreach (var f in await TestDatabases.AllAsync())
            {
                var pid = await TestDatabases.AddProductAsync(f, "EDIT", 20m);
                var catalog = new CatalogService(f);

                // Poste A charge le produit…
                var loaded = (await catalog.GetProductsAsync()).Single(p => p.Id == pid);
                var snapshot = ProductSnapshot.Of(loaded);

                // …pendant que le poste B vend 5 unités.
                await new SalesService(f).RecordSaleAsync(NewSale(pid, 5m));

                // Poste A change le prix et ajoute 2 au stock depuis la fiche produit.
                loaded.PrixVente = 9.99m;
                loaded.StockActuel += 2m;
                await catalog.SaveProductsAsync(new[] { new ProductChange(loaded, snapshot) });

                await using var db = await f.CreateDbContextAsync();
                var fresh = await db.Produits.AsNoTracking().FirstAsync(p => p.Id == pid);
                Assert.Equal(9.99m, fresh.PrixVente);
                Assert.Equal(17m, fresh.StockActuel); // 20 - 5 + 2
            }
        }

        [Fact]
        public async Task Popularite_calculee_par_la_base()
        {
            foreach (var f in await TestDatabases.AllAsync())
            {
                var a = await TestDatabases.AddProductAsync(f, "POP_A", 100m);
                var b = await TestDatabases.AddProductAsync(f, "POP_B", 100m);
                var sales = new SalesService(f);
                await sales.RecordSaleAsync(NewSale(a, 1.5m));
                await sales.RecordSaleAsync(NewSale(a, 2m));
                await sales.RecordSaleAsync(NewSale(b, 1m));

                var snap = await new PopularityService(f).GetAsync(forceRefresh: true);
                Assert.Equal(3.5m, snap.Get(a, null));
                Assert.Equal(1m, snap.Get(b, null));
            }
        }

        [Fact]
        public async Task Versions_de_donnees_incrementees_et_detectees()
        {
            foreach (var f in await TestDatabases.AllAsync())
            {
                using var monitor = new DataVersionMonitor(f, TimeSpan.FromHours(1));
                await monitor.CheckNowAsync(); // état initial
                var pid = await TestDatabases.AddProductAsync(f, "VERSION", 5m);
                await new SalesService(f).RecordSaleAsync(NewSale(pid, 1m));
                var changed = await monitor.CheckNowAsync();
                Assert.Contains(DataDomains.Ventes, changed);
                Assert.Contains(DataDomains.Stocks, changed);
            }
        }
    }
}
