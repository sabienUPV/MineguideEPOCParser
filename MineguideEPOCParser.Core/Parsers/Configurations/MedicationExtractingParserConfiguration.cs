using MineguideEPOCParser.Core.Validation;

namespace MineguideEPOCParser.Core.Parsers.Configurations
{
    public class MedicationExtractingParserConfiguration : DataParserConfiguration
    {
        public const string DefaultSystemPrompt = """
        You are meant to parse any medical data sent to you in SPANISH.
        Follow STRICTLY these instructions by order priority:
        - ONLY return the names of any medication you find AS IS, don't say anything more.
        - If the text is blank, don't say anything, just send a blank message.
        - The JSON format should be: { ""Medicamentos"": [ ] }     
        """;
        public const bool DefaultSystemPromptUsesJsonFormat = true;

        public const string InputRowNumberHeaderName = "InputRowNumber";

        public bool DecodeHtmlFromInput { get; set; }

        public string SystemPrompt { get; set; } = DefaultSystemPrompt;

        public bool UseJsonFormat { get; set; } = DefaultSystemPromptUsesJsonFormat;

        /// <summary>
        ///  Medication name, input row number, and additional columns for analysis (from <see cref="MedicationAnalyzers.MedicationDetails"/>)
        /// </summary>
        public override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns()
            => (DefaultTHeaderName, [DefaultMedicationHeaderName, InputRowNumberHeaderName, ..MedicationAnalyzers.MedicationDetails.GetDetailsColumnsExceptMedication()]);
    }
}
