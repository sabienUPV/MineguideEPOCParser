using MineguideEPOCParser.Core.Validation;

namespace MineguideEPOCParser.Core.Parsers.Configurations
{
    public enum NavigationDirection { Next, Back, Stop }
    public record ValidationStepResult(NavigationDirection Direction, List<MedicationResult> Results);

    public class MedicationManualValidatorParserConfiguration : MedicationManualValidatorParserConfigurationBase
    {
        public required Func<string, IEnumerable<MedicationResult>, CancellationToken, Task<ValidationStepResult>> ValidationFunction { get; set; }
    }

    public abstract class MedicationManualValidatorParserConfigurationBase : DataParserConfiguration
    {
        public const char MultipleMedicationsSeparator = '+';

        public const string DefaultReportNumberHeaderName = "Numero";

        public string ReportNumberHeaderName { get; set; } = DefaultReportNumberHeaderName;
        public string MedicationHeaderName { get; set; } = DefaultMedicationHeaderName;

        // NOTE: DON'T MANUALLY SET THE OutputAdditionalHeaderNames for this config (publicly),
        // since we are not supporting that for detecting existing medication matches.

        // MedicationMatch header names
        public string MatchStartIndexHeaderName => BuildMedicationHeader(nameof(MedicationMatch.StartIndex));
        public string MatchLengthHeaderName => BuildMedicationHeader(nameof(MedicationMatch.Length));
        public string MatchInTextHeaderName => BuildMedicationHeader(nameof(MedicationMatch.MatchInText));
        public string MatchExperimentResultHeaderName => BuildMedicationHeader(nameof(MedicationResult.ExperimentResult));
        public string MatchCorrectedMedicationHeaderName => BuildMedicationHeader(nameof(MedicationResult.CorrectedMedication));

        public string BuildMedicationHeader(string header) => $"{MedicationHeaderName}_{header}";

        // StartIndex, Length, MatchInText, ExtractedMedication, CorrectedMedication, + all the details columns except medication (since that would be redundant with the ExtractedMedication column)
        public override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (DefaultTHeaderName,
        [
            // The order is important here! We want the details columns to go first to match the columns from the input file (which comes from MedicationExtractingParser)
            ..MedicationAnalyzers.MedicationDetails.GetDetailsColumnsExceptMedication(),
            ..GetMedicationMatchHeaders()
        ]);

        private string[] GetMedicationMatchHeaders() =>
        [
            ..GetRequiredMedicationMatchHeaders(),
            MatchCorrectedMedicationHeaderName // This one is optional
        ];

        // Return the headers that are required for the medication matches
        public string[] GetRequiredMedicationMatchHeaders() =>
        [
            MatchStartIndexHeaderName,
            MatchLengthHeaderName,
            MatchInTextHeaderName,
            MatchExperimentResultHeaderName,
        ];
    }
}
