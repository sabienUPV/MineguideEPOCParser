namespace MineguideEPOCParser.Core.Parsers.Configurations
{
    public class MedicationMapperGroupingParserConfiguration : DataParserConfiguration
    {
        public required string InputGroupingFile { get; set; }

        public const string ReplacementHeaderName = "NewName";
        public override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (ReplacementHeaderName, [ReplacementHeaderName]);
    }
}
