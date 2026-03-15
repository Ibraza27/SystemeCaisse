using System;
using System.ComponentModel.DataAnnotations;

namespace SystemeCaisse.Core.Entities
{
    public class Fournisseur
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nom { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Contact { get; set; }

        [MaxLength(20)]
        public string? Telephone { get; set; }

        [MaxLength(100)]
        public string? Email { get; set; }

        public string? Adresse { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
