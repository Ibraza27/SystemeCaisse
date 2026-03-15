using System;
using System.ComponentModel.DataAnnotations;

namespace SystemeCaisse.Core.Entities
{
    public class Entreprise
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(100)]
        public string Nom { get; set; } = "Mon Entreprise";

        [MaxLength(200)]
        public string? Adresse { get; set; }

        [MaxLength(20)]
        public string? Telephone { get; set; }

        [MaxLength(50)]
        public string? Siret { get; set; }

        public string? HorairesSemaine { get; set; }
        public string? HorairesDimanche { get; set; }

        public string? LogoPath { get; set; }
        
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
