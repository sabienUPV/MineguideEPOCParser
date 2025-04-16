using Newtonsoft.Json;
using System.Net;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    public class MedicationExtractingParser : DataParser<MedicationExtractingParserConfiguration>
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
                var medications = await ApiClient.CallToApi<MedicationsList>(t, "llama3.1:latest", Configuration.SystemPrompt, Logger, cancellationToken);

                if (medications == null)
                {
                    Logger?.Warning($"No medications found in the text: {t}");
                    continue;
                }

                if (!Configuration.OverwriteColumn)
                {
                    // In the 'T' column, replace the multiline original text with a single line text
                    // (only if we are not overwriting it)
                    t = t.Replace('\n', '\t').Replace("\r", "");
                }

                // Devuelve cada medicamento en una fila, ordenados alfabéticamente
                foreach (var medication in medications.Medicamentos.Order())
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
                        newRow[inputColumnIndex] = t;
                    }

                    yield return newRow;
                }
            }
        }
    }

    public class MedicationsList
    {
        [JsonRequired]
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

        public bool DecodeHtmlFromInput { get; set; }

        public string SystemPrompt { get; set; } = DefaultSystemPrompt;
    }
}
