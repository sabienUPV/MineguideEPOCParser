using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly;
using Serilog;

namespace MineguideEPOCParser.Core
{
	public static class ApiClient
	{
		private const string ApiKey = "32868ebff04b45108ae1637756df5778";

		public static async Task<string> CallToApi(string t, ILogger? log = null, CancellationToken cancellationToken = default)
        {
            var retryPolicy = Policy.Handle<JsonException>()
                .WaitAndRetryAsync(10, i => TimeSpan.FromSeconds(2), (ex, _sleepDuration, retryCount, _context) =>
				{
					log?.Warning(ex, "Error from API - Invalid JSON: {ExceptionMessage}. Retrying in {SleepDuration} seconds... (number of retries: {AttemptNumber})", ex.Message, _sleepDuration.TotalSeconds, retryCount);
				});

			using var client = new HttpClient();
			
			var uri = new Uri("https://mineguide-epoc.itaca.upv.es:11434/api/generate");

			var generateRequest = new RequestConfig()
			{
				Prompt = t,
				Model = "medicamento-parser-dev",
				Format = "json",
				Stream = false,
			};

			try
			{
				return await retryPolicy.ExecuteAsync(async () =>
				{
					log?.Information("Calling API...");

					log?.Verbose("Request:\n{Request}", JsonSerializer.Serialize(generateRequest));

					using var request = new HttpRequestMessage()
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

					var medicamentosList = JsonSerializer.Deserialize<MedicationsList>(apiResponse.Response);

					cancellationToken.ThrowIfCancellationRequested();

					if (medicamentosList == null)
					{
						throw new InvalidOperationException($"Error: API response is in an invalid format. Could not parse medication as JSON.\nRaw response: {apiResponse.Response}");
					}

					var medicamentosString = string.Join('\n', medicamentosList.Medicamentos);

					log?.Debug("Medication list:\n{MedicationList}", medicamentosString);

					return medicamentosString;
				});
			}
            catch (JsonException ex)
			{
				// JsonException is thrown when the API returns an invalid JSON response.
				// We account for this and we have a retry policy set in place.
				// But if we exhaust all retries and the response is still invalid, we log the error and return an empty medication list.
				log?.Warning(ex, "Error from API - Invalid JSON. Exhausted all retries. Returning empty medication list.\nError Message: {ExceptionMessage}", ex.Message);
				return string.Empty;
			}
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

		private class MedicationsList
		{
			public required string[] Medicamentos { get; set; }
		}
	}
}
