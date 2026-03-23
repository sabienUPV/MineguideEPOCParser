using CsvHelper;
using CsvHelper.Configuration;
using MineguideEPOCParser.Core.Parsers.Configurations;
using MineguideEPOCParser.Core.Validation;

namespace MineguideEPOCParser.Core.Tools
{
    public class MedicationExperimentStats
    {
        public int TP { get; set; }
        public int TPStar { get; set; }
        public int FP { get; set; }
        public int FN { get; set; }
        public int Hallucinations { get; set; }

        public int Correct => TP + TPStar;
        public int Predicted => TP + TPStar + FP;
        public int Actual => TP + TPStar + FN;

        public double Precision => Predicted == 0 ? 0 : (double)Correct / Predicted;
        public double Recall => Actual == 0 ? 0 : (double)Correct / Actual;
        public double F1Score => (Precision + Recall) == 0 ? 0 : 2 * (Precision * Recall) / (Precision + Recall);

        public override string ToString()
        {
            return $"TP: {TP}\n" +
                   $"TP*: {TPStar}\n" +
                   $"FP: {FP}\n" +
                   $"FN: {FN}\n" +
                   $"Hallucinations: {Hallucinations}\n" +
                   $"Precision: {Precision:P2}\n" +
                   $"Recall: {Recall:P2}\n" +
                   $"F1-Score: {F1Score:P2}";
        }
    }

    public static class MedicationExperimentStatsCalculator
    {
        public static async Task<MedicationExperimentStats> CalculateStatsAsync(string csvPath, MedicationManualValidatorParserConfiguration config, CancellationToken cancellationToken = default)
        {
            var stats = new MedicationExperimentStats();

            var csvConfig = new CsvConfiguration(config.Culture)
            {
                HasHeaderRecord = true
            };
            
            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, csvConfig);

            await csv.ReadAsync();
            csv.ReadHeader();
            var headers = csv.HeaderRecord;

            if (headers == null)
            {
                throw new InvalidOperationException("The CSV file does not have a header record.");
            }

            var experimentResultHeader = config.MatchExperimentResultHeaderName;
            var startIndexHeader = config.MatchStartIndexHeaderName;
            var matchInTextHeader = config.MatchInTextHeaderName;

            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var resultStr = csv.GetField(experimentResultHeader);
                
                if (string.IsNullOrEmpty(resultStr)) continue;

                if (resultStr == MedicationResult.TPStar)
                {
                    stats.TPStar++;
                }
                else if (Enum.TryParse<MedicationResult.ExperimentResultType>(resultStr, out var resultType))
                {
                    switch (resultType)
                    {
                        case MedicationResult.ExperimentResultType.TP:
                            stats.TP++;
                            break;
                        case MedicationResult.ExperimentResultType.FP:
                            stats.FP++;
                            // Check for hallucinations
                            var startIndexStr = csv.GetField(startIndexHeader);
                            var matchInTextStr = csv.GetField(matchInTextHeader);
                            
                            if (startIndexStr == "-1" || string.IsNullOrWhiteSpace(matchInTextStr))
                            {
                                stats.Hallucinations++;
                            }
                            break;
                        case MedicationResult.ExperimentResultType.FN:
                            stats.FN++;
                            break;
                    }
                }
            }

            return stats;
        }
    }
}
