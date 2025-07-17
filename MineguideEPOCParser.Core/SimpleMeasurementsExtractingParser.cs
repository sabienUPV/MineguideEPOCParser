using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MineguideEPOCParser.Core
{
    /// <summary>
    /// Simple version of the parser (only FEV1 %, no LLM)
    /// </summary>
    public partial class SimpleMeasurementsExtractingParser : DataParser<SimpleMeasurementsExtractingParserConfiguration>
    {
        /// <summary>
        /// Use Regex to extract the FEV1 (%) measurements from the text in the input column.
        /// </summary>
        protected override async IAsyncEnumerable<string[]> ApplyTransformations(IAsyncEnumerable<string[]> rows, int inputTargetColumnIndex, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                // Recoge la columna que contiene las medidas
                var t = row[inputTargetColumnIndex];

                // If the input text is HTML encoded, decode it
                if (Configuration.DecodeHtmlFromInput)
				{
					t = WebUtility.HtmlDecode(t);
				}

                // Normalize the spelling of some measurements
                //t = NormalizeText(t);

                Logger?.Debug("Extracting from text: {t}", t);

                // TODO: Extrae las medidas de FEV1 (%) de la columna
                var measurements = ExtractFEV1Measurements(t).ToList();

                if (measurements.Count == 0)
                {
                    Logger?.Warning($"No measurements found in the text: {t}");
                    continue;
                }

                if (!Configuration.OverwriteInputTargetColumn)
                {
                    // In the 'T' column, replace the multiline original text with a single line text
                    // (only if we are not overwriting it)
                    t = t.Replace('\n', '\t').Replace("\r", "");
                }

                // Devuelve cada medida en una fila
                foreach (var measurement in measurements)
                {
                    // Duplicate the row for each measurement, including said measurement in it
                    string[] newRow;

                    if (Configuration.OverwriteInputTargetColumn)
                    {
                        newRow = row.Select((x, i) => i == inputTargetColumnIndex ? measurement : x).ToArray();
                    }
                    else
                    {
                        // If we are not overwriting the column, add the FEV1 value (in %) to the end
                        newRow = [.. row, measurement];

                        // In the 'T' column, replace the multiline original text with a single line text
                        newRow[inputTargetColumnIndex] = t;
                    }

                    yield return newRow;
                }
            }
        }

        private IEnumerable<string> ExtractFEV1Measurements(string t)
        {
            // Crear un Regex que extrae las medidas de FEV1 (%) del texto
            // (ejemplo: "FEV1: 80%" o "FEV1 80%")
            return ExtractFEV1Regex().Matches(t)
                .Select(m =>
                {
                    Logger?.Verbose("FEV1 measurement found: {Measurement}", m.Groups[0].Value);

                    var value = m.Groups[1].Value.Replace(',', '.');

                    Logger?.Debug("FEV1 value: {Value}", value);

                    return value;
                });
        }

        //private static string NormalizeText(string text)
        //{
        //    // Normalize FEV1 spelling (possible spellings: FEV1, FEV 1, FEVI)
        //    // (also replace ALL commas with dots so decimal numbers are correctly parsed)
        //    return text.Replace("FEV 1", "FEV1").Replace("FEVI", "FEV1").Replace(',', '.');
        //}

        /// <summary>
        /// Regex to extract the FEV1 (%) measurements from the text.
        /// Notes:
        /// - The measurement must be followed by a percentage value (%)
        /// - This accepts various spellings ("FEV1", "FEV 1", "FEVI") as the measurement name, in a case-insensitive way
        /// - The value can be a decimal number (with a dot or a comma as the decimal separator)
        /// - It prevents matching the FEV1/FVC ratio by checking that the measurement name is not followed by a slash
        /// - It also prevents matching the measurement if it is followed by a "<" or ">" character (because it might show an interval rather than a single value)
        /// </summary>
        [GeneratedRegex(@"FEV\s?[1I][^\/<>\n]*?(\d+(?:[\.,]\d+)?)\s*?%", RegexOptions.IgnoreCase)]
        internal static partial Regex ExtractFEV1Regex();
    }
}
