using System;
using System.ComponentModel.DataAnnotations;

namespace SystemeCaisse.Core.Entities
{
    /// <summary>
    /// Compteur de version par domaine de données (Produits, Ventes, Commandes...).
    /// Incrémenté à chaque écriture par les services métier ; chaque poste interroge
    /// cette petite table pour savoir quelles données rafraîchir (synchronisation inter-postes).
    /// </summary>
    public class DataVersion
    {
        [Key]
        [MaxLength(30)]
        public string Domaine { get; set; } = string.Empty;

        public long Version { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>Noms des domaines suivis par <see cref="DataVersion"/>.</summary>
    public static class DataDomains
    {
        public const string Produits = "produits";
        public const string Ventes = "ventes";
        public const string Stocks = "stocks";
        public const string Commandes = "commandes";
        public const string Promotions = "promotions";
        public const string Configuration = "configuration";
        public const string Images = "images";

        public static readonly string[] All = { Produits, Ventes, Stocks, Commandes, Promotions, Configuration, Images };
    }
}
