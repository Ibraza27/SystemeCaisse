using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class Commande
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

        [MaxLength(200)]
        public string? Adresse { get; set; }

        [MaxLength(100)]
        public string? Ville { get; set; }

        [MaxLength(10)]
        public string? CodePostal { get; set; }

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

        // Status: "en_attente", "traitee", "annulee"
        [Required]
        [MaxLength(20)]
        public string Statut { get; set; } = "en_attente";

        [MaxLength(20)]
        public string? ModePaiement { get; set; } // "espece", "virement", "wero"

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Navigation
        public List<LigneCommande> Lignes { get; set; } = new();

        // Computed properties (not mapped to DB)
        [NotMapped]
        public decimal TotalAvecLivraison => Total + MontantLivraison;

        [NotMapped]
        public decimal Restant => TotalAvecLivraison - MontantPaye;

        [NotMapped]
        public string StatutPaiement
        {
            get
            {
                if (Restant <= 0) return "regle";
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
                    "traitee" => "Traitée",
                    "annulee" => "Annulée",
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
            _ => ModePaiement ?? "—"
        };
    }
}
