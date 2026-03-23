using CsvHelper;
using CsvHelper.Configuration;
using MineguideEPOCParser.Core.Parsers.Configurations;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core.Parsers
{
    /// <summary>
    /// Takes the input file and randomly samples a specified number of rows.
    /// </summary>
    public class RandomSamplerParser : DataParser<RandomSamplerParserConfiguration>
    {
        private Random? _random;
        private HashSet<string>? _excludeReportNumbers = null; // To store report numbers to exclude if ExcludeFile is provided

        protected override void InitParsing()
        {
            _random = new Random(Configuration.Seed);
        }

        protected override async Task DoPreProcessing(CancellationToken cancellationToken = default)
        {
            if (Configuration.ExcludeFiles is null || Configuration.ExcludeFiles.Length == 0) return;

            Logger?.Information("Reading exclude files to build the set of report numbers to exclude from sampling...");

            _excludeReportNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var csvConfig = new CsvConfiguration(Configuration.Culture);
            
            foreach (var excludeFile in Configuration.ExcludeFiles)
            {
                using var reader = new StreamReader(excludeFile);
                using var csv = new CsvReader(reader, csvConfig);
                await foreach (var record in csv.GetRecordsAsync<dynamic>(cancellationToken))
                {
                    var dictRecord = (IDictionary<string, object>)record;
                    if (!dictRecord.TryGetValue(Configuration.ReportNumberHeaderName, out var reportNumberObj))
                    {
                        throw new InvalidOperationException($"The report number header '{Configuration.ReportNumberHeaderName}' was not found in the exclude file '{excludeFile}'. Cannot exclude rows.");
                    }
                    var reportNumber = reportNumberObj?.ToString();
                    if (!string.IsNullOrWhiteSpace(reportNumber))
                    {
                        _excludeReportNumbers.Add(reportNumber);
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Logger?.Information("Pre-processing was cancelled while reading exclude file '{ExcludeFile}'.", excludeFile);
                    break;
                }
            }

            Logger?.Information("Finished reading exclude files. Total report numbers to exclude: {ExcludeCount}.", _excludeReportNumbers.Count);
        }

        protected override async IAsyncEnumerable<string[]> ApplyTransformations(
            IAsyncEnumerable<string[]> rows,
            int inputTargetColumnIndex,
            string[] headers, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Use a dictionary to ensure unique keys efficiently
            var randomizedRows = new Dictionary<int, string[]>();

            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                if (_excludeReportNumbers != null)
                {
                    // Get the report number from the specified header
                    var reportNumberIndex = GetColumnIndex(headers, Configuration.ReportNumberHeaderName);

                    if (reportNumberIndex < 0)
                    {
                        throw new InvalidOperationException($"The report number header '{Configuration.ReportNumberHeaderName}' was not found in the input file. Cannot exclude rows.");
                    }

                    var reportNumber = row[reportNumberIndex];

                    if (_excludeReportNumbers.Contains(reportNumber))
                    {
                        Logger?.Information("Skipping report number {ReportNumber} as it is in the exclude list.", reportNumber);
                        continue; // Skip this row if the report number is in the exclude list
                    }
                }

                int key;
                do
                {
                    key = _random!.Next();
                } while (randomizedRows.ContainsKey(key)); // Fast lookup

                randomizedRows.Add(key, row);
            }

            // Sort by random key and take the sample
            foreach (var kvp in randomizedRows.OrderBy(t => t.Key).Take(Configuration.SampleSize))
            {
                yield return kvp.Value;
            }
        }
    }
}
