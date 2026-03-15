using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SystemeCaisse.Core.Entities
{
    public class Inventaire
    {
        [Key]
        public int Id { get; set; }

        public DateTime DateCreation { get; set; } = DateTime.Now;
        public DateTime? DateValidation { get; set; }

        [MaxLength(20)]
        public string Statut { get; set; } = "En cours"; // "En cours", "Validé"

        [MaxLength(255)]
        public string? Notes { get; set; }

        public List<LigneInventaire> Lignes { get; set; } = new List<LigneInventaire>();
    }
}
