using MineguideEPOCParser.Core.Validation;

namespace MineguideEPOCParser.Core.Parsers.Configurations
{
    public class MedicationManualValidatorParserConfiguration : MedicationManualValidatorParserConfigurationBase
    {
        public required Func<string, IEnumerable<MedicationResult>, CancellationToken, Task<MedicationResult[]>> ValidationFunction { get; set; }
    }

    public abstract class MedicationManualValidatorParserConfigurationBase : DataParserConfiguration
    {
        public const string DefaultReportNumberHeaderName = "Numero";

        public string ReportNumberHeaderName { get; set; } = DefaultReportNumberHeaderName;
        public string MedicationHeaderName { get; set; } = DefaultMedicationHeaderName;

        // This parser is special! And it is meant to be used with the output of the MedicationExtractingParser,
        // so we want to skip adding duplicate headers because we want to overwrite
        // the existing medication details columns with the new validated values, instead of adding new columns with duplicate header names.
        public override bool SkipDuplicateHeaders => true;

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
        public override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (DefaultTHeaderName, [
            // The order is important here! We want the details columns to go first to match the columns from the input file (which comes from MedicationExtractingParser)
            ..MedicationAnalyzers.MedicationDetails.GetDetailsColumnsExceptMedication(),
            MatchStartIndexHeaderName,
            MatchLengthHeaderName,
            MatchInTextHeaderName,
            MatchExperimentResultHeaderName,
            MatchCorrectedMedicationHeaderName
        ]);

        public string[] GetRequiredMedicationMatchHeaders()
        {
            // Return the headers that are required for the medication matches
            return [
                MatchStartIndexHeaderName,
                MatchLengthHeaderName,
                MatchInTextHeaderName,
                MatchExperimentResultHeaderName,
                //MatchCorrectedMedicationHeaderName // This one is optional, so we purposefully don't include it here
            ];
        }
    }
}
