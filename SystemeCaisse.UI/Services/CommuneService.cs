using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SystemeCaisse.UI.Services
{
    public class CommuneEntry
    {
        public string Display { get; set; } = string.Empty;
        public string Ville { get; set; } = string.Empty;
        public string CodePostal { get; set; } = string.Empty;
        public string NormalizedVille { get; set; } = string.Empty;

        public override string ToString() => Display;
    }

    public static class CommuneService
    {
        private static List<CommuneEntry> _communes = new();
        private static bool _loaded = false;

        public static string NormalizeText(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            string formD = input.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            bool lastWasSpace = false;

            foreach (char c in formD)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark)
                    continue;

                if (c == '-' || c == '\'' || c == '’' || c == '—' || c == '/' || char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(char.ToUpperInvariant(c));
                    lastWasSpace = false;
                }
            }

            return sb.ToString().Trim();
        }

        private static string ExpandSaintAbbreviations(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return normalized;
            var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "ST") parts[i] = "SAINT";
                else if (parts[i] == "STE") parts[i] = "SAINTE";
            }
            return string.Join(' ', parts);
        }

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
                var entries = new List<CommuneEntry>(40000);

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string nom = element.TryGetProperty("nom", out var nomProp) ? nomProp.GetString() ?? "" : "";
                    string normNom = ExpandSaintAbbreviations(NormalizeText(nom));
                    if (element.TryGetProperty("codesPostaux", out var cpArray))
                    {
                        foreach (var cp in cpArray.EnumerateArray())
                        {
                            string codePostal = cp.GetString() ?? "";
                            entries.Add(new CommuneEntry
                            {
                                Display = $"{codePostal} — {nom}",
                                Ville = nom,
                                CodePostal = codePostal,
                                NormalizedVille = normNom
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

        public static List<CommuneEntry> Search(string query, int maxResults = 30)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<CommuneEntry>();
            if (!_loaded) Load();

            string rawTrim = query.Trim();
            if (rawTrim.Length < 1) return new List<CommuneEntry>();

            string qNorm = ExpandSaintAbbreviations(NormalizeText(rawTrim));
            if (string.IsNullOrEmpty(qNorm)) return new List<CommuneEntry>();

            // Separate digits (postal code part) and letters (city part) if user typed both
            var tokens = qNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string? cpToken = tokens.FirstOrDefault(t => t.All(char.IsDigit));
            string cityQuery = string.Join(' ', tokens.Where(t => !t.All(char.IsDigit)));

            bool isPurePostalCode = cpToken != null && string.IsNullOrEmpty(cityQuery);

            var exactMatches = new List<CommuneEntry>();
            var startsWithMatches = new List<CommuneEntry>();
            var wordStartsWithMatches = new List<CommuneEntry>();
            var containsMatches = new List<CommuneEntry>();

            foreach (var c in _communes)
            {
                if (isPurePostalCode)
                {
                    if (c.CodePostal.Equals(cpToken, StringComparison.OrdinalIgnoreCase))
                        exactMatches.Add(c);
                    else if (c.CodePostal.StartsWith(cpToken!, StringComparison.OrdinalIgnoreCase))
                        startsWithMatches.Add(c);
                    continue;
                }

                // If both CP and city were typed (e.g. "75015 PARIS" or "93 SAINT DENIS")
                if (cpToken != null && !c.CodePostal.StartsWith(cpToken, StringComparison.OrdinalIgnoreCase))
                    continue;

                string targetCityQuery = !string.IsNullOrEmpty(cityQuery) ? cityQuery : qNorm;

                if (c.NormalizedVille.Equals(targetCityQuery, StringComparison.Ordinal))
                {
                    exactMatches.Add(c);
                }
                else if (c.NormalizedVille.StartsWith(targetCityQuery, StringComparison.Ordinal))
                {
                    startsWithMatches.Add(c);
                }
                else if (c.NormalizedVille.Contains(" " + targetCityQuery, StringComparison.Ordinal))
                {
                    wordStartsWithMatches.Add(c);
                }
                else if (c.NormalizedVille.Contains(targetCityQuery, StringComparison.Ordinal))
                {
                    containsMatches.Add(c);
                }
            }

            return exactMatches
                .OrderBy(c => c.CodePostal)
                .ThenBy(c => c.Ville)
                .Concat(startsWithMatches.OrderBy(c => isPurePostalCode ? 0 : c.NormalizedVille.Length).ThenBy(c => c.CodePostal))
                .Concat(wordStartsWithMatches.OrderBy(c => c.NormalizedVille.Length).ThenBy(c => c.CodePostal))
                .Concat(containsMatches.OrderBy(c => c.NormalizedVille.Length).ThenBy(c => c.CodePostal))
                .Take(maxResults)
                .ToList();
        }
    }
}
