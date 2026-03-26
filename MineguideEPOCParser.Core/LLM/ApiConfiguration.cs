namespace MineguideEPOCParser.Core.LLM
{
    public abstract class ApiConfiguration
    {
        public const string DefaultApiUrl = "https://mineguide.itaca.upv.es:11434/api/generate";

        public abstract string ApiUrl { get; }
        public abstract string ApiKey { get; }
    }
}
