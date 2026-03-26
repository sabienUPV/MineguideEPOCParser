namespace MineguideEPOCParser.Core.LLM
{
    public abstract class ApiConfiguration
    {
        public abstract string ApiUrl { get; }
        public abstract string ApiKey { get; }
    }
}
