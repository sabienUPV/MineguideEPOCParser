using MineguideEPOCParser.Core.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Polly;
using Polly.Retry;
using Serilog;
using System.Text;

namespace MineguideEPOCParser.Core.LLM
{
	public class ApiClient
	{
        public static ApiConfiguration Configuration = new AppSettingsApiConfiguration();
        public static string ApiUrl => Configuration.ApiUrl;
        public static string ApiKey => Configuration.ApiKey;

        private static readonly HttpClient _sharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };

        public static async Task<TOutput?> CallToApiJson<TOutput>(string t, string model, string? system, ILogger? log = null, CancellationToken cancellationToken = default, bool autoPullModel = true)
        {
            var jsonRetryPolicy = CreateJsonRetryPolicy(log);
            var httpRetryPolicy = CreateHttpRetryPolicy(log);

            var retryPolicy = Policy.WrapAsync(jsonRetryPolicy, httpRetryPolicy);

            var uri = new Uri(ApiUrl);
            RequestConfig generateRequest = CreateRequestConfig(t, model, system, format: "json");

            try
            {
                return await retryPolicy.ExecuteAsync(async (ct) =>
                {
                    var apiResponse = await ExecuteApiCall(_sharedClient, uri, generateRequest, log, ct, autoPullModel);
                    var output = ExtractJsonOutputFromApiResponse<TOutput>(log, apiResponse, ct);
                    return output;
                }, cancellationToken);
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

        public static async Task<string?> CallToApiText(string t, string model, string? system, ILogger? log = null, CancellationToken cancellationToken = default, bool autoPullModel = true)
        {
            var retryPolicy = CreateHttpRetryPolicy(log);

            var uri = new Uri(ApiUrl);
            RequestConfig generateRequest = CreateRequestConfig(t, model, system, format: null);

            return await retryPolicy.ExecuteAsync(async (ct) =>
            {
                var apiResponse = await ExecuteApiCall(_sharedClient, uri, generateRequest, log, ct, autoPullModel);
                return apiResponse.Response;
            }, cancellationToken);
        }

        private static async Task<ApiResponse> ExecuteApiCall(HttpClient client, Uri uri, RequestConfig generateRequest, ILogger? log, CancellationToken cancellationToken, bool autoPullModel)
        {
            log?.Information("Calling API...");
            log?.Debug("API URL: {ApiUrl}", ApiUrl);
            log?.Debug("API Key (truncated): {ApiKey}", Utilities.MaskApiKey(ApiKey));

            var settings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    // ollama's output properties are in snake_case
                    // (https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-completion)
                    NamingStrategy = new SnakeCaseNamingStrategy()
                }
            };

            log?.Verbose("Request:\n{@Request}", generateRequest);
            var request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = uri,
                Content = new StringContent(JsonConvert.SerializeObject(generateRequest, settings), Encoding.UTF8, "application/json")
            };

            // Add API key to the request header if it's set in the configuration
            // This way, if we want to make it work without an API key
            // (for example, if the Ollama API is running locally in the same machine as this code and we are not exposing it outside),
            // we can just set an empty string in the configuration)
            if (!string.IsNullOrEmpty(ApiKey))
            {
                request.Headers.Add("X-API-Key", ApiKey);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var response = await client.SendAsync(request, cancellationToken);

            stopwatch.Stop();

            log?.Information("API call completed in {ProcessingTimeMs} ms", stopwatch.ElapsedMilliseconds);

            cancellationToken.ThrowIfCancellationRequested();

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync();

                // If it's a 404 error, the message mentions the model, and autoPullModel is enabled,
                // we can assume that the error is caused by the model not being found in the server.
                // In that case, we can try to automatically pull the model and retry the request.
                if (autoPullModel &&
                    response.StatusCode == System.Net.HttpStatusCode.NotFound &&
                    errorBody.Contains("model", StringComparison.OrdinalIgnoreCase))
                {
                    log?.Warning("Model '{Model}' is not installed in the server. Initiating automatic download...", generateRequest.Model);

                    await PullModelAsync(client, uri, generateRequest.Model, log, cancellationToken);

                    log?.Information("Model '{Model}' downloaded successfully! Retrying original request...", generateRequest.Model);

                    // Recursive call (setting autoPullModel to false to avoid infinite loops if something goes wrong)
                    return await ExecuteApiCall(client, uri, generateRequest, log, cancellationToken, autoPullModel: false);
                }

                throw new HttpRequestException($"HTTP Error {(int)response.StatusCode} ({response.StatusCode}): {errorBody}");
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            cancellationToken.ThrowIfCancellationRequested();

            using var reader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(reader);

            var serializer = JsonSerializer.Create(settings);
            var apiResponse = serializer.Deserialize<ApiResponse>(jsonReader)
                ?? throw new InvalidOperationException("Error: API response is null");

            log?.Debug("Raw API Response:\n{Response}", apiResponse.Response);
            log?.Verbose("Raw API Response with metadata: {@FullResponse}", apiResponse);

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

        private static async Task PullModelAsync(HttpClient client, Uri originalUri, string modelName, ILogger? log, CancellationToken cancellationToken)
        {
            // Check the pull URL (e.g.: http://localhost:11434/api/generate -> http://localhost:11434/api/pull)
            var pullUri = new Uri(new Uri(originalUri.GetLeftPart(UriPartial.Authority)), "/api/pull");

            var requestBody = new { name = modelName, stream = false };
            var request = new HttpRequestMessage(HttpMethod.Post, pullUri)
            {
                Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrEmpty(ApiKey))
            {
                request.Headers.Add("X-API-Key", ApiKey);
            }

            log?.Information("Downloading model '{Model}'... This might take several minutes depending on the model size and network speed. Please wait.", modelName);

            var pullResponse = await client.SendAsync(request, cancellationToken);

            if (!pullResponse.IsSuccessStatusCode)
            {
                string pullError = await pullResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Failed to automatically pull model '{modelName}'. Status: {pullResponse.StatusCode}. Error: {pullError}");
            }
        }

        /// <summary>
        /// <see href="https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-completion"/>
        /// </summary>
        private class RequestConfig
		{
			public required string Prompt { get; set; }
			public required string Model { get; set; }

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public RequestOptions? Options { get; set; }

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string? System { get; set; }

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
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

        /// <summary>
        /// <see href="https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-completion"/>
        /// </summary>
		private class ApiResponse
		{
            [JsonProperty(Required = Required.Always)]
			public required string Response { get; init; }

            // Other properties, might be interesting for analyzing logs later
            public string? Model { get; set; }
            public string? CreatedAt { get; set; }
            public bool? Done { get; set; }
            public long? TotalDuration { get; set; }
            public long? LoadDuration { get; set; }
            public int? PromptEvalCount { get; set; }
            public long? PromptEvalDuration { get; set; }
            public int? EvalCount { get; set; }
            public long? EvalDuration { get; set; }
            public int[]? Context { get; set; }
        }
	}
}
