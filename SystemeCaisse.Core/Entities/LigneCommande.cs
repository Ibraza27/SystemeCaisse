using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class LigneCommande
    {
        [Key]
        public int Id { get; set; }

        public int CommandeId { get; set; }
        [ForeignKey("CommandeId")]
        public Commande? Commande { get; set; }

        public int? ProduitId { get; set; }
        [ForeignKey("ProduitId")]
        public Produit? Produit { get; set; }

        [MaxLength(100)]
        public string ProduitNom { get; set; } = string.Empty;

        [MaxLength(50)]
        public string CategorieNom { get; set; } = "Autre";

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrixUnitaire { get; set; }

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
