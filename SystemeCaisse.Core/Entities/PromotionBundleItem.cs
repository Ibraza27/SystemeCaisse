using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class PromotionBundleItem
    {
        [Key]
        public int Id { get; set; }

        public int PromotionId { get; set; }
        [ForeignKey("PromotionId")]
        public Promotion Promotion { get; set; } = null!;

        public int ProduitId { get; set; }
        [ForeignKey("ProduitId")]
        public Produit Produit { get; set; } = null!;

        public decimal QuantiteRequise { get; set; } = 1;
    }
}
