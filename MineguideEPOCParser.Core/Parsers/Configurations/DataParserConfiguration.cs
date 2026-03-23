using MineguideEPOCParser.Core.Validation;

namespace MineguideEPOCParser.Core.Parsers.Configurations
{
    public class DataParserConfiguration
    {
        public required string CultureName { get; set; }
        public System.Globalization.CultureInfo Culture => new(CultureName);

        public required string InputFile { get; set; }
        public required string OutputFile { get; set; }

        public string? InputTargetColumnHeaderName { get; set; }
        public string[] OutputAdditionalHeaderNames { get; private set; }

        /// <summary>
        /// If true, the input target column will be overwritten with the output column(s), renaming the header, instead of adding a new column.
        /// </summary>
        public bool OverwriteInputTargetColumn { get; set; } = false;

        /// <summary>
        /// Whether to include a retry policy when the API returns an invalid response (i.e: invalid JSON).
        /// </summary>
        public bool RetryOnInvalidApiResponse { get; set; } = true;

        /// <summary>
        /// <para>
        /// If true, the parser will skip the row if the API response is invalid (and, if applicable, the retry policy is exhausted).
        /// </para>
        /// <para>
        /// If false, the parser will include the raw API response in the output file in a single row
        /// (this can be useful for debugging or testing purposes, such as when we want to compare how different system prompts affect the API response).
        /// </para>
        /// </summary>
        public bool SkipOnInvalidApiResponse { get; set; } = true;

        /// <summary>
        /// [Optional] Limit the number of rows to process.
        /// </summary>
        public int? RowLimit { get; set; }

        /// <summary>
        /// If true, the parser will skip adding the output column(s) if they already exist in the input file (i.e., if there are duplicate headers).
        /// This is false by default, since the parser needs to be able to support this behavior for specific cases,
        /// but it is not meant to be used by all parsers.
        /// <para>
        /// NOTE: This is only meant for specific parser types that are meant to overwrite some existing headers by default
        /// (e.g. <see cref="MedicationManualValidatorParserConfiguration"/>,
        /// where the output overwrites the <see cref="MedicationAnalyzers.MedicationDetails"/> columns
        /// that are already present in the input file, since its input is the output of the <see cref="MedicationExtractingParser"/>).
        /// </para>
        /// </summary>
        public virtual bool SkipDuplicateHeaders => false;

        public DataParserConfiguration()
        {
            (InputTargetColumnHeaderName, OutputAdditionalHeaderNames) = GetDefaultColumns();
        }

        public const string DefaultTHeaderName = "T";
        public const string DefaultMedicationHeaderName = "Medication";
        public virtual (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (DefaultTHeaderName, [DefaultMedicationHeaderName]);
    }
}
