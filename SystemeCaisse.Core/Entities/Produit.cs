using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class Produit
    {
        [Key]
        public int Id { get; set; }

        private string _nom = string.Empty;
        [Required]
        [MaxLength(100)]
        public string Nom 
        { 
            get => _nom; 
            set => _nom = value?.ToUpper() ?? string.Empty; 
        }

        // 1: 5.5%, 2: 10%, 3: 20%
        public int TaxTier { get; set; } = 1;

        [MaxLength(20)]
        public string? CodeBarre { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrixVente { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrixAchat { get; set; }

        [MaxLength(20)]
        public string TypeVente { get; set; } = "unite"; // "unite" ou "poids"

        [MaxLength(50)]
        public string? Categorie { get; set; }

        public int? FournisseurId { get; set; }
        [ForeignKey("FournisseurId")]
        public Fournisseur? Fournisseur { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal StockActuel { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal StockAlerte { get; set; } = 5;

        [Column(TypeName = "decimal(18,2)")]
        public decimal TauxTVA { get; set; } = 20.0m;

        public bool Actif { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Image path relative to assets
        [MaxLength(255)]
        public string? ImagePath { get; set; }

        [NotMapped]
        public decimal ValidatedSalesCount { get; set; }

        [NotMapped]
        public decimal ValeurStock => StockActuel * PrixVente;

        [NotMapped]
        public bool IsAlert => StockActuel <= StockAlerte;

        [NotMapped]
        public DateTime? LastEntryDate { get; set; } // Needs to be populated from Mouvements if needed, or left null
    }
}
