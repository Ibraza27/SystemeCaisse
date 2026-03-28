using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class LigneVente
    {
        [Key]
        public int Id { get; set; }

        public int VenteId { get; set; }
        [ForeignKey("VenteId")]
        public Vente? Vente { get; set; }

        public int? ProduitId { get; set; }
        [ForeignKey("ProduitId")]
        public Produit? Produit { get; set; }

        [MaxLength(100)]
        public string ProduitNom { get; set; } = string.Empty; // Snapshot du nom

        [MaxLength(50)]
        public string CategorieNom { get; set; } = "Autre"; // Snapshot de la catégorie

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrixUnitaire { get; set; } // Snapshot du prix

        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantite { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalLigne { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Remise { get; set; }

        [MaxLength(100)]
        public string? PromotionAppliquee { get; set; }

        public int TaxTier { get; set; } = 1;
    }
}
