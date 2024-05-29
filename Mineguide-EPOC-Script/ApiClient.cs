using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mineguide_EPOC_Script
{
	public static class ApiClient
    {
        private const string ApiKey = "32868ebff04b45108ae1637756df5778";

		public static async Task<string> CallToApi(string t)
        {
            var client = new HttpClient();

            var uri = new Uri("https://mineguide-epoc.itaca.upv.es:11434/api/generate");

            var generateRequest = new RequestConfig()
            {
                Prompt = t,
                Model = "medicamento-parser",
                // Format = "json",
                Stream = false,
            };

            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = uri,
                Content = new StringContent(JsonSerializer.Serialize(generateRequest), Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-API-Key", ApiKey);

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse>();

            if (apiResponse == null)
			{
                throw new InvalidOperationException("Error: API response is null");
			}
            
            Console.WriteLine(apiResponse.Response);

            return apiResponse.Response;
        }

        private class RequestConfig
        {
            public required string Prompt { get; set; }
            public required string Model { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Format { get; set; }
            public required bool Stream { get; set; }
        }

        private class ApiResponse
        {
            public required string Response { get; init; }
        }
    }
}
