using static MineguideEPOCParser.Core.MedicationMatch;

namespace MineguideEPOCParser.Core
{
    public class MedicationMatch
    {
        public int StartIndex { get; set; }
        public int Length { get; set; }
        public required string MatchInText { get; set; }
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
            /// False Positive
            /// </summary>
            FP,
            /// <summary>
            /// False Negative
            /// </summary>
            FN
        }


        // Comparer by StartIndex
        public class StartIndexComparer : IComparer<MedicationMatch>
        {
            public int Compare(MedicationMatch? x, MedicationMatch? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                return x.StartIndex.CompareTo(y.StartIndex);
            }
        }

        // Comparer instance
        public static IComparer<MedicationMatch> Comparer { get; } = new StartIndexComparer();
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
        public static string ToResultString(this ExperimentResultType resultType)
        {
            return resultType switch
            {
                ExperimentResultType.TP => "TP",
                ExperimentResultType.TP_ => "TP*",
                ExperimentResultType.FP => "FP",
                ExperimentResultType.FN => "FN",
                _ => throw new ArgumentOutOfRangeException(nameof(resultType), resultType, null)
            };
        }
    }
}
