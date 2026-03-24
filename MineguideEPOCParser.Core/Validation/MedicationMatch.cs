using CsvHelper.Configuration.Attributes;
using static MineguideEPOCParser.Core.Validation.MedicationAnalyzers;

namespace MineguideEPOCParser.Core.Validation
{
    public class MedicationResult
    {
        public required string ExtractedMedication { get; set; } // The medication from your array
        public ExperimentResultType ExperimentResult { get; set; } = ExperimentResultType.TP; // Default to True Positive
        public string? CorrectedMedication { get; set; } // The corrected medication by the user after validating, if any

        // This property will be ignored in CSV Parser,
        // since for import we don't need it since we calculate it ourselves
        // (or we import via ClassMap which overrides this Ignore),
        // and for export we use GetMedicationMatchValues() to include the details values in the output with full control of the order and formatting.
        [Ignore]
        public MedicationDetails? Details { get; set; }
        
        public const string TPStar = "TP*"; // C# doesn't allow enum members to have special characters, so we use "TP_" and map it to "TP*"
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
            [Name(TPStar)] // This attribute is used by CsvHelper to map the "TP*" string in CSV to the TP_ enum member
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

        // Instead of leaving those fields empty,
        // we put StartIndex = -1 and Length = 0
        // to indicate that it didn't match (and it is not just missing values).
        public virtual string[] GetMedicationMatchValues(IFormatProvider? culture) => PrependDetailsToMatchValuesIfPresent([
                "-1",
                "0",
                string.Empty,
                ExperimentResult.ToResultString(),
                CorrectedMedication ?? string.Empty
            ], culture);

        protected string[] PrependDetailsToMatchValuesIfPresent(string[] matchValues, IFormatProvider? culture) => [
            ..Details?.GetDetailsValuesExceptMedication(culture)
                ?? Enumerable.Repeat(string.Empty, MedicationDetails.GetDetailsColumnsExceptMedication().Length).ToArray(),
            ..matchValues
        ];
    }

    public class MedicationMatch : MedicationResult
    {
        public int StartIndex { get; set; }
        public int Length { get; set; }
        public required string MatchInText { get; set; }

        public override string[] GetMedicationMatchValues(IFormatProvider? culture) => PrependDetailsToMatchValuesIfPresent([
            StartIndex.ToString(),
            Length.ToString(),
            MatchInText,
            ExperimentResult.ToResultString(),
            CorrectedMedication ?? string.Empty
        ], culture);

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
                MedicationResult.ExperimentResultType.TP_ => MedicationResult.TPStar, // C# doesn't allow the enum member to be named "TP*", so we use "TP_" and map it to "TP*"
                _ => resultType.ToString() // The others can be returned as their enum names (TP, FP, FN)
            };
        }
    }
}
