using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class PromotionTier
    {
        [Key]
        public int Id { get; set; }

        public int PromotionId { get; set; }
        [ForeignKey("PromotionId")]
        public Promotion? Promotion { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal QuantiteMin { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrixUnitaire { get; set; }
    }
}
