using System.Net;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    public class MeasurementsExtractingParser : DataParser<MeasurementsExtractingParserConfiguration>
    {
        public const string SystemPrompt = """
        You are meant to parse any medical data sent to you in SPANISH.
        Follow STRICTLY these instructions by order priority:
        - ONLY return the names, values and units of measurements you find AS IS. Don't try to analyze any other context around them. If you see: "FEV1: 50%" or "FVC: 5000ml" or "FEV1/FVC 65%", then that data SHOULD be included, regardless of the origin or correctness. For now, we are just trying to extract these values, not evaluate them.
        - Notice that the same measurement might be included in multiple different units (i.e: ml and %). You should include both of them in different objects. We want all possible representations of measurements, even if it looks redundant. For example, if you have 2 FEV1 measurements in ml and %, and then 2 FVC measurements, also in ml and %, you would end up with 4 JSON objects, 2 for the 2 FEV1 measurements, and another 2 for the other 2 FVC measurements.
        - If the text is blank, return an empty JSON object.
        - The JSON format should be: { "Measurements": [{"Type": <Name of the measurement>, "Value": <number WITHOUT the Unit>, "Unit": <"%" or "l" or "ml" (it should be present AFTER THE NUMBER... if it's not, set it to null>} ] }
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

                List<string> subTextsToSearch;
                if (Configuration.MeasurementsToLookFor == null)
                {
                    var textToSearch = t;

                    // Normalize the spelling of some measurements
                    textToSearch = NormalizeText(textToSearch);

                    subTextsToSearch = [textToSearch];
                }
                else
                {
                    // Extract the text to search if looking for specific measurements for improved efficiency
                    subTextsToSearch = [];
                    foreach (var (measurement, text) in ExtractTextToSearch(t))
                    {
                        // Normalize the spelling of some measurements
                        var finalText = NormalizeText(measurement, text);

                        subTextsToSearch.Add(finalText);
                    }

                    if (subTextsToSearch.Count == 0)
                    {
                        Logger?.Warning("No lines of text contain any of the measurements to look for: {MeasurementsToLookFor}.\n\nOriginal text: {T}", string.Join(", ", Configuration.MeasurementsToLookFor), t);
                        continue;
                    }
                }

                var allMeasurements = new List<Measurement>();

                foreach (var textToSearch in subTextsToSearch)
                {
                    // Call the API to extract the measurements
                    var measurementsData = await ApiClient.CallToApi<MeasurementsData>(textToSearch, "llama3.1:latest", SystemPrompt, Logger, cancellationToken);
                    if (measurementsData == null)
                    {
                        Logger?.Warning($"No measurements found in the subtext: {textToSearch}");
                        continue;
                    }
                    allMeasurements.AddRange(measurementsData.Measurements);
                }

                if (allMeasurements.Count == 0)
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

                // For all text searched, use a pipe symbol as a separator
                // (because the CSV parser we use with the output doesn't support multiline text)
                var allTextSearched = string.Join("|", subTextsToSearch);

                // Devuelve cada medida en una fila, ordenados alfabéticamente por tipo
                foreach (var measurement in allMeasurements.OrderBy(m => m.Type))
                {
                    // Duplicate the row for each measurement, including the measurement
                    string[] newRow;

                    // If the unit is missing, deduce it
                    if (string.IsNullOrEmpty(measurement.Unit))
                    {
                        measurement.Unit = DeduceMissingUnit(measurement.Value);
                    }

                    if (Configuration.OverwriteColumn)
                    {
                        newRow = GenerateNewRowWithOverwrite(row, inputColumnIndex, [allTextSearched, measurement.Type, measurement.Value.ToString(), measurement.Unit]).ToArray();
                    }
                    else
                    {
                        // If we are not overwriting the column, add the exact text that was searched, the measurement type, value and unit to the end
                        newRow = row.Append(allTextSearched).Append(measurement.Type).Append(measurement.Value.ToString()).Append(measurement.Unit).ToArray();

                        // In the 'T' column, replace the multiline original text with a single line text
                        newRow[inputColumnIndex] = t;
                    }

                    yield return newRow;
                }
            }
        }

        /// <summary>
        /// <list type="bullet">
        /// <item>If the value is less than 15, assume liters (l)</item>
        /// <item>If the value is between 15 and 99, assume a percentage (%)</item>
        /// <item>If the value is greater than or equal to 100, assume mililiters (ml)</item>
        /// </list>
        /// </summary>
        private static string DeduceMissingUnit(double value) => value switch
        {
            < 15 => "l",
            < 100 => "%",
            _ => "ml"
        };

        private IEnumerable<(string Measurement, string Text)> ExtractTextToSearch(string t)
        {
            if (Configuration.MeasurementsToLookFor == null)
            {
                throw new InvalidOperationException($"{nameof(Configuration)}.{nameof(Configuration.MeasurementsToLookFor)} is null");
            }

            // If we are looking for specific measurements,
            // split the text into lines of text that contain one of these measurements

            var currentIndex = -1;
            while (currentIndex < t.Length)
            {
                List<(int Index, string Measurement)> nextMeasurementsByIndex = [];
                foreach (var measurement in Configuration.MeasurementsToLookFor)
                {
                    var currentMeasurementIndex = t.IndexOf(measurement, currentIndex + 1, StringComparison.OrdinalIgnoreCase);

                    // Save the indexes of any found measurements
                    if (currentMeasurementIndex >= 0)
                    {
                        nextMeasurementsByIndex.Add((currentMeasurementIndex, measurement));
                    }
                }

                // If no measurement was found, finish
                if (nextMeasurementsByIndex.Count == 0)
                {
                    break;
                }

                // We sort the measurements by index to process them in order
                nextMeasurementsByIndex.Sort(Comparer<(int Index, string Measurement)>.Create((x, y) => x.Index.CompareTo(y.Index)));

                // Find the first measurement index
                var nextMeasurement = nextMeasurementsByIndex[0];

                // Find the next line break after the very next measurement
                var nextLineBreakIndex = t.IndexOf('\n', nextMeasurement.Index + 1);

                // Add any measurements before the next line break, each in different strings
                var end = nextLineBreakIndex >= 0 ? nextLineBreakIndex : t.Length;
                for (int i = 1; i < nextMeasurementsByIndex.Count; i++)
                {
                    var lastMeasurement = nextMeasurementsByIndex[i - 1];

                    var currentMeasurement = nextMeasurementsByIndex[i];

                    if (currentMeasurement.Index < end)
                    {
                        yield return (lastMeasurement.Measurement, t[lastMeasurement.Index..currentMeasurement.Index]);
                        nextMeasurement = currentMeasurement;
                    }
                }

                // Now we process the last measurement before the line break

                if (nextLineBreakIndex < 0)
                {
                    // If there are no more line breaks, add the remaining text and finish
                    yield return (nextMeasurement.Measurement, t[nextMeasurement.Index..]);
                    break;
                }
                else
                {
                    // Add the text from the measurement to the next line break (excluding the line break)
                    yield return (nextMeasurement.Measurement, t[nextMeasurement.Index..nextLineBreakIndex]);

                    if (nextLineBreakIndex + 1 >= t.Length)
                    {
                        // If the line break is the last character, we don't need to continue
                        break;
                    }
                }

                currentIndex = nextLineBreakIndex;
            }
        }

        private static string NormalizeText(string text)
        {
            // Normalize FEV1 spelling and remove carriage returns
            return text.Replace("FEV 1", "FEV1").Replace("\r", "");
        }

        private static string NormalizeText(string measurement, string text)
        {
            // Normalize FEV1 spelling
            if (measurement == "FEV 1")
            {
                text = text.Replace("FEV 1", "FEV1");
            }

            // Remove carriage returns
            text = text.Replace("\r", "");

            return text;
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
        public string? Unit { get; set; }

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
        // Important EPOC-related measurements: FEV1 (with both spellings), FVC, FEV1/FVC
        // other possibly useful measurements: DLCO, KCO
        protected virtual string[] GetDefaultMeasurementsToLookFor() => ["FEV1", "FEV 1", "FVC", "DLCO", "KCO"];

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
