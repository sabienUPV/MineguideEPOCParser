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

            var request = new GenerateCompletionRequest()
            {
                Prompt = t,
            };

            var result = await ollama.GetCompletion(request);

            Console.WriteLine(result);

            return result.Response;
        }
    }
}
