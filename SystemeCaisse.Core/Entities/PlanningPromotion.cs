using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class PlanningPromotion
    {
        [Key]
        public int Id { get; set; }

        public int PromotionId { get; set; }
        [ForeignKey("PromotionId")]
        public Promotion? Promotion { get; set; }

        public DateTime? DateDebut { get; set; }

        public DateTime? DateFin { get; set; }

        public int NbVentes { get; set; } = 0;

        public string? Commentaires { get; set; }
    }
}
