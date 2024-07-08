using System.Net;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    public class MedicationExtractingParser : MedicationParser<MedicationExtractingParserConfiguration>
    {
        /// <summary>
        /// Calls the Ollama API to extract the medications from the text in the input column.
        /// </summary>
        protected override async IAsyncEnumerable<string[]> ApplyTransformations(IAsyncEnumerable<string[]> rows, int inputColumnIndex, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                // Recoge la columna que contiene los medicamentos
                var t = row[inputColumnIndex];

                // If the input text is HTML encoded, decode it
                if (Configuration.DecodeHtmlFromInput)
				{
					t = WebUtility.HtmlDecode(t);
				}

                // Llama a la API para extraer los medicamentos
                var medications = await ApiClient.CallToApi(t, Logger, cancellationToken);

                // Devuelve cada medicamento en una fila, ordenados alfabéticamente
                foreach (var medication in medications.Order())
                {
                    // Duplicate the row for each medication, including the medication
                    string[] newRow;

                    if (Configuration.OverwriteColumn)
                    {
                        // If we are overwriting, replace the input column with the medication
                        newRow = row.Select((value, index) => index == inputColumnIndex ? medication : value).ToArray();

                        // Note: If we are overwriting, the 'T' column is being replaced with the medication name,
                        // so we don't need to replace the multiline text with a single line text,
                        // because we are replacing the whole column
                    }
                    else
                    {
                        // If we are not overwriting the column, add the medication to the end
                        newRow = Utilities.ArrayCopyAndAdd(row, medication);

                        // In the 'T' column, replace the multiline original text with a single line text
                        newRow[inputColumnIndex] = t.Replace('\n', '\t').Replace("\r", "");
                    }

                    yield return newRow;
                }
            }
        }
    }

	public class MedicationExtractingParserConfiguration : MedicationParserConfiguration
	{
		public bool DecodeHtmlFromInput { get; set; }
	}
}
