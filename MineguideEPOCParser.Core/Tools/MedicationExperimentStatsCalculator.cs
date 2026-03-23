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

        public List<MedicationStatRow> Rows { get; set; } = [];

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
                   $"Precision: {Precision:F4}\n" +
                   $"Recall: {Recall:F4}\n" +
                   $"F1-Score: {F1Score:F4}";
        }
    }

    public class MedicationStatRow
    {
        public string? ReportNumber { get; set; }
        public string? Medication { get; set; }
        public string? Result { get; set; }
        public int StartIndex { get; set; }
        public string? MatchInText { get; set; }
        public bool IsHallucination { get; set; }
    }

    public static class MedicationExperimentStatsCalculator
    {
        public class CalculatorConfiguration : MedicationManualValidatorParserConfigurationBase { }

        public static async Task<MedicationExperimentStats> CalculateStatsAsync(string csvPath, CalculatorConfiguration config, CancellationToken cancellationToken = default)
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
            var medicationHeader = config.MedicationHeaderName;
            var reportNumberHeader = config.ReportNumberHeaderName;

            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var resultStr = csv.GetField(experimentResultHeader);
                var medicationStr = csv.GetField(medicationHeader);
                var reportNumberStr = csv.GetField(reportNumberHeader);
                var startIndexStr = csv.GetField(startIndexHeader);
                var matchInTextStr = csv.GetField(matchInTextHeader);

                bool hasStartIndex = int.TryParse(startIndexStr, out int startIndex);
                bool isHallucination = false;

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
                            if (!hasStartIndex || startIndex < 0 || string.IsNullOrWhiteSpace(matchInTextStr))
                            {
                                stats.Hallucinations++;
                                isHallucination = true;
                            }
                            break;
                        case MedicationResult.ExperimentResultType.FN:
                            stats.FN++;
                            break;
                    }
                }

                stats.Rows.Add(new MedicationStatRow
                {
                    ReportNumber = reportNumberStr,
                    Medication = medicationStr,
                    Result = resultStr,
                    StartIndex = startIndex,
                    MatchInText = matchInTextStr,
                    IsHallucination = isHallucination
                });
            }

            return stats;
        }
    }
}
