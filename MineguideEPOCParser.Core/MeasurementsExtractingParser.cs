using System.Net;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    public class MeasurementsExtractingParser : DataParser<MeasurementsExtractingParserConfiguration>
    {
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

                // If we are not looking for specific measurements, send the whole text to the API
                string textToSearch;

                // If we are looking for specific measurements,
                // cut the text to only send the lines of text that contain at least one of these measurements
                if (Configuration.MeasurementsToLookFor != null)
                {
                    textToSearch = string.Join('\n', t.Split('\n')
                        .Where(l => Configuration.MeasurementsToLookFor
                            .Any(m => l.Contains(m, StringComparison.OrdinalIgnoreCase))));

                    if (string.IsNullOrWhiteSpace(textToSearch))
                    {
                        Logger?.Warning("No lines of text contain any of the measurements to look for: {MeasurementsToLookFor}.\n\nOriginal text: {T}", string.Join(", ", Configuration.MeasurementsToLookFor), t);
                        continue;
                    }
                }
                else
                {
                    // If we are not looking for specific measurements, send the whole text
                    textToSearch = t;
                }

                // Llama a la API para extraer las medidas
                // TODO: UPDATE LLM MODEL USED TO EXTRACT MEASUREMENTS
                var measurementsData = await ApiClient.CallToApi<MeasurementsData>(textToSearch, "medicamento-parser-dev", Logger, cancellationToken);

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
                        newRow = GenerateNewRowWithOverwrite(row, inputColumnIndex, [textToSearch, measurement.Type, measurement.Value, measurement.Unit]).ToArray();
                    }
                    else
                    {
                        // If we are not overwriting the column, add the exact text that was searched, the measurement type, value and unit to the end
                        newRow = row.Append(textToSearch).Append(measurement.Type).Append(measurement.Value).Append(measurement.Unit).ToArray();

                        // In the 'T' column, replace the multiline original text with a single line text
                        newRow[inputColumnIndex] = t;
                    }

                    yield return newRow;
                }
            }
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
    }

    public class Measurement
    {
        public required string Type { get; set; }
        public required string Value { get; set; }
        public required string Unit { get; set; }
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
