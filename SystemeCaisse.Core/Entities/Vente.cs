using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class Vente
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string NumeroTicket { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Identifiant unique généré par le poste au moment de l'encaissement.
        /// Garantit l'idempotence : une vente renvoyée (file hors-ligne, nouvelle tentative)
        /// n'est jamais enregistrée deux fois (index unique).
        /// </summary>
        [MaxLength(36)]
        public string? ClientUid { get; set; }

        /// <summary>Nom du poste de caisse ayant encaissé la vente.</summary>
        [MaxLength(50)]
        public string? Poste { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        public int NbArticles { get; set; }

        [MaxLength(20)]
        public string MoyenPaiement { get; set; } = "especes";

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalRemise { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontantEspeces { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontantCB { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MonnaieRendue { get; set; }

        [MaxLength(20)]
        public string Statut { get; set; } = "validee"; // validee, annulee

        // Navigation property
        public List<LigneVente> Lignes { get; set; } = new();
    }
}
