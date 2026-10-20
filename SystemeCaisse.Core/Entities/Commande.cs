using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SystemeCaisse.Core.Entities
{
    public class Commande : INotifyPropertyChanged
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        public string NumeroCommande { get; set; } = string.Empty;

        // Client Info
        [Required]
        [MaxLength(100)]
        public string Nom { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Prenom { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Telephone { get; set; } = string.Empty;

        // ─── Sync avec TransitManager ───
        [MaxLength(36)]
        public string? SyncId { get; set; } // GUID de réconciliation inter-systèmes

        [MaxLength(10)]
        public string Source { get; set; } = "caisse"; // "caisse" ou "web"

        [MaxLength(200)]
        public string? Email { get; set; }

        [MaxLength(200)]
        public string? Adresse { get; set; }

        [MaxLength(100)]
        public string? Ville { get; set; }

        [MaxLength(10)]
        public string? CodePostal { get; set; }

        [MaxLength(500)]
        public string? Commentaire { get; set; }

        // Amounts
        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalRemise { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontantPaye { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontantLivraison { get; set; }

        public bool AvecLivraison { get; set; }

        public int NbArticles { get; set; }

        // Status: "en_attente", "validee", "traitee", "annulee", "supprimee"
        [Required]
        [MaxLength(20)]
        public string Statut { get; set; } = "en_attente";

        [MaxLength(20)]
        public string? ModePaiement { get; set; } // "espece", "virement", "wero", "cb", "en_ligne", "sur_place"

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>Nom du poste de caisse ayant créé la commande (null pour les commandes web).</summary>
        [MaxLength(50)]
        public string? Poste { get; set; }

        // Navigation
        public List<LigneCommande> Lignes { get; set; } = new();

        // ─── Multi-select checkbox (not persisted) ───
        private bool _isSelected;
        [NotMapped]
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // Computed properties (not mapped to DB)
        [NotMapped]
        public decimal TotalAvecLivraison => Total + MontantLivraison;

        [NotMapped]
        public decimal Restant => Math.Round(TotalAvecLivraison - MontantPaye, 2);

        [NotMapped]
        public string StatutPaiement
        {
            get
            {
                if (Math.Round(TotalAvecLivraison - MontantPaye, 2) <= 0) return "regle";
                if (MontantPaye > 0) return "partiel";
                return "non_regle";
            }
        }

        [NotMapped]
        public string StatutPaiementDisplay
        {
            get
            {
                return StatutPaiement switch
                {
                    "regle" => "Réglé",
                    "partiel" => "Partiellement réglé",
                    "non_regle" => "Non réglé",
                    _ => "Inconnu"
                };
            }
        }

        [NotMapped]
        public string StatutDisplay
        {
            get
            {
                return Statut switch
                {
                    "en_attente" => "En attente",
                    "validee" => "Validée",
                    "traitee" => "Traitée",
                    "annulee" => "Annulée",
                    "supprimee" => "Supprimée",
                    _ => Statut
                };
            }
        }

        [NotMapped]
        public string VilleCodePostal => 
            string.IsNullOrWhiteSpace(CodePostal) ? (Ville ?? "") : $"{CodePostal} {Ville}";

        [NotMapped]
        public string ModePaiementDisplay => ModePaiement switch
        {
            "espece" => "Espèce",
            "virement" => "Virement",
            "wero" => "Wero",
            "cb" => "CB",
            "en_ligne" => "En ligne",
            "sur_place" => "Sur place",
            _ => ModePaiement ?? "—"
        };

        [NotMapped]
        public bool IsGroupedCommande => Lignes != null && Lignes.Any(l => !string.IsNullOrWhiteSpace(l.ClientNom));

        [NotMapped]
        public List<CommandeClientSubTotal> SubClientTotals => Lignes == null
            ? new List<CommandeClientSubTotal>()
            : Lignes.Where(l => !string.IsNullOrWhiteSpace(l.ClientNom))
                    .GroupBy(l => l.ClientNom!)
                    .Select(g => new CommandeClientSubTotal
                    {
                        ClientNom = g.Key,
                        Total = g.Sum(x => x.TotalLigne),
                        NbArticles = (int)g.Sum(x => Math.Ceiling(x.Quantite)),
                        Lignes = g.ToList()
                    })
                    .ToList();

        // INotifyPropertyChanged (for IsSelected binding)
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CommandeClientSubTotal
    {
        public string ClientNom { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public int NbArticles { get; set; }
        public List<LigneCommande> Lignes { get; set; } = new();
    }
}
