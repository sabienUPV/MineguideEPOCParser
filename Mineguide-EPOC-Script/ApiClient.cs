using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mineguide_EPOC_Script
{
    public static class ApiClient
    {
        public static async Task<string> CallToApi(string t)
        {
            var handler = new WinHttpHandler();
            var client = new HttpClient(handler);

            var uri = new Uri("http://mineguide-epoc.itaca.upv.es/ollama-api");

            var generateRequest = new RequestConfig()
            {
                Prompt = t,
                Model = "medicamento-parser",
                Stream = false,
            };

            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = uri,
                Content = new StringContent(JsonSerializer.Serialize(generateRequest), Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();

            Console.WriteLine(responseBody);

            return responseBody;
        }

        private class RequestConfig
        {
            public required string Prompt { get; set; }
            public required string Model { get; set; }
            public required bool Stream { get; set; }
        }
    }
}