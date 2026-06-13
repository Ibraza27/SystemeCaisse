using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SystemeCaisse.UI.Services
{
    public class CommuneEntry
    {
        public string Display { get; set; } = string.Empty;
        public string Ville { get; set; } = string.Empty;
        public string CodePostal { get; set; } = string.Empty;

        public override string ToString() => Display;
    }

    public static class CommuneService
    {
        private static List<CommuneEntry> _communes = new();
        private static bool _loaded = false;

        public static void Load()
        {
            if (_loaded) return;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "communes.json");
                if (!File.Exists(path))
                {
                    System.Diagnostics.Debug.WriteLine($"CommuneService: file not found at {path}");
                    return;
                }

                string json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var entries = new List<CommuneEntry>();

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string nom = element.TryGetProperty("nom", out var nomProp) ? nomProp.GetString() ?? "" : "";
                    if (element.TryGetProperty("codesPostaux", out var cpArray))
                    {
                        foreach (var cp in cpArray.EnumerateArray())
                        {
                            string codePostal = cp.GetString() ?? "";
                            entries.Add(new CommuneEntry
                            {
                                Display = $"{codePostal} — {nom}",
                                Ville = nom,
                                CodePostal = codePostal
                            });
                        }
                    }
                }

                _communes = entries.OrderBy(e => e.CodePostal).ThenBy(e => e.Ville).ToList();
                _loaded = true;
                System.Diagnostics.Debug.WriteLine($"CommuneService: loaded {_communes.Count} entries");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CommuneService error: {ex.Message}");
            }
        }

        public static List<CommuneEntry> Search(string query, int maxResults = 25)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return new List<CommuneEntry>();
            if (!_loaded) Load();

            string q = query.Trim().ToUpperInvariant();

            // Always search BOTH by postal code prefix AND by city name containing the query
            // This ensures cities only findable by CP are also discovered when typing names and vice versa
            var results = _communes
                .Where(c => c.CodePostal.StartsWith(q) || c.Ville.ToUpperInvariant().Contains(q) || c.Display.ToUpperInvariant().Contains(q))
                .Take(maxResults)
                .ToList();

            return results;
        }
    }
}
