namespace MineguideEPOCParser.Core.Parsers.Configurations
{
    public class RandomSamplerParserConfiguration : DataParserConfiguration
    {
        // Randomly generated default seed value for reproducibility.
        public const int DefaultSeed = 1947;
        // Default sample size for the random sampling.
        public const int DefaultSampleSize = 100;

        public int SampleSize { get; set; } = DefaultSampleSize;
        public int Seed { get; set; } = DefaultSeed;

        // Files including reports to exclude from the sample (e.g., if you already had those in a previous sample and want to avoid duplicates in the new sample).
        public string[]? ExcludeFiles { get; set; }
        // Report number header for primary key matching with the exclude file (if applicable).
        public string ReportNumberHeaderName { get; set; } = MedicationManualValidatorParserConfiguration.DefaultReportNumberHeaderName;

        // No additional output columns, just sampling the input
        public override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (null, []);
    }
}
