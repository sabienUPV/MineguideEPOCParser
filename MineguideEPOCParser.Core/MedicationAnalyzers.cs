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
        public static (int MatchCount, double MatchPercentage, Dictionary<string, MedicationDetails> Details) AnalyzeMedicationMatches(
            string t, string[] medications, ILogger? logger = null)
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
                int exactMatchIndex = t.IndexOf(med, StringComparison.OrdinalIgnoreCase);
                bool isExactMatch = exactMatchIndex >= 0;

                // Levenshtein distance matching
                int bestDistance = int.MaxValue;
                string? closestMatch = null;
                int? closestMatchIndex = null;
                double similarityScore = 0;

                // Only check for fuzzy matches if no exact match and medication name is meaningful
                if (!isExactMatch && med.Length > 3)
                {
                    // Split text into words
                    var words = SplitIntoWordsRegex().Matches(t)
                        .Select(m => (m.Value, m.Index))
                        .ToArray();

                    // Check each word for similarity
                    foreach (var (word, index) in words)
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
                            closestMatchIndex = index;
                        }
                    }
                }

                MatchSimilarityType matchType;
                if (isExactMatch)
                {
                    // If we found an exact match,
                    // we didn't need to calculate Levenshtein distance for it,
                    // so we need to manually set the similarity score and best match

                    matchType = MatchSimilarityType.Exact;
                    similarityScore = ExactMatchThreshold; // 100% similarity for exact matches
                    closestMatch = med; // The medication itself is the best match
                    closestMatchIndex = exactMatchIndex; // Use the index of the exact match
                }
                else
                {
                    // Determine match type with threshold
                    matchType = DetermineMatchSimilarityType(similarityScore);
                }

                if (IsStrongSimilarityOrBetter(similarityScore))
                {
                    medicationsInText++;
                }

                medicationDetails[med] = new MedicationDetails
                {
                    Medication = med,
                    ExactMatch = isExactMatch,
                    SimilarityScore = similarityScore,
                    BestMatch = closestMatch,
                    BestMatchIndex = closestMatchIndex,
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

        public const double ExactMatchThreshold = 1.0; // 100% similarity threshold for exact matches
        public const double StrongSimilarityThreshold = 0.8; // 80% similarity threshold for strong matches
        public const double ModerateSimilarityThreshold = 0.6; // 60% similarity threshold for moderate matches

        public static bool IsExactMatch(double similarityScore) => similarityScore >= ExactMatchThreshold; // 100% similarity threshold
        public static bool IsStrongSimilarityOrBetter(double similarityScore) => similarityScore >= StrongSimilarityThreshold; // 80% similarity threshold
        public static bool IsModerateSimilarityOrBetter(double similarityScore) => similarityScore >= ModerateSimilarityThreshold; // 60% similarity threshold

        public static MatchSimilarityType DetermineMatchSimilarityType(double similarityScore)
        {
            if (IsExactMatch(similarityScore)) // 100% similarity threshold
            {
                return MatchSimilarityType.Exact;
            }
            else if (IsStrongSimilarityOrBetter(similarityScore)) // 80% similarity threshold
            {
                return MatchSimilarityType.StrongSimilarity;
            }
            else if (IsModerateSimilarityOrBetter(similarityScore)) // 60% similarity threshold
            {
                return MatchSimilarityType.ModerateSimilarity;
            }

            return MatchSimilarityType.None;
        }

        public record MedicationDetails
        {
            public required string Medication { get; init; }
            public bool ExactMatch { get; init; }
            public double SimilarityScore { get; init; }
            public string SimilarityScorePercentage => GetSimilarityScorePercentage(System.Globalization.CultureInfo.InvariantCulture);
            public string? BestMatch { get; init; }
            public int? BestMatchIndex { get; init; } // Index of the best match in the original text
            public int LevenshteinDistance { get; init; }
            public MatchSimilarityType MatchType { get; init; }

            public string GetSimilarityScorePercentage(IFormatProvider? provider)
                => SimilarityScore.ToString("P2", provider);
        };

        public enum MatchSimilarityType
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
