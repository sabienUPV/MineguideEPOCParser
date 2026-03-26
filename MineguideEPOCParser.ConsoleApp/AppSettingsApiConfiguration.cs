using Microsoft.Extensions.Configuration;
using MineguideEPOCParser.Core.LLM;

namespace MineguideEPOCParser.ConsoleApp
{
    public class AppSettingsApiConfiguration : ApiConfiguration
    {
        private static readonly IConfiguration Configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        public override string ApiUrl => Configuration["ApiUrl"] ?? DefaultApiUrl;
        public override string ApiKey => Configuration["ApiKey"] ?? throw new InvalidOperationException($"Ollama API key ('{nameof(ApiKey)}' property) is not set in appsettings.json.");

    }
}
