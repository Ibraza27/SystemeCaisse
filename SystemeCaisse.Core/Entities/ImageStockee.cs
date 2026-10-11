using System;
using System.ComponentModel.DataAnnotations;

namespace SystemeCaisse.Core.Entities
{
    /// <summary>
    /// Image stockée en base (photos produits, logo entreprise).
    /// La clé correspond au chemin relatif historique (ex: "Images/Produits/produit_42_6385.png")
    /// afin que <see cref="Produit.ImagePath"/> reste inchangé. Chaque poste garde un cache disque local.
    /// </summary>
    public class ImageStockee
    {
        [Key]
        [MaxLength(255)]
        public string Cle { get; set; } = string.Empty;

        public byte[] Donnees { get; set; } = Array.Empty<byte>();

        /// <summary>SHA-256 hexadécimal du contenu (détection des changements pour le cache local).</summary>
        [MaxLength(64)]
        public string Hash { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
