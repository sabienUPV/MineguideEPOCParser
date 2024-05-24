using OllamaSharp;

namespace Mineguide_EPOC_Script
{
    public static class ApiClient
    {
        public static async Task<string[]> CallToApi(string t)
        {
            var uri = new Uri("http://mineguide-epoc.itaca.upv.es/ollama-api:80");
            var ollama = new OllamaApiClient(uri)
            {
                SelectedModel = "medicamento-parser:latest"
            };

            ConversationContext? context = null;
            context = await ollama.StreamCompletion(t, context, stream => Console.Write(stream.Response));

            string[] medications = new string[100];

            for (int i = 0; i < medications.Length; i++)
            {
                medications[i] = context.ToString();
            }

            /* EXAMPLE */ return medications;
        }
    }
}
