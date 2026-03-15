using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class MouvementStock
    {
        [Key]
        public int Id { get; set; }

        public int ProduitId { get; set; }
        [ForeignKey("ProduitId")]
        public Produit Produit { get; set; }

        [Required]
        [MaxLength(20)]
        public string TypeMouvement { get; set; } // "entree", "sortie", "inventaire"

        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantite { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PrixUnitaire { get; set; } // Pour les entrées (prix achat)

        [MaxLength(255)]
        public string? Commentaire { get; set; }

        public DateTime DateMouvement { get; set; } = DateTime.Now;

        // Snapshot info
        [MaxLength(100)]
        public string ProduitNomSnapshot { get; set; }
    }
}
