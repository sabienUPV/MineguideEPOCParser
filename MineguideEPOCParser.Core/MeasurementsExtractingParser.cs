using System.Net;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    public class MeasurementsExtractingParser : DataParser<MeasurementsExtractingParserConfiguration>
    {
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

                // Devuelve cada medida en una fila, ordenados alfabéticamente por tipo
                foreach (var measurement in measurementsData.Measurements.OrderBy(m => m.Type))
                {
                    // Duplicate the row for each measurement, including the measurement
                    string[] newRow;

                    if (Configuration.OverwriteColumn)
                    {
                        // If we are overwriting...
                        newRow = row
                            // Replace the input column with the measurement type
                            .Select((value, index) => index == inputColumnIndex ? measurement.Type : value)
                            // then add the measurement value and unit to the end
                            .Append(measurement.Value).Append(measurement.Unit)
                            .ToArray();

                        // Note: If we are overwriting, the 'T' column is being replaced with the measurement type,
                        // so we don't need to replace the multiline text with a single line text,
                        // because we are replacing the whole column
                    }
                    else
                    {
                        // If we are not overwriting the column, add the measurement type, value and unit to the end
                        newRow = row.Append(measurement.Type).Append(measurement.Value).Append(measurement.Unit).ToArray();

                        // In the 'T' column, replace the multiline original text with a single line text
                        newRow[inputColumnIndex] = t.Replace('\n', '\t').Replace("\r", "");
                    }

                    yield return newRow;
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
	}
}
