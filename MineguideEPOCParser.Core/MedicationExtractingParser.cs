using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    public class MedicationExtractingParser : MedicationParser
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
                var medications = await ApiClient.CallToApi(t, Logger, cancellationToken);

                // Devuelve cada medicamento en una fila, ordenados alfabéticamente
                foreach (var medication in medications.Order())
                {
                    // Duplicate the row for each medication, adding the medication to the end
                    var newRow = Utilities.ArrayCopyAndAdd(row, medication);

                    // In the 'T' column, replace the multiline original text with a single line text
                    newRow[inputColumnIndex] = t.Replace('\n', '\t').Replace("\r", "");

                    yield return newRow;
                }
            }
        }
    }
}
