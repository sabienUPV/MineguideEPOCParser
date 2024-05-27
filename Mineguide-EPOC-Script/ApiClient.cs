using OllamaSharp;

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

            string med = "";

            ConversationContext? context = null;
            context = await ollama.StreamCompletion(t, context, stream =>
            {
                Console.WriteLine(stream.Response);
            });

            string medications = "";
            /* EXAMPLE */ return medications;
        }
    }
}
