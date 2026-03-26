// NOTE: THIS FILE IS ONLY USED IN THE .NET 10 VERSION OF THE GUI APPLICATION.
// THE .NET 6 VERSION EXCLUDES THIS FILE FROM COMPILATION, BECAUSE WE DON'T SUPPORT Microsoft.Extensions.Configuration IN THE .NET 6 VERSION, AS EXPLAINED IN App.xaml.cs.

using Microsoft.Extensions.Configuration;
using MineguideEPOCParser.Core.LLM;
using System.IO;

namespace MineguideEPOCParser.GUIApp.Utils
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