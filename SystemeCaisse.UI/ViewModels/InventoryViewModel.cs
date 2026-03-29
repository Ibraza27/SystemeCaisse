using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Data;

namespace SystemeCaisse.UI.ViewModels
{
    public partial class InventoryViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        [ObservableProperty]
        private ObservableCollection<Inventaire> _history;

        [ObservableProperty]
        private Inventaire? _activeInventory;

        [ObservableProperty]
        private bool _isInventoryActive;

        public InventoryViewModel(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            History = new ObservableCollection<Inventaire>();
        }

        public async Task InitializeAsync()
        {
            await LoadHistoryInternalAsync();
        }

        [RelayCommand]
        public async Task LoadHistoryAsync() => await LoadHistoryInternalAsync();

        private async Task LoadHistoryInternalAsync()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var list = await context.Inventaires
                    .Include(i => i.Lignes)
                    .OrderByDescending(i => i.DateCreation)
                    .ToListAsync();
                
                await Application.Current.Dispatcher.InvokeAsync(() => 
                {
                    History.Clear();
                    foreach (var i in list) History.Add(i);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur chargement historique : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public async Task StartNewInventoryAsync()
        {
            if (IsInventoryActive) return;

            try
            {
                using var context = _contextFactory.CreateDbContext();
                var produits = await context.Produits.Where(p => p.Actif).ToListAsync();

                var inventaire = new Inventaire
                {
                    DateCreation = DateTime.Now,
                    Statut = "En cours",
                    Notes = $"Inventaire du {DateTime.Now:dd/MM/yyyy}"
                };

                foreach (var p in produits)
                {
                    inventaire.Lignes.Add(new LigneInventaire
                    {
                        Produit = p,
                        ProduitId = p.Id,
                        QuantiteSysteme = p.StockActuel,
                        QuantiteReelle = p.StockActuel // Default to system stock
                    });
                }

                // Don't save yet, just in memory for editing
                ActiveInventory = inventaire;
                IsInventoryActive = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur création inventaire : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CancelInventory()
        {
            if (MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Annuler l'inventaire en cours ? Tout travail non sauvegardé sera perdu.", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ActiveInventory = null;
                IsInventoryActive = false;
            }
        }

        [RelayCommand]
        public async Task ValidateInventoryAsync()
        {
            if (ActiveInventory == null) return;

            if (MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Valider l'inventaire ?\nCela mettra à jour les stocks de tous les produits.", "Attention - Irréversible", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                using var context = _contextFactory.CreateDbContext();
                
                // 1. Save Inventory Record
                ActiveInventory.Statut = "Validé";
                ActiveInventory.DateValidation = DateTime.Now;
                
                // Attach without duplicates if possible, or just add new
                // Context tracking can be tricky with detached entities.
                // Safest to re-fetch products or attach properly.
                
                // Let's add the inventory object graph. EF Core should handle new Lignes.
                // We need to ensure Products are attached as Unchanged to avoid duplicating them.
                foreach (var ligne in ActiveInventory.Lignes)
                {
                    context.Attach(ligne.Produit); // Attach product as existing
                    
                    // 2. Update Stock & Create Movement
                    decimal ecart = ligne.QuantiteReelle - ligne.QuantiteSysteme;
                    
                    if (ecart != 0)
                    {
                        ligne.Produit.StockActuel = ligne.QuantiteReelle;
                        ligne.ValeurEcart = ecart * ligne.Produit.PrixAchat;

                        var mvt = new MouvementStock
                        {
                            ProduitId = ligne.ProduitId,
                            DateMouvement = DateTime.Now,
                            TypeMouvement = "inventaire",
                            Quantite = ecart,
                            Commentaire = $"Régularisation Inventaire #{ActiveInventory.DateCreation:dd/MM/yy}"
                        };
                        context.MouvementsStock.Add(mvt);
                    }
                }

                context.Inventaires.Add(ActiveInventory);
                await context.SaveChangesAsync();

                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), "Inventaire validé et stocks mis à jour !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                IsInventoryActive = false;
                ActiveInventory = null;
                await LoadHistoryAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Services.WindowHelper.GetAdminWindow(), $"Erreur validation : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
