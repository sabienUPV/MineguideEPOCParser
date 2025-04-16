namespace MineguideEPOCParser.Core
{
    public class DataParserConfiguration
    {
        public required string CultureName { get; set; }
        public required string InputFile { get; set; }
        public required string OutputFile { get; set; }

        public string InputHeaderName { get; set; }
        public string[] OutputHeaderNames { get; set; }

        public int NumberOfOutputColumns => OutputHeaderNames?.Length ?? 0;

        /// <summary>
        /// If true, the input column will be overwritten with the output column, renaming the header, instead of adding a new column.
        /// </summary>
        public bool OverwriteColumn { get; set; } = false;

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
        public int? Count { get; set; }

        public DataParserConfiguration()
        {
            (InputHeaderName, OutputHeaderNames) = GetDefaultColumns();
        }

        public const string THeaderName = "T";
        public const string MedicationHeaderName = "Medication";
        protected virtual (string inputHeader, string[] outputHeaders) GetDefaultColumns() => (THeaderName, [MedicationHeaderName]);
    }
}
