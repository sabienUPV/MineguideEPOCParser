using MineguideEPOCParser.Core.LLM;
using MineguideEPOCParser.Core.Parsers.Configurations;
using MineguideEPOCParser.Core.Utils;
using MineguideEPOCParser.Core.Validation;
using System.Net;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core.Parsers
{
    public class MedicationExtractingParser : DataParser<MedicationExtractingParserConfiguration>
    {
        public const string DefaultModel = "llama3:8b";

        /// <summary>
        /// Calls the Ollama API to extract the medications from the text in the input column.
        /// </summary>
        protected override async IAsyncEnumerable<string[]> ApplyTransformations(IAsyncEnumerable<string[]> rows, int inputTargetColumnIndex, string[] inputHeaders, string[] outputHeaders, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                // Recoge la columna que contiene los medicamentos
                var t = row[inputTargetColumnIndex];

                // If the input text is HTML encoded, decode it
                if (Configuration.DecodeHtmlFromInput)
				{
					t = WebUtility.HtmlDecode(t);
				}

                // Llama a la API para extraer los medicamentos
                string[]? medications;
                if (Configuration.UseJsonFormat)
                {
                    var medicationsJson = await ApiClient.CallToApiJson<MedicationsList>(t, DefaultModel, Configuration.SystemPrompt, Logger, cancellationToken);
                    medications = medicationsJson?.Medicamentos.Where(m => !string.IsNullOrWhiteSpace(m)).ToArray();
                }
                else
                {
                    var medicationsText = await ApiClient.CallToApiText(t, DefaultModel, Configuration.SystemPrompt, Logger, cancellationToken);
                    medications = medicationsText?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).ToArray();
                }

                Dictionary<string, MedicationAnalyzers.MedicationDetails>? medicationDetails;
                if (medications == null)
                {
                    Logger?.Warning("No medications found in the text: {Text}", t);
                    continue;
                }
                else
                {
                    Logger?.Information("Extracted {MedicationCount} medications from text (text length: {TextLength})", medications.Length, t.Length);
                    Logger?.Debug("Medications: {@Medications}", medications);
                    (_, _, medicationDetails) = MedicationAnalyzers.AnalyzeMedicationMatches(t, medications, Logger); // Analyze how extracted medications match the text and log the results
                }

                if (!Configuration.OverwriteInputTargetColumn)
                {
                    // In the 'T' column, replace the multiline original text with a single line text
                    // (only if we are not overwriting it)
                    t = t.Replace('\n', '\t').Replace("\r", "");
                }

                // Devuelve cada medicamento en una fila, ordenados alfabéticamente
                foreach (var newRow in CreateNewRowsForEachMedication(row, t, medications, inputTargetColumnIndex, medicationDetails))
                {
                    yield return newRow;
                }
            }
        }

        private IEnumerable<string[]> CreateNewRowsForEachMedication(string[] row, string t, string[] medications, int inputTargetColumnIndex, Dictionary<string, MedicationAnalyzers.MedicationDetails>? medicationDetails)
        {
            // Devuelve cada medicamento en una fila, ordenados alfabéticamente
            foreach (var medication in medications.OrderBy(m => m))
            {
                // Duplicate the row for each medication, including the medication
                string[] newRow;

                if (Configuration.OverwriteInputTargetColumn)
                {
                    // If we are overwriting, replace the input column with the medication
                    if (medicationDetails is null)
                    {
                        newRow = Utilities.ArrayCopyAndReplace(row, inputTargetColumnIndex, medication);
                    }
                    else
                    {
                        newRow = Utilities.ArrayCopyAndReplace(
                            row,
                            inputTargetColumnIndex,
                            CreateNewColumnValuesFromMedicationAndDetails(medication, medicationDetails[medication])
                        );
                    }

                    // Note: If we are overwriting, the 'T' column is being replaced with the medication name,
                    // so we don't need to replace the multiline text with a single line text,
                    // because we are replacing the whole column
                }
                else
                {
                    // If we are not overwriting the column, add the medication to the end
                    if (medicationDetails is null)
                    {
                        newRow = Utilities.ArrayCopyAndAdd(row, medication);
                    }
                    else
                    {
                        newRow = Utilities.ArrayCopyAndAdd(
                            row,
                            CreateNewColumnValuesFromMedicationAndDetails(medication, medicationDetails[medication]));
                    }

                    // In the 'T' column, replace the multiline original text with a single line text
                    newRow[inputTargetColumnIndex] = t;
                }

                yield return newRow;
            }
        }

        private string[] CreateNewColumnValuesFromMedicationAndDetails(string medication, MedicationAnalyzers.MedicationDetails details)
        {
            return
            [
                medication,
                CurrentRowNumber.ToString(),
                ..details.GetDetailsValuesExceptMedication()
            ];
        }
    }

    public class MedicationsList
    {
        [Newtonsoft.Json.JsonRequired]
        public required string[] Medicamentos { get; set; }

        public override string ToString() => string.Join(',', Medicamentos);
    }
}
