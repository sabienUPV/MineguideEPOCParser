using OllamaSharp;
using OllamaSharp.Models;

namespace Mineguide_EPOC_Script
{
    public static class ApiClient
    {
        public static async Task<string> CallToApi(string t)
        {
            var uri = new Uri("http://mineguide-epoc.itaca.upv.es/ollama-api");
            var ollama = new OllamaApiClient(uri)
            {
                SelectedModel = "llama3"
            };

            var result = await ollama.GetCompletion(t, null!);

            Console.WriteLine(result);

            return result.Response;
        }
    }
}
