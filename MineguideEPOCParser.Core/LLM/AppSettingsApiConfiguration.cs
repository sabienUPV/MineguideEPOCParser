using Microsoft.Extensions.Configuration;

namespace MineguideEPOCParser.Core.LLM
{
    public class AppSettingsApiConfiguration : ApiConfiguration
    {
        private static readonly IConfiguration Configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        public override string ApiUrl => Configuration["ApiUrl"] ?? "https://mineguide.itaca.upv.es:11434/api/generate";
        public override string ApiKey => Configuration["ApiKey"] ?? throw new InvalidOperationException("Ollama API key ('ApiKey' property) is not set in appsettings.json.");

    }
}
