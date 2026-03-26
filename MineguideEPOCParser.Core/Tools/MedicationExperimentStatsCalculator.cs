using CsvHelper;
using CsvHelper.Configuration;
using MineguideEPOCParser.Core.Parsers.Configurations;
using MineguideEPOCParser.Core.Validation;

namespace MineguideEPOCParser.Core.Tools
{
    /// <summary>
    /// Represents the statistics for a medication extraction experiment.
    /// Supports two evaluation modes:
    /// 1. Strict: Only exact matches (TP) are considered correct. TP* (fuzzy matches) are treated as errors.
    ///    In this mode, a TP* effectively counts as both a False Positive (FP) (it predicted something incorrectly)
    ///    and a False Negative (FN) (it failed to perfectly extract a real entity).
    /// 2. Relaxed: Both exact (TP) and fuzzy (TP*) matches are considered correct.
    /// </summary>
    public class MedicationExperimentStats
    {
        public int TP { get; set; }
        public int TPStar { get; set; }
        /// <summary>
        /// Relaxed = Exact (<see cref="TP"/>) + Fuzzy (<see cref="TPStar"/>).
        /// </summary>
        public int TPRelaxed => TP + TPStar;
        public int FP { get; set; }
        public int FN { get; set; }
        public int Hallucinations { get; set; }

        public List<MedicationStatRow> Rows { get; set; } = [];

        /// <summary>
        /// Total number of predictions made by the model (TP + TP* + FP).
        /// <para>
        /// Note: TP* is included because in strict mode each TP* counts as BOTH one FP (the wrong value) and one FN (the correct value it didn't get),
        /// and in relaxed mode it counts as a TP.
        /// So in Predicted, we count them to count the FP part in strict mode and the TP part in relaxed mode.
        /// </para>
        /// </summary>
        public int Predicted => TP + TPStar + FP;

        /// <summary>
        /// Total number of actual entities present in the gold standard (TP + TP* + FN).
        /// <para>
        /// Note: TP* is included because in strict mode each TP* counts as BOTH one FP (the wrong value) and one FN (the correct value it didn't get),
        /// and in relaxed mode it counts as a TP.
        /// So in Actual, we count them to count the FN part in strict mode and the TP part in relaxed mode.
        /// </para>
        /// </summary>
        public int Actual => TP + TPStar + FN;

        public double StrictPrecision => CalculatePrecision(TP, Predicted);
        public double StrictRecall => CalculateRecall(TP, Actual);
        public double StrictF1Score => CalculateF1Score(StrictPrecision, StrictRecall);

        public double RelaxedPrecision => CalculatePrecision(TPRelaxed, Predicted);
        public double RelaxedRecall => CalculateRecall(TPRelaxed, Actual);
        public double RelaxedF1Score => CalculateF1Score(RelaxedPrecision, RelaxedRecall);

        public double Precision => RelaxedPrecision;
        public double Recall => RelaxedRecall;
        public double F1Score => RelaxedF1Score;

        private static double CalculatePrecision(int correct, int predicted) => predicted == 0 ? 0 : (double)correct / predicted;
        private static double CalculateRecall(int correct, int actual) => actual == 0 ? 0 : (double)correct / actual;
        private static double CalculateF1Score(double precision, double recall) => (precision + recall) == 0 ? 0 : 2 * (precision * recall) / (precision + recall);

        public override string ToString()
        {
            return $"TP: {TP}\n" +
                   $"TP*: {TPStar}\n" +
                   $"FP: {FP}\n" +
                   $"FN: {FN}\n" +
                   $"Hallucinations: {Hallucinations}\n" +
                   $"Strict Precision: {StrictPrecision:F4}\n" +
                   $"Strict Recall: {StrictRecall:F4}\n" +
                   $"Strict F1-Score: {StrictF1Score:F4}\n" +
                   $"Relaxed Precision: {RelaxedPrecision:F4}\n" +
                   $"Relaxed Recall: {RelaxedRecall:F4}\n" +
                   $"Relaxed F1-Score: {RelaxedF1Score:F4}";
        }
    }

    public class MedicationStatRow
    {
        public int ReportNumber { get; set; }
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

                int.TryParse(reportNumberStr, out int reportNumber);
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
                    ReportNumber = reportNumber,
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
