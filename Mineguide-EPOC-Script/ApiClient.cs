using System.Text;
using System.Text.Json;

namespace Mineguide_EPOC_Script
{
	public static class ApiClient
    {
        public static async Task<string> CallToApi(string t)
        {
            var client = new HttpClient();

            var uri = new Uri("https://mineguide-epoc.itaca.upv.es/ollama-api/api/generate");

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

            var response = await client.SendAsync(request);
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