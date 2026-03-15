using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class CommandeFournisseur
    {
        [Key]
        public int Id { get; set; }

        public int FournisseurId { get; set; }
        [ForeignKey("FournisseurId")]
        public Fournisseur? Fournisseur { get; set; }

        [Required]
        [MaxLength(50)]
        public string NumeroCommande { get; set; } = string.Empty;

        public DateTime DateCommande { get; set; }

        public DateTime? DateLivraisonPrevue { get; set; }

        [MaxLength(20)]
        public string Statut { get; set; } = "en_cours"; // "en_cours", "livree", "annulee"

        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
