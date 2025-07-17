using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MineguideEPOCParser.Core
{
    public class MedicationMapperGroupingParser : DataParser<MedicationMapperGroupingParserConfiguration>
    {
        public const string UnknownGroup = "UNK";
        public const char MedicationSeparator = '+';

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

        public static async Task<Dictionary<string, string[]>?> LoadGroups(string inputGroupingFile, CancellationToken cancellationToken = default)
        {
            using var jsonStream = File.OpenRead(inputGroupingFile);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, string[]>>(jsonStream, cancellationToken: cancellationToken);
        }

        protected static Dictionary<string, string> CreateNameToGroup(Dictionary<string, string[]> groups)
        {
            return groups.SelectMany(g => g.Value.Select(m => (m, g.Key))).ToDictionary(p => p.m, p => p.Key);
        }

        protected override async IAsyncEnumerable<string[]> ApplyTransformations(IAsyncEnumerable<string[]> rows, int inputTargetColumnIndex, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                // Get the medication name
                var t = row[inputTargetColumnIndex];

                // Split the medication name by "+" to handle multiple medications in a single cell
                var medications = t.Split(MedicationSeparator);

                // Get the group of each medication, and combine them with "+"
                var groups = string.Join(MedicationSeparator, medications.Select(m =>
                {
                    // Get the group of the medication.
                    // If the medication is not in a group, use "UNK" as the group name
                    return NameToGroup!.GetValueOrDefault(m, UnknownGroup);
                }));

                // Replace or add the group or groups to the row
                string[] newRow;
                if (Configuration.OverwriteInputTargetColumn)
                {
                    // If we are overwriting, replace the medication name(s) with the group name(s)
                    newRow = row.Select((value, index) => index == inputTargetColumnIndex ? groups : value).ToArray();
                }
                else
                {
                    // If we are not overwriting, add the group name(s) to the row
                    newRow = Utilities.ArrayCopyAndAdd(row, groups);
                }

                yield return newRow;
            }
        }
    }

    public class MedicationMapperGroupingParserConfiguration : DataParserConfiguration
    {
        public required string InputGroupingFile { get; set; }

        public const string ReplacementHeaderName = "NewName";
        protected override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (ReplacementHeaderName, [ReplacementHeaderName]);
    }
}
