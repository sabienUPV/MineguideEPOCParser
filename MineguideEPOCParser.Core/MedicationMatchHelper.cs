namespace MineguideEPOCParser.Core
{
    public static class MedicationMatchHelper
    {
        /// <summary>
        /// Instead of matching medications by exact text,
        /// we allow for fuzzy matching using Levenshtein distance,
        /// as long as the similarity score is strong enough.
        /// </summary>
        public static List<MedicationMatch> FindAllMedicationMatchesBySimilarity(string text, string[] medications)
        {
            var (_, _, medicationDetails) = MedicationAnalyzers.AnalyzeMedicationMatches(text, medications);

            var matches = new List<MedicationMatch>();

            foreach (var kvp in medicationDetails)
            {
                var similarityScore = kvp.Value.SimilarityScore;
                if (MedicationAnalyzers.IsStrongSimilarityOrBetter(similarityScore))
                {
                    matches.Add(new MedicationMatch
                    {
                        StartIndex = kvp.Value.BestMatchIndex!.Value,
                        Length = kvp.Value.BestMatch!.Length,
                        MatchInText = kvp.Value.BestMatch,
                        ExtractedMedication = kvp.Value.Medication,
                        ExperimentResult = kvp.Value.ExactMatch
                            ? MedicationMatch.ExperimentResultType.TP
                            : MedicationMatch.ExperimentResultType.TP_
                    });
                }
            }

            return matches;
        }

        public static List<MedicationMatch> FindAllMedicationMatches(string text, string[] medications)
        {
            var matches = new List<MedicationMatch>();
            var textLower = text.ToLowerInvariant();

            foreach (string medication in medications)
            {
                var medicationLower = medication.ToLowerInvariant();
                var startIndex = 0;

                while (true)
                {
                    var index = textLower.IndexOf(medicationLower, startIndex);
                    if (index == -1) break;

                    // Check for potential overlaps with existing matches
                    var actualText = text.Substring(index, medication.Length);

                    if (!HasOverlap(matches, index, medication.Length))
                    {
                        matches.Add(new MedicationMatch
                        {
                            StartIndex = index,
                            Length = medication.Length,
                            MatchInText = actualText,
                            ExtractedMedication = medication,
                            ExperimentResult = MedicationMatch.ExperimentResultType.TP // Default to True Positive, since we are matching by exact text
                        });
                    }

                    startIndex = index + 1; // Move past this occurrence
                }
            }

            return matches;
        }

        private static bool HasOverlap(List<MedicationMatch> existingMatches, int newStart, int newLength)
        {
            var newEnd = newStart + newLength - 1;

            return existingMatches.Any(match =>
            {
                var existingEnd = match.StartIndex + match.Length - 1;

                // Check if ranges overlap
                return !(newEnd < match.StartIndex || newStart > existingEnd);
            });
        }
    }
}
