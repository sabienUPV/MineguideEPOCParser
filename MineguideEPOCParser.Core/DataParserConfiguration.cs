namespace MineguideEPOCParser.Core
{
    public class DataParserConfiguration
    {
        public required string CultureName { get; set; }
        public required string InputFile { get; set; }
        public required string OutputFile { get; set; }

        public string InputHeaderName { get; set; }
        public string OutputHeaderName { get; set; }

        /// <summary>
        /// If true, the input column will be overwritten with the output column, renaming the header, instead of adding a new column.
        /// </summary>
        public bool OverwriteColumn { get; set; } = false;

        public int? Count { get; set; }

        public DataParserConfiguration()
        {
            (InputHeaderName, OutputHeaderName) = GetDefaultColumns();
        }

        public const string THeaderName = "T";
        public const string MedicationHeaderName = "Medication";
        protected virtual (string inputHeader, string outputHeader) GetDefaultColumns() => (THeaderName, MedicationHeaderName);
    }
}
