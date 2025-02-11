using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace MineguideEPOCParser.Core
{
    public class MeasurementsExtractingParser : DataParser<MeasurementsExtractingParserConfiguration>
    {
        public const string SystemPrompt = """
        You are meant to parse any medical data sent to you in SPANISH.
        Follow STRICTLY these instructions by order priority:
        - ONLY return the values and units of "FEV1", "FVC", "FEV1/FVC", "DLCO", "KCO" measurements you find AS IS. Don't try to analyze any other context around them. If you see: "FEV1: 50%" or "FVC: 5000ml" or "FEV1/FVC 65%", then that data SHOULD be included, regardless of the origin or correctness. For now, we are just trying to extract these values, not evaluate them.
        - Notice that the same measurement might be included in multiple different units (i.e: ml and %). You should include both of them in different objects. We want all possible representations of measurements, even if it looks redundant. For example, if you have 2 FEV1 measurements in ml and %, and then 2 FVC measurements, also in ml and %, you would end up with 4 JSON objects, 2 for the 2 FEV1 measurements, and another 2 for the other 2 FVC measurements.
        - DON'T EXCLUDE measurements that you consider might not be relevant. 
        - Ignore any other measurements that might look like they are related, such as "FEVI" or "PFR". They have probably different meanings than you think, and may confuse you. Stick only to literal "FEV1", "FVC", "FEV1/FVC", "DLCO" and "KCO" measurements, that's it.
        - REMEMBER: In SPANISH, commas (",") are used as DECIMAL delimiters (like dots "." in English). If the number has either a comma or a dot, always assume it's a decimal point, since we don't expect any thousands delimiters.
        - If the text is blank, return an empty JSON object.
        - The JSON format should be: { "Measurements": [{"Type": <"FEV1" or "FVC" or "FEV1/FVC">, "Value": <number WITHOUT the Unit>, "Unit": <"%" or "l" or "ml" (it should be present AFTER THE NUMBER...>} ] }
        """;

        // 4 Output columns: TextSearched, Type, Value, Unit
        public override int OutputColumnsCount => 4;

        /// <summary>
        /// Calls the Ollama API to extract the measurements from the text in the input column.
        /// </summary>
        protected override async IAsyncEnumerable<string[]> ApplyTransformations(IAsyncEnumerable<string[]> rows, int inputColumnIndex, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                // Recoge la columna que contiene las medidas
                var t = row[inputColumnIndex];

                // If the input text is HTML encoded, decode it
                if (Configuration.DecodeHtmlFromInput)
				{
					t = WebUtility.HtmlDecode(t);
				}

                // Extract the text to search if looking for specific measurements for improved efficiency
                string? textToSearch = ExtractTextToSearch(t);

                if (textToSearch == null)
                {
                    continue;
                }

                // Llama a la API para extraer las medidas
                var measurementsData = await ApiClient.CallToApi<MeasurementsData>(textToSearch, "llama3.1:latest", SystemPrompt, Logger, cancellationToken);

                if (measurementsData == null)
                {
                    Logger?.Warning($"No measurements found in the text: {t}");
                    continue;
                }

                if (!Configuration.OverwriteColumn)
                {
                    // In the 'T' column, replace the multiline original text with a single line text
                    // (only if we are not overwriting it)
                    t = t.Replace('\n', '\t').Replace("\r", "");
                }

                // For the text to search, replace the multiline original text with a single line text
                // (we need this because the CSV parser we use with the output doesn't support multiline text)
                textToSearch = textToSearch.Replace('\n', '\t').Replace("\r", "");

                // Devuelve cada medida en una fila, ordenados alfabéticamente por tipo
                foreach (var measurement in measurementsData.Measurements.OrderBy(m => m.Type))
                {
                    // Duplicate the row for each measurement, including the measurement
                    string[] newRow;

                    if (Configuration.OverwriteColumn)
                    {
                        newRow = GenerateNewRowWithOverwrite(row, inputColumnIndex, [textToSearch, measurement.Type, measurement.Value.ToString(), measurement.Unit]).ToArray();
                    }
                    else
                    {
                        // If we are not overwriting the column, add the exact text that was searched, the measurement type, value and unit to the end
                        newRow = row.Append(textToSearch).Append(measurement.Type).Append(measurement.Value.ToString()).Append(measurement.Unit).ToArray();

                        // In the 'T' column, replace the multiline original text with a single line text
                        newRow[inputColumnIndex] = t;
                    }

                    yield return newRow;
                }
            }
        }

        private string? ExtractTextToSearch(string t)
        {
            // If we are looking for specific measurements,
            // cut the text to only send the lines of text that contain at least one of these measurements
            if (Configuration.MeasurementsToLookFor == null)
            {
                // If we are not looking for specific measurements, send the whole text
                return t;
            }

            StringBuilder sb = new();

            var remainingText = t;

            while (remainingText.Length > 0)
            {
                var nextMeasurementIndex = -1;
                foreach (var m in Configuration.MeasurementsToLookFor)
                {
                    nextMeasurementIndex = remainingText.IndexOf(m, nextMeasurementIndex);
                    if (nextMeasurementIndex >= 0)
                    {
                        break;
                    }
                }

                // If no measurement was found, finish
                if (nextMeasurementIndex < 0)
                {
                    break;
                }

                // If the measurement was found, remove the text before it
                remainingText = remainingText[nextMeasurementIndex..];

                var nextLineBreakIndex = remainingText.IndexOf('\n', nextMeasurementIndex);

                if (nextLineBreakIndex < 0)
                {
                    // If there are no more line breaks, add the remaining text and finish
                    sb.Append(remainingText.AsSpan(nextMeasurementIndex));
                    break;
                }
                else
                {
                    // Add the text from the measurement to the next line break
                    sb.Append(remainingText.AsSpan(nextMeasurementIndex, nextLineBreakIndex + 1));

                    if (nextLineBreakIndex + 1 >= remainingText.Length)
                    {
                        // If the line break is the last character, we don't need to continue
                        break;
                    }

                    // Remove the text that was added
                    remainingText = remainingText[(nextLineBreakIndex + 1)..];
                }
            }

            if (sb.Length == 0)
            {
                Logger?.Warning("No lines of text contain any of the measurements to look for: {MeasurementsToLookFor}.\n\nOriginal text: {T}", string.Join(", ", Configuration.MeasurementsToLookFor), t);
                return null;
            }

            return sb.ToString();
        }

        private static IEnumerable<string> GenerateNewRowWithOverwrite(string[] row, int inputColumnIndex, IEnumerable<string> outputValues)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i == inputColumnIndex)
                {
                    foreach (var outputValue in outputValues)
                    {
                        yield return outputValue;
                    }
                }
                else
                {
                    yield return row[i];
                }
            }
        }
    }

    public class MeasurementsData
    {
        public required Measurement[] Measurements { get; set; }

        public override string ToString() => string.Join(',', Measurements.Select(m => m.ToString()));
    }

    public class Measurement
    {
        public required string Type { get; set; }
        public required double Value { get; set; }
        public required string Unit { get; set; }

        public override string ToString() => $"{{\"Type\": \"{Type}\", \"Value\": {Value}, \"Unit\": \"{Unit}\"}}";
    }

    public class MeasurementsExtractingParserConfiguration : DataParserConfiguration
	{
		public bool DecodeHtmlFromInput { get; set; }

        /// <summary>
        /// Only send to the LLM API lines of text that contain at least one of these measurements (case insensitive) to reduce the search scope.
        /// <para>
        /// (Note: We only check for the presence of these strings in the lines of text. We don't check if they actually contain values. The LLM's work is to validate and extract those)
        /// </para>
        /// </summary>
        public string[]? MeasurementsToLookFor { get; set; }

        // Default measurements to look for
        // Important EPOC-related measurements: FEV1, FVC, FEV1/FVC
        // other possibly useful measurements: DLCO, KCO
        protected virtual string[] GetDefaultMeasurementsToLookFor() => ["FEV1", "FVC", "DLCO", "KCO"];

        public MeasurementsExtractingParserConfiguration() : base()
        {
            MeasurementsToLookFor = GetDefaultMeasurementsToLookFor();
        }

        // Default column names

        /// <summary>
        /// The text that was sent to the API (may be different from the original text if we reduced the search scope with <see cref="MeasurementsToLookFor"/>)
        /// </summary>
        public const string TextSearchedHeaderName = "T-Searched";

        public const string MeasurementTypeHeaderName = "Type";
        public const string MeasurementValueHeaderName = "Value";
        public const string MeasurementUnitHeaderName = "Unit";

        protected override (string inputHeader, string[] outputHeaders) GetDefaultColumns() => (THeaderName, [TextSearchedHeaderName, MeasurementTypeHeaderName, MeasurementValueHeaderName, MeasurementUnitHeaderName]);
    }
}
