using Serilog;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace MineguideEPOCParser.Core
{
    public static partial class MedicationAnalyzers
    {
        /// <summary>
        /// Analyze how extracted medications match the text and log the results
        /// </summary>
        /// <param name="t">The original text to analyze</param>
        /// <param name="medications">Array of extracted medication names</param>
        /// <param name="logger">Optional Serilog logger</param>
        /// <returns>Analysis statistics including match count and percentage</returns>
        public static (int matchCount, double matchPercentage, Dictionary<string, MedicationDetails> details) AnalyzeMedicationMatches(
            string t, string[] medications, ILogger? logger)
        {
            int medicationsInText = 0;
            var medicationDetails = new Dictionary<string, MedicationDetails>();

            foreach (var med in medications)
            {
                // Skip empty medications
                if (string.IsNullOrWhiteSpace(med)) continue;

                // Skip duplicate medications
                if (medicationDetails.ContainsKey(med)) continue;

                // Case-insensitive exact match check
                bool isExactMatch = t.Contains(med, StringComparison.OrdinalIgnoreCase);

                // Levenshtein distance matching
                int bestDistance = int.MaxValue;
                string? closestMatch = null;
                double similarityScore = 0;

                // Only check for fuzzy matches if no exact match and medication name is meaningful
                if (!isExactMatch && med.Length > 3)
                {
                    // Split text into words
                    var words = SplitIntoWordsRegex().Matches(t)
                        .Select(m => m.Value)
                        .ToArray();

                    // Check each word for similarity
                    foreach (var word in words)
                    {
                        // Skip very short words
                        if (word.Length < 3) continue;

                        // Calculate Levenshtein distance
                        int distance = Fastenshtein.Levenshtein.Distance(med.ToLower(), word.ToLower());

                        // Calculate similarity as percentage (higher is better)
                        double similarity = 1.0 - ((double)distance / Math.Max(med.Length, word.Length));

                        // Keep track of the best match
                        if (similarity > similarityScore)
                        {
                            similarityScore = similarity;
                            bestDistance = distance;
                            closestMatch = word;
                        }
                    }
                }

                // Determine match type with threshold
                MatchType matchType = MatchType.None;
                if (isExactMatch)
                {
                    matchType = MatchType.Exact;
                    medicationsInText++;
                }
                else if (similarityScore >= 0.8) // 80% similarity threshold
                {
                    matchType = MatchType.StrongSimilarity;
                    medicationsInText++;
                }
                else if (similarityScore >= 0.6) // 60% similarity threshold
                {
                    matchType = MatchType.ModerateSimilarity;
                }

                medicationDetails[med] = new MedicationDetails
                {
                    Medication = med,
                    ExactMatch = isExactMatch,
                    SimilarityScore = similarityScore,
                    BestMatch = closestMatch,
                    LevenshteinDistance = isExactMatch ? 0 : bestDistance,
                    MatchType = matchType
                };
            }

            // Calculate match percentage for reporting
            double matchPercentage = medications.Length > 0
                ? (double)medicationsInText / medications.Length * 100
                : 0;

            logger?.Debug("Medications present in text (exact or strong similarity): {MedicationsInText}/{MedicationsFound} ({MatchPercentage:F1}%)",
                medicationsInText, medications.Length, matchPercentage);
            logger?.Verbose("Medication match details: {@MedicationDetails}", medicationDetails);

            // Return statistics for potential further processing
            return (medicationsInText, matchPercentage, medicationDetails);
        }

        public record MedicationDetails
        {
            public required string Medication { get; init; }
            public bool ExactMatch { get; init; }
            public double SimilarityScore { get; init; }
            public string SimilarityScorePercentage => GetSimilarityScorePercentage(System.Globalization.CultureInfo.InvariantCulture);
            public string? BestMatch { get; init; }
            public int LevenshteinDistance { get; init; }
            public MatchType MatchType { get; init; }

            public string GetSimilarityScorePercentage(IFormatProvider? provider)
                => SimilarityScore.ToString("P2", provider);
        };

        public enum MatchType
        {
            None,
            Exact,
            [Display(Name = "Strong Similarity")]
            StrongSimilarity,
            [Display(Name = "Moderate Similarity")]
            ModerateSimilarity
        }

        [GeneratedRegex(@"\b\w+\b", RegexOptions.CultureInvariant)]
        private static partial System.Text.RegularExpressions.Regex SplitIntoWordsRegex();
    }
}
