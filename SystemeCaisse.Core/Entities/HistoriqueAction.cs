using System;
using System.ComponentModel.DataAnnotations;

namespace SystemeCaisse.Core.Entities
{
    public class HistoriqueAction
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string TypeAction { get; set; } = string.Empty;

        public string? Description { get; set; }

        [MaxLength(50)]
        public string? Utilisateur { get; set; }

        public string? DonneesJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
