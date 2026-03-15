using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SystemeCaisse.Core.Entities
{
    public class LigneInventaire
    {
        [Key]
        public int Id { get; set; }

        public int InventaireId { get; set; }
        public Inventaire Inventaire { get; set; }

        public int ProduitId { get; set; }
        public Produit Produit { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal QuantiteSysteme { get; set; } // Snapshot at start

        [Column(TypeName = "decimal(18,3)")]
        public decimal QuantiteReelle { get; set; } // Counted value

        [Column(TypeName = "decimal(18,3)")]
        public decimal Ecart => QuantiteReelle - QuantiteSysteme;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ValeurEcart { get; set; } // Cash value of discrepancy
    }
}
