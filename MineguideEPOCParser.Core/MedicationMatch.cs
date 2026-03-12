namespace MineguideEPOCParser.Core
{
    public class MedicationResult
    {
        public required string ExtractedMedication { get; set; } // The medication from your array
        public ExperimentResultType ExperimentResult { get; set; } = ExperimentResultType.TP; // Default to True Positive
        public string? CorrectedMedication { get; set; } // The corrected medication by the user after validating, if any

        /// <summary>
        /// Possible values:
        /// <list type="bullet">
        /// <item>"TP" (True Positive): The medication was found and is correct.</item>
        /// <item>"TP*" (True Positive but not exact): The medication was found, but the match is not exact (e.g., the original text had an accent mark but the LLM removed it - such as "Urbasón" vs "Urbason").</item>
        /// <item>"FP" (False Positive): The medication was found but is incorrect.</item>
        /// <item>"FN" (False Negative): The medication was not found but should have been.</item>
        /// </list>
        /// </summary>
        public enum ExperimentResultType
        {
            /// <summary>
            /// True Positive
            /// </summary>
            TP,
            /// <summary>
            /// True Positive but not exact (TP*)
            /// </summary>
            TP_,
            /// <summary>
            /// False Positive (also used for hallucinations)
            /// </summary>
            FP,
            /// <summary>
            /// False Negative
            /// </summary>
            FN
        }
    }

    public class MedicationMatch : MedicationResult
    {
        public int StartIndex { get; set; }
        public int Length { get; set; }
        public required string MatchInText { get; set; }

        // Comparer by StartIndex
        public class StartIndexComparer : IComparer<MedicationResult>
        {
            public int Compare(MedicationResult? x, MedicationResult? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                if (x is MedicationMatch matchX && y is MedicationMatch matchY)
                {
                    return matchX.StartIndex.CompareTo(matchY.StartIndex);
                }

                // If one is a match and the other is not, the match comes first
                if (x is MedicationMatch) return -1;
                if (y is MedicationMatch) return 1;

                // If both are not matches, they are equal for sorting (they will be at the end)
                return 0;
            }
        }

        // Comparer instance
        public static IComparer<MedicationResult> Comparer { get; } = new StartIndexComparer();
    }

    public static class ExperimentResultTypeExtensions
    {
        /// <summary>
        /// Possible values:
        /// <list type="bullet">
        /// <item>"TP" (True Positive): The medication was found and is correct.</item>
        /// <item>"TP*" (True Positive but not exact): The medication was found, but the match is not exact (e.g., the original text had an accent mark but the LLM removed it - such as "Urbasón" vs "Urbason").</item>
        /// <item>"FP" (False Positive): The medication was found but is incorrect.</item>
        /// <item>"FN" (False Negative): The medication was not found but should have been.</item>
        /// </list>
        /// </summary>
        public static string ToResultString(this MedicationResult.ExperimentResultType resultType)
        {
            return resultType switch
            {
                MedicationResult.ExperimentResultType.TP => "TP",
                MedicationResult.ExperimentResultType.TP_ => "TP*",
                MedicationResult.ExperimentResultType.FP => "FP",
                MedicationResult.ExperimentResultType.FN => "FN",
                _ => throw new ArgumentOutOfRangeException(nameof(resultType), resultType, null)
            };
        }
    }
}
