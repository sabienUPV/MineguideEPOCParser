using CsvHelper.Configuration;
using CsvHelper;
using System.Globalization;

namespace MineguideEPOCParser.Core
{
    public static class SystemPromptUtils
    {
        public static List<SystemPromptObject> ParseFromCsvFile(string promptsFile, string cultureName)
        {
            var csvConfig = new CsvConfiguration(new CultureInfo(cultureName));

            using var reader = new StreamReader(promptsFile);
            using var csvReader = new CsvReader(reader, csvConfig);

            return csvReader.GetRecords<SystemPromptObject>().ToList();
        }

        public class SystemPromptObject
        {
            public required string SystemPrompt { get; set; }
            public required string Format { get; set; }

            public bool UsesJsonFormat => !string.IsNullOrEmpty(Format) && Format.Equals("json", StringComparison.OrdinalIgnoreCase);
        }
    }
}
