using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class Promotion
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nom { get; set; } = string.Empty;

        public DateTime DateDebut { get; set; }
        public DateTime DateFin { get; set; }

        [Required]
        [MaxLength(20)]
        public string TypePromotion { get; set; } = "remise_total"; 
        // "remise_total", "remise_produit", "quantite_offerte" (2+1), "remise_ieme" (-X% on 2nd), "prix_degressif", "seuil_panier"

        public int? ProduitId { get; set; }
        [ForeignKey("ProduitId")]
        public Produit? Produit { get; set; }

        [MaxLength(50)]
        public string? Categorie { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Valeur { get; set; } // % or €
        public bool IsPourcentage { get; set; } = true;

        // For quantite_offerte (ex: 2+1)
        public decimal? SeuilQuantite { get; set; } 
        public decimal? QuantiteOfferte { get; set; }

        // For remise_ieme (ex: 50% sur le 2ème)
        public int? IemeArticle { get; set; }
        public decimal? RemiseSurIeme { get; set; }

        // For seuil_panier
        [Column(TypeName = "decimal(18,2)")]
        public decimal? SeuilPanier { get; set; }

        public bool Actif { get; set; } = true;
        public bool Cumulable { get; set; } = false;

        // For prix_degressif
        public List<PromotionTier> Tiers { get; set; } = new();

        // For offre_combine (Bundle)
        public List<PromotionBundleItem> BundleItems { get; set; } = new();
    }
}
