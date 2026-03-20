using Serilog;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace MineguideEPOCParser.Core
{
    public static partial class MedicationAnalyzers
    {
        /// <summary>
        /// Analyzes how extracted medications match the text and logs the results.
        /// Uses exact matching first, falling back to an anchor-based sliding window 
        /// Levenshtein distance calculation to accurately find compound medications 
        /// while preserving punctuation and spacing.
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

            // Pre-calculate anchors once for the entire text to save CPU
            var textAnchors = SplitIntoWordsRegex().Matches(t);

            foreach (var med in medications)
            {
                // Skip empty or duplicate medications
                if (string.IsNullOrWhiteSpace(med) || medicationDetails.ContainsKey(med))
                    continue;

                // 1. Case-insensitive exact match check (Fastest)
                int exactMatchIndex = t.IndexOf(med, StringComparison.OrdinalIgnoreCase);
                bool isExactMatch = exactMatchIndex >= 0;

                int bestDistance = int.MaxValue;
                string? closestMatch = null;
                int? closestMatchIndex = null;
                double similarityScore = 0;

                // 2. Levenshtein fuzzy matching (Anchor-based sliding window)
                if (!isExactMatch && med.Length > 3)
                {
                    var fuzzyResult = FindBestFuzzyMatch(t, med, textAnchors);

                    similarityScore = fuzzyResult.Similarity;
                    bestDistance = fuzzyResult.Distance;
                    closestMatch = fuzzyResult.Match;
                    closestMatchIndex = fuzzyResult.Index;
                }

                // 3. Determine final match type and populate details
                MatchSimilarityType matchType;

                if (isExactMatch)
                {
                    matchType = MatchSimilarityType.Exact;
                    similarityScore = ExactMatchThreshold;
                    closestMatch = med;
                    closestMatchIndex = exactMatchIndex;
                }
                else
                {
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

            return (medicationsInText, matchPercentage, medicationDetails);
        }

        /// <summary>
        /// Slides a window of words across the text to find the best fuzzy match for a medication.
        /// By using word anchors, it inherently preserves spaces, punctuation (like +), and formatting
        /// from the original text during the comparison.
        /// </summary>
        private static (double Similarity, int Distance, string? Match, int? Index) FindBestFuzzyMatch(
            string text,
            string medication,
            MatchCollection anchors)
        {
            if (anchors.Count == 0)
                return (0, int.MaxValue, null, null);

            int bestDistance = int.MaxValue;
            string? closestMatch = null;
            int? closestMatchIndex = null;
            double bestSimilarityScore = 0.0;

            // Determine roughly how many words make up the medication name
            int medWordCount = SplitIntoWordsRegex().Matches(medication).Count;
            if (medWordCount == 0) medWordCount = 1;

            // Check windows slightly smaller and larger than the target word count
            // to account for LLMs combining or splitting words
            int minWindowSize = Math.Max(1, medWordCount - 1);
            int maxWindowSize = Math.Min(anchors.Count, medWordCount + 2);

            for (int windowSize = minWindowSize; windowSize <= maxWindowSize; windowSize++)
            {
                for (int i = 0; i <= anchors.Count - windowSize; i++)
                {
                    int startIndex = anchors[i].Index;
                    var lastAnchor = anchors[i + windowSize - 1];
                    int length = (lastAnchor.Index + lastAnchor.Length) - startIndex;

                    // Extract the exact substring from the text, preserving ALL punctuation and spaces
                    string candidate = text.Substring(startIndex, length);

                    // Optimization: If the length difference is huge, it mathematically 
                    // cannot be a good match. Skip the expensive calculation.
                    if (Math.Abs(candidate.Length - medication.Length) > (medication.Length * 0.5))
                        continue;

                    int distance = CalculateCaseInsensitiveLevenshteinDistance(medication, candidate);
                    double similarity = CalculateSimilarityScore(medication, candidate, distance);

                    if (similarity > bestSimilarityScore)
                    {
                        bestSimilarityScore = similarity;
                        bestDistance = distance;
                        closestMatch = candidate;
                        closestMatchIndex = startIndex;

                        // Early exit if we find a perfect mathematical match
                        if (similarity >= ExactMatchThreshold)
                        {
                            return (bestSimilarityScore, bestDistance, closestMatch, closestMatchIndex);
                        }
                    }
                }
            }

            return (bestSimilarityScore, bestDistance, closestMatch, closestMatchIndex);
        }

        /// <summary>
        /// Calculate Levenshtein distance (case-insensitive)
        /// </summary>
        public static int CalculateCaseInsensitiveLevenshteinDistance(string value1, string value2)
            => Fastenshtein.Levenshtein.Distance(value1.ToLower(), value2.ToLower());

        /// <summary>
        /// Calculate similarity as percentage (higher is better)
        /// </summary>
        public static double CalculateSimilarityScore(string value1, string value2, int distance) =>
            1.0 - ((double)distance / Math.Max(value1.Length, value2.Length));

        // Thresholds
        public const double ExactMatchThreshold = 1.0; // 100% similarity threshold for exact matches
        public const double StrongSimilarityThreshold = 0.8; // 80% similarity threshold for strong matches
        public const double ModerateSimilarityThreshold = 0.6; // 60% similarity threshold for moderate matches

        // Helper evaluations
        public static bool IsExactMatch(double similarityScore) => similarityScore >= ExactMatchThreshold;
        public static bool IsStrongSimilarityOrBetter(double similarityScore) => similarityScore >= StrongSimilarityThreshold;
        public static bool IsModerateSimilarityOrBetter(double similarityScore) => similarityScore >= ModerateSimilarityThreshold;

        public static MatchSimilarityType DetermineMatchSimilarityType(double similarityScore)
        {
            if (IsExactMatch(similarityScore))
                return MatchSimilarityType.Exact;

            if (IsStrongSimilarityOrBetter(similarityScore))
                return MatchSimilarityType.StrongSimilarity;

            if (IsModerateSimilarityOrBetter(similarityScore))
                return MatchSimilarityType.ModerateSimilarity;

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

            // Helper methods for CSV export
            public static string[] GetDetailsColumnsExceptMedication() => [
                nameof(ExactMatch),
                nameof(SimilarityScore),
                nameof(SimilarityScorePercentage),
                nameof(BestMatch),
                nameof(BestMatchIndex),
                nameof(LevenshteinDistance),
                nameof(MatchType)
            ];

            public string[] GetDetailsValuesExceptMedication(IFormatProvider? culture) => [
                ExactMatch.ToString(),
                SimilarityScore.ToString(),
                GetSimilarityScorePercentage(culture),
                BestMatch ?? string.Empty,
                LevenshteinDistance.ToString(),
                MatchType.ToString()
            ];
        }

        public enum MatchSimilarityType
        {
            None,
            Exact,
            [Display(Name = "Strong Similarity")]
            StrongSimilarity,
            [Display(Name = "Moderate Similarity")]
            ModerateSimilarity
        }

#if NET7_0_OR_GREATER
        [GeneratedRegex(@"\b\w+\b", RegexOptions.CultureInvariant)]
        private static partial Regex SplitIntoWordsRegex();
#else
        private static readonly Regex _splitIntoWordsRegex = new(
            @"\b\w+\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static Regex SplitIntoWordsRegex() => _splitIntoWordsRegex;
#endif
    }
}