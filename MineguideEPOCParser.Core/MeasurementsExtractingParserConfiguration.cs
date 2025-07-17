namespace MineguideEPOCParser.Core
{
    public class MeasurementsData
    {
        public required Measurement[] Measurements { get; set; }

        public override string ToString() => string.Join(',', Measurements.Select(m => m.ToString()));
    }

    public class Measurement
    {
        public required string Type { get; set; }
        public required double Value { get; set; }
        public string? Unit { get; set; }

        public override string ToString() => $"{{\"Type\": \"{Type}\", \"Value\": {Value}, \"Unit\": \"{Unit}\"}}";
    }

    public class SimpleMeasurementsExtractingParserConfiguration : DataParserConfiguration
    {
        public bool DecodeHtmlFromInput { get; set; }

        public const string FEV1HeaderName = "FEV1 (%)";
        protected override (string inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (THeaderName, [FEV1HeaderName]);
    }

    public class ComplexMeasurementsExtractingParserConfiguration : SimpleMeasurementsExtractingParserConfiguration
    {
        /// <summary>
        /// Only send to the LLM API lines of text that contain at least one of these measurements (case insensitive) to reduce the search scope.
        /// <para>
        /// (Note: We only check for the presence of these strings in the lines of text. We don't check if they actually contain values. The LLM's work is to validate and extract those)
        /// </para>
        /// </summary>
        public string[]? MeasurementsToLookFor { get; set; }

        // Default measurements to look for
        // Important EPOC-related measurements: FEV1, FVC, FEV1/FVC
        // other possibly useful measurements: DLCO, KCO
        protected virtual string[] GetDefaultMeasurementsToLookFor() => ["FEV1/FVC", "FEV1", "FVC", "DLCO", "KCO"];

        public ComplexMeasurementsExtractingParserConfiguration() : base()
        {
            MeasurementsToLookFor = GetDefaultMeasurementsToLookFor();
        }

        // Create from the base class
        public static ComplexMeasurementsExtractingParserConfiguration FromSimple(SimpleMeasurementsExtractingParserConfiguration conf)
        {
            return new ComplexMeasurementsExtractingParserConfiguration()
            {
                CultureName = conf.CultureName,
                InputFile = conf.InputFile,
                OutputFile = conf.OutputFile,
                RowLimit = conf.RowLimit,
                OverwriteInputTargetColumn = conf.OverwriteInputTargetColumn,
                DecodeHtmlFromInput = conf.DecodeHtmlFromInput,
            };
        }

        // Default column names

        /// <summary>
        /// The text that was sent to the API (may be different from the original text if we reduced the search scope with <see cref="MeasurementsToLookFor"/>)
        /// </summary>
        public const string TextSearchedHeaderName = "T-Searched";

        public const string MeasurementTypeHeaderName = "Type";
        public const string MeasurementValueHeaderName = "Value";
        public const string MeasurementUnitHeaderName = "Unit";

        protected override (string inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (THeaderName, [TextSearchedHeaderName, MeasurementTypeHeaderName, MeasurementValueHeaderName, MeasurementUnitHeaderName]);
    }
}
