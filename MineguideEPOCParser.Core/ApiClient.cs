using System.Net.Http.Json;
using System.Text;
using Polly;
using Serilog;
using Newtonsoft.Json;
using JsonException = Newtonsoft.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;
using System.Text.Json.Serialization;
using JsonIgnoreAttribute = System.Text.Json.Serialization.JsonIgnoreAttribute;
using Newtonsoft.Json.Linq;
using Polly.Retry;

namespace MineguideEPOCParser.Core
{
	public static class ApiClient
	{
		private const string ApiUrl = "https://mineguide-epoc.itaca.upv.es:11434/api/generate";
        private const string ApiKey = "32868ebff04b45108ae1637756df5778";

		public static async Task<TOutput?> CallToApiJson<TOutput>(string t, string model, string? system, ILogger? log = null, CancellationToken cancellationToken = default)
        {
            var jsonRetryPolicy = CreateJsonRetryPolicy(log);
            var httpRetryPolicy = CreateHttpRetryPolicy(log);

            var retryPolicy = Policy.WrapAsync(jsonRetryPolicy, httpRetryPolicy);

            using var client = new HttpClient();

            var uri = new Uri(ApiUrl);
            RequestConfig generateRequest = CreateRequestConfig(t, model, system, format: "json");

            try
            {
                return await retryPolicy.ExecuteAsync(GetExecuteApiCallJsonFunc<TOutput>(client, uri, generateRequest, log, cancellationToken));
            }
            catch (JsonException ex)
            {
                // JsonException is thrown when the API returns an invalid JSON response.
                // We account for this and we have a retry policy set in place.
                // But if we exhaust all retries and the response is still invalid, we log the error and return the default value as output.
                log?.Warning(ex, "Error from API - Invalid JSON. Exhausted all retries. Returning default value.\nError Message: {ExceptionMessage}", ex.Message);
                return default;
            }
        }

        public static async Task<string?> CallToApiText(string t, string model, string? system, ILogger? log = null, CancellationToken cancellationToken = default)
        {
            var retryPolicy = CreateHttpRetryPolicy(log);

            using var client = new HttpClient();

            var uri = new Uri(ApiUrl);
            RequestConfig generateRequest = CreateRequestConfig(t, model, system, format: null);

            return await retryPolicy.ExecuteAsync(GetExecuteApiCallFunc(client, uri, generateRequest, log, cancellationToken));
        }

        private static Func<Task<string>> GetExecuteApiCallFunc(HttpClient client, Uri uri, RequestConfig generateRequest, ILogger? log, CancellationToken cancellationToken)
        {
            return async () =>
            {
                var apiResponse = await ExecuteApiCall(client, uri, generateRequest, log, cancellationToken);
                return apiResponse.Response;
            };
        }

        private static Func<Task<TOutput>> GetExecuteApiCallJsonFunc<TOutput>(HttpClient client, Uri uri, RequestConfig generateRequest, ILogger? log, CancellationToken cancellationToken)
        {
            return async () =>
            {
                var apiResponse = await ExecuteApiCall(client, uri, generateRequest, log, cancellationToken);
                var output = ExtractJsonOutputFromApiResponse<TOutput>(log, apiResponse, cancellationToken);
                return output;
            };
        }

        private static async Task<ApiResponse> ExecuteApiCall(HttpClient client, Uri uri, RequestConfig generateRequest, ILogger? log, CancellationToken cancellationToken)
        {
            log?.Information("Calling API...");

            log?.Verbose("Request:\n{Request}", JsonSerializer.Serialize(generateRequest));
            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = uri,
                Content = new StringContent(JsonSerializer.Serialize(generateRequest), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-API-Key", ApiKey);

            var response = await client.SendAsync(request, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            response.EnsureSuccessStatusCode();

            var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse>(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (apiResponse == null)
            {
                throw new InvalidOperationException("Error: API response is null");
            }

            log?.Debug("Raw API Response:\n{Response}", apiResponse.Response);

            return apiResponse;
        }

        private static TOutput ExtractJsonOutputFromApiResponse<TOutput>(ILogger? log, ApiResponse apiResponse, CancellationToken cancellationToken)
        {
            var output = Utilities.DeserializeObject<TOutput>(apiResponse.Response, new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
            }, DuplicatePropertyNameHandling.Error);

            cancellationToken.ThrowIfCancellationRequested();

            if (output == null)
            {
                throw new InvalidOperationException($"Error: API response is in an invalid format. Could not parse medication as JSON.\nRaw response: {apiResponse.Response}");
            }

            log?.Debug("Extracted output:\n{Output}", output);
            return output;
        }

        private static RequestConfig CreateRequestConfig(string t, string model, string? system, string? format, float? temperature = 0f)
        {
            var generateRequest = new RequestConfig()
            {
                Prompt = t,
                Model = model,
                Options = new RequestOptions
                {
                    Temperature = temperature // Temperature is set to 0 by default for getting the most predictable output possible
                },
                System = system,
                Format = format,
                Stream = false,
            };
            return generateRequest;
        }

        private static AsyncRetryPolicy CreateJsonRetryPolicy(ILogger? log)
        {
            return Policy.Handle<JsonException>()
                .WaitAndRetryAsync(10, i => TimeSpan.FromSeconds(2), (ex, sleepDuration, retryCount, _context) =>
                {
                    log?.Warning(ex, "Error from API - Invalid JSON: {ExceptionMessage}. Retrying in {SleepDuration} seconds... (number of retries: {AttemptNumber})", ex.Message, sleepDuration.TotalSeconds, retryCount);
                });
        }

        private static AsyncRetryPolicy CreateHttpRetryPolicy(ILogger? log)
        {
            return Policy.Handle<HttpRequestException>()
                .WaitAndRetryAsync(
                [
						// 12 retries with exponential backoff
                        TimeSpan.FromSeconds(1), // 1 second
                        TimeSpan.FromSeconds(2), // 2 seconds
                        TimeSpan.FromSeconds(5), // 5 seconds
                        TimeSpan.FromSeconds(10), // 10 seconds
                        TimeSpan.FromSeconds(20), // 20 seconds
                        TimeSpan.FromSeconds(30), // 30 seconds
                        TimeSpan.FromSeconds(60), // 1 minute
                        TimeSpan.FromSeconds(120), // 2 minutes
                        TimeSpan.FromSeconds(300), // 5 minutes
                        TimeSpan.FromSeconds(600), // 10 minutes
						TimeSpan.FromSeconds(1800), // 30 minutes
						TimeSpan.FromSeconds(3600) // 1 hour
                ], (ex, sleepDuration, retryCount, _context) =>
                {
                    log?.Warning(ex, "Error from API - HTTP error: {ExceptionMessage}. Retrying in {SleepDuration} seconds... (number of retries: {AttemptNumber})", ex.Message, sleepDuration.TotalSeconds, retryCount);
                });
        }

        /// <summary>
        /// <see href="https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-completion"/>
        /// </summary>
        private class RequestConfig
		{
			public required string Prompt { get; set; }
			public required string Model { get; set; }

			[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
			public RequestOptions? Options { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? System { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
			public string? Format { get; set; }
			public bool Stream { get; set; }
		}

        /// <summary>
		/// <see href="https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-completion"/>
        /// <see cref="https://github.com/ollama/ollama/blob/main/docs/modelfile.md#parameter"/>
        /// </summary>
        private class RequestOptions
		{
			public float? Temperature { get; set; }
        }

		private class ApiResponse
		{
            [System.Text.Json.Serialization.JsonRequired]
			public required string Response { get; init; }
		}
	}
}
