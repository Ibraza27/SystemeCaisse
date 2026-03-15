using System.ComponentModel.DataAnnotations;

namespace SystemeCaisse.Core.Entities
{
    public class SystemConfiguration
    {
        [Key]
        [MaxLength(50)]
        public string Cle { get; set; } = string.Empty;

        public string? Valeur { get; set; }

        [MaxLength(20)]
        public string Type { get; set; } = "text"; // text, boolean, number, json

        public string? Description { get; set; }
    }
}
