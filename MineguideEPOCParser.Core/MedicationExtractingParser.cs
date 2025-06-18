using System.Net;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    public class MedicationExtractingParser : DataParser<MedicationExtractingParserConfiguration>
    {
        public const string DefaultModel = "llama3.1:latest";

        public override int OutputColumnsCount => 7; // Medication name, and 6 additional columns for analysis (from MedicationAnalyzers.MedicationDetails)

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
                string[]? medications;
                if (Configuration.UseJsonFormat)
                {
                    var medicationsJson = await ApiClient.CallToApiJson<MedicationsList>(t, DefaultModel, Configuration.SystemPrompt, Logger, cancellationToken);
                    medications = medicationsJson?.Medicamentos;
                }
                else
                {
                    var medicationsText = await ApiClient.CallToApiText(t, DefaultModel, Configuration.SystemPrompt, Logger, cancellationToken);
                    medications = medicationsText?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

                if (!Configuration.OverwriteColumn)
                {
                    // In the 'T' column, replace the multiline original text with a single line text
                    // (only if we are not overwriting it)
                    t = t.Replace('\n', '\t').Replace("\r", "");
                }

                // Devuelve cada medicamento en una fila, ordenados alfabéticamente
                foreach (var newRow in CreateNewRowsForEachMedication(row, t, medications, inputColumnIndex, medicationDetails))
                {
                    yield return newRow;
                }
            }
        }

        private IEnumerable<string[]> CreateNewRowsForEachMedication(string[] row, string t, string[] medications, int inputColumnIndex, Dictionary<string, MedicationAnalyzers.MedicationDetails>? medicationDetails)
        {
            // Devuelve cada medicamento en una fila, ordenados alfabéticamente
            foreach (var medication in medications.Order())
            {
                // Duplicate the row for each medication, including the medication
                string[] newRow;

                if (Configuration.OverwriteColumn)
                {
                    // If we are overwriting, replace the input column with the medication
                    if (medicationDetails is null)
                    {
                        newRow = Utilities.ArrayCopyAndReplace(row, inputColumnIndex, medication);
                    }
                    else
                    {
                        newRow = Utilities.ArrayCopyAndReplace(
                            row,
                            inputColumnIndex,
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
                    newRow[inputColumnIndex] = t;
                }

                yield return newRow;
            }
        }

        private string[] CreateNewColumnValuesFromMedicationAndDetails(string medication, MedicationAnalyzers.MedicationDetails details)
        {
            return
            [
                medication,
                details.ExactMatch.ToString(),
                details.SimilarityScore.ToString(),
                details.GetSimilarityScorePercentage(Configuration.Culture),
                details.BestMatch ?? string.Empty,
                details.LevenshteinDistance.ToString(),
                details.MatchType.ToString()
            ];
        }
    }

    public class MedicationsList
    {
        [Newtonsoft.Json.JsonRequired]
        public required string[] Medicamentos { get; set; }

        public override string ToString() => string.Join(',', Medicamentos);
    }

    public class MedicationExtractingParserConfiguration : DataParserConfiguration
	{
        public const string DefaultSystemPrompt = """
        You are meant to parse any medical data sent to you in SPANISH.
        Follow STRICTLY these instructions by order priority:
        - ONLY return the names of any medication you find AS IS, don't say anything more.
        - If the text is blank, don't say anything, just send a blank message.
        - The JSON format should be: { ""Medicamentos"": [ ] }     
        """;
        public const bool DefaultSystemPromptUsesJsonFormat = true;


        public bool DecodeHtmlFromInput { get; set; }

        public string SystemPrompt { get; set; } = DefaultSystemPrompt;

        public bool UseJsonFormat { get; set; } = DefaultSystemPromptUsesJsonFormat;

        protected override (string inputHeader, string[] outputHeaders) GetDefaultColumns()
            => (THeaderName, [MedicationHeaderName,
                nameof(MedicationAnalyzers.MedicationDetails.ExactMatch),
                nameof(MedicationAnalyzers.MedicationDetails.SimilarityScore), 
                nameof(MedicationAnalyzers.MedicationDetails.SimilarityScorePercentage), 
                nameof(MedicationAnalyzers.MedicationDetails.BestMatch), 
                nameof(MedicationAnalyzers.MedicationDetails.LevenshteinDistance), 
                nameof(MedicationAnalyzers.MedicationDetails.MatchType)]);
    }
}
