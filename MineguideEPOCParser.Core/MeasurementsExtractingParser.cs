using System.Net;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    public class MeasurementsExtractingParser : DataParser<MeasurementsExtractingParserConfiguration>
    {
        // 3 Output columns: Type, Value, Unit
        public override int OutputColumnsCount => 3;

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

                // Llama a la API para extraer las medidas
                var measurementsData = await ApiClient.CallToApi<MeasurementsData>(t, Logger, cancellationToken);

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

                // Devuelve cada medida en una fila, ordenados alfabéticamente por tipo
                foreach (var measurement in measurementsData.Measurements.OrderBy(m => m.Type))
                {
                    // Duplicate the row for each measurement, including the measurement
                    string[] newRow;

                    if (Configuration.OverwriteColumn)
                    {
                        newRow = GenerateNewRowWithOverwrite(row, inputColumnIndex, [measurement.Type, measurement.Value, measurement.Unit]).ToArray();
                    }
                    else
                    {
                        // If we are not overwriting the column, add the measurement type, value and unit to the end
                        newRow = row.Append(measurement.Type).Append(measurement.Value).Append(measurement.Unit).ToArray();

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

        // Default column names
        public const string MeasurementTypeHeaderName = "Type";
        public const string MeasurementValueHeaderName = "Value";
        public const string MeasurementUnitHeaderName = "Unit";

        protected override (string inputHeader, string[] outputHeaders) GetDefaultColumns() => (THeaderName, [MeasurementTypeHeaderName, MeasurementValueHeaderName, MeasurementUnitHeaderName]);
    }
}
