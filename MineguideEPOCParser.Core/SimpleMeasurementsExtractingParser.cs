using System.Net;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    // TODO: Create Simple version of the parser (only FEV1 %, no LLM)
    public class SimpleMeasurementsExtractingParser : DataParser<SimpleMeasurementsExtractingParserConfiguration>
    {
        /// <summary>
        /// Use Regex to extract the FEV1 (%) measurements from the text in the input column.
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

                // Normalize the spelling of some measurements
                t = NormalizeText(t);

                // TODO: Extrae las medidas de FEV1 (%) de la columna
                var allMeasurements = ExtractFEV1Measurements(t).ToList();

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
                        newRow = GenerateNewRowWithOverwrite(row, inputColumnIndex, [measurement.Value.ToString()]).ToArray();
                    }
                    else
                    {
                        // If we are not overwriting the column, add the exact text that was searched, the measurement type, value and unit to the end
                        newRow = row.Append(measurement.Value.ToString()).ToArray();

                        // In the 'T' column, replace the multiline original text with a single line text
                        newRow[inputColumnIndex] = t;
                    }

                    yield return newRow;
                }
            }
        }

        private IEnumerable<Measurement> ExtractFEV1Measurements(string t)
        {
            // TODO: Extrae las medidas de FEV1 (%) de la columna
            throw new NotImplementedException();
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

        /// <summary>
        /// Checks if there are any numbers after the measurement in the text.
        /// If there are NOT, then the measurement is not valid for us, since we are expecting a value.
        /// (This can happen with measurements like "FEVI normal" for example, which are not measurable values,
        /// so we should ignore them)
        /// </summary>
        private static bool AnyNumbersAfterMeasurement(int measurementLength, string text)
        {
            // Skip the measurement (we are expecting it to be at the beginning of the text)
            var nextIndex = measurementLength;
            while (nextIndex < text.Length)
            {
                if (char.IsDigit(text[nextIndex]))
                {
                    return true;
                }
                nextIndex++;
            }
            return false;
        }

        private static string NormalizeText(string text)
        {
            // Normalize FEV1 spelling (possible spellings: FEV1, FEV 1, FEVI) and remove carriage returns
            return text.Replace("FEV 1", "FEV1").Replace("FEVI", "FEV1").Replace("\r", "");
        }

        private static string NormalizeText(string measurement, string text)
        {
            // Normalize FEV1 spelling (possible spellings: FEV1, FEV 1, FEVI)
            if (measurement == "FEV 1")
            {
                text = text.Replace("FEV 1", "FEV1");
            }
            else if (measurement == "FEVI")
            {
                text = text.Replace("FEVI", "FEV1");
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
}
