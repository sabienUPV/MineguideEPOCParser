using System.Text;

namespace MineguideEPOCParser.Core
{
    /// <summary>
    /// Transforms a JSON grouping file into a CSV mapper dictionary.
    /// CSV format: Include,Name,NewName
    /// (Include is always True)
    /// </summary>
    public class GroupingToMapperTransformer
    {
        public static async Task Transform(string inputGroupingFile, string outputMapperFile, CancellationToken cancellationToken = default)
        {
            var groups = await MedicationMapperGroupingParser.LoadGroups(inputGroupingFile, cancellationToken);

            if (groups == null)
            {
                throw new InvalidOperationException("The grouping file could not be loaded.");
            }

            var csv = new StringBuilder();
            csv.AppendLine("Include;Name;NewName");

            foreach (var group in groups)
            {
                foreach (var medication in group.Value)
                {
                    csv.AppendLine($"True;{medication};{group.Key}");
                }
            }

            await File.WriteAllTextAsync(outputMapperFile, csv.ToString(), cancellationToken);
        }
    }
}
