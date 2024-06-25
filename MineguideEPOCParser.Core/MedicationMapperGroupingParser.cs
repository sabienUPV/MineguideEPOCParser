using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MineguideEPOCParser.Core
{
    public class MedicationMapperGroupingParser : MedicationParser<MedicationMapperGroupingParserConfiguration>
    {
        /// <summary>
        /// Original dictionary loaded from the JSON file.
        /// (Key: Group name, Value: Array of medication names)
        /// </summary>
        protected Dictionary<string, string[]>? Groups { get; set; }

        /// <summary>
        /// Optimized dictionary to search for the group of a medication.
        /// (Key: Medication name, Value: Group name)
        /// </summary>
        protected Dictionary<string, string>? NameToGroup { get; set; }

        protected override async Task DoPreProcessing(CancellationToken cancellationToken = default)
        {
            // Load dictionary from JSON file
            Groups = await LoadGroups(Configuration.InputGroupingFile, cancellationToken);

            if (Groups == null)
            {
                throw new InvalidOperationException("The grouping file could not be loaded.");
            }

            // Create an optimized dictionary to search for the group of a medication
            NameToGroup = CreateNameToGroup(Groups);
        }

        protected static async Task<Dictionary<string, string[]>?> LoadGroups(string inputGroupingFile, CancellationToken cancellationToken = default)
        {
            using var jsonStream = File.OpenRead(inputGroupingFile);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, string[]>>(jsonStream, cancellationToken: cancellationToken);
        }

        protected static Dictionary<string, string> CreateNameToGroup(Dictionary<string, string[]> groups)
        {
            return groups.SelectMany(g => g.Value.Select(m => (m, g.Key))).ToDictionary(p => p.m, p => p.Key);
        }

        protected override async IAsyncEnumerable<string[]> ApplyTransformations(IAsyncEnumerable<string[]> rows, int inputColumnIndex, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                // Get the medication name
                var t = row[inputColumnIndex];

                // Get the group of the medication
                var group = NameToGroup!.GetValueOrDefault(t);

                // If the medication is not in any group, return the row as is and continue
                if (group == null)
                {
                    yield return row;
                    continue;
                }

                // If the medication is in a group, replace the medication name with the group name
                var newRow = row.Select((value, index) => index == inputColumnIndex ? group : value).ToArray();

                yield return newRow;
            }
        }
    }

    public class MedicationMapperGroupingParserConfiguration : MedicationParserConfiguration
    {
        public required string InputGroupingFile { get; set; }

        public const string ReplacementHeaderName = "NewName";
        protected override (string inputHeader, string outputHeader) GetDefaultColumns() => (ReplacementHeaderName, ReplacementHeaderName);
    }
}
