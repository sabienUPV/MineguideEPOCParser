using CsvHelper;
using CsvHelper.Configuration;
using MineguideEPOCParser.Core.Parsers.Configurations;
using MineguideEPOCParser.Core.Validation;

namespace MineguideEPOCParser.Core.Tools
{
    public enum ErrorType
    {
        /// <summary>
        /// a critical type of hallucination in which no medication in the original text can be inferred from it
        /// </summary>
        MorphologicalHallucination,
        /// <summary>
        /// A more benign type of hallucination in which the LLM introduced typos in the original text,
        /// but via Levenshtein we could still map it to the medication in the original text
        /// </summary>
        GenerativeTypo,
        SemanticAmbiguity,
        UnderExtraction,
        OverExtraction,
        EntityMerging
    }

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


        public int BoundaryErrors => UnderExtractions + OverExtractions + EntityMergingErrors;

        public int UnderExtractions { get; set; }
        public int OverExtractions { get; set; }
        public int EntityMergingErrors { get; set; }

        /// <summary>
        /// General error category in which the LLM produced something not present in the original text,
        /// and can be subcategorized into benign and mappable Generative typos, or critical morphological hallucinations. 
        /// </summary>
        public int MorphologicalAlterations => MorphologicalHallucinations + GenerativeTypos;

        public int MorphologicalHallucinations { get; set; }
        public int GenerativeTypos { get; set; }

        public int SemanticAmbiguities { get; set; } // Contextual Errors / Non-Pharmacological Entities (FP with match in text)

        /// <summary>
        /// Total number of errors for qualitative analysis (Boundary Errors + Hallucinations + Semantic Ambiguities).
        /// </summary>
        public int TotalErrorsForAnalysis => BoundaryErrors + MorphologicalAlterations + SemanticAmbiguities;

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
                   $"Morphological Alterations: {MorphologicalAlterations}\n" +
                   $"Morphological Hallucinations: {MorphologicalHallucinations}\n" +
                   $"Generative Typos: {GenerativeTypos}\n" +
                   $"Boundary Errors: {BoundaryErrors}\n" +
                   $"Under-extractions: {UnderExtractions}\n" +
                   $"Over-extractions: {OverExtractions}\n" +
                   $"Semantic Ambiguities: {SemanticAmbiguities}\n" +
                   $"Entity Merging Errors: {EntityMergingErrors}\n" +
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
        public string? CorrectedMedication { get; set; }
        public ErrorType? Error { get; set; }
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
            var correctedMedicationHeader = config.MatchCorrectedMedicationHeaderName;

            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var resultStr = csv.GetField(experimentResultHeader);
                var medicationStr = csv.GetField(medicationHeader);
                var reportNumberStr = csv.GetField(reportNumberHeader);
                var startIndexStr = csv.GetField(startIndexHeader);
                var matchInTextStr = csv.GetField(matchInTextHeader);
                
                csv.TryGetField(correctedMedicationHeader, out string? correctedMedicationStr);

                int.TryParse(reportNumberStr, out int reportNumber);
                bool hasStartIndex = int.TryParse(startIndexStr, out int startIndex);
                ErrorType? error = null;

                if (string.IsNullOrEmpty(resultStr)) continue;

                if (resultStr == MedicationResult.TPStar)
                {
                    stats.TPStar++;

                    // Boundary Errors logic
                    // (Mutually exclusive: can't be both under and over due to length check,
                    // and can't be entity merging at the same time since it's a different case)
                    if (!string.IsNullOrEmpty(correctedMedicationStr) && !string.IsNullOrEmpty(matchInTextStr))
                    {
                        bool correctionHasMultipleMedications = correctedMedicationStr.Contains(MedicationManualValidatorParserConfigurationBase.MultipleMedicationsSeparator);
                        var correctedMedications = correctionHasMultipleMedications
                            ? correctedMedicationStr.Split(MedicationManualValidatorParserConfigurationBase.MultipleMedicationsSeparator, StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).ToArray()
                            : null;

                        if (correctedMedications != null && correctedMedications.Length > 1 // You need at least 2 elements for an entity merging error to make sense
                            // Check that all included medications are part of the match in the text
                            // (we have it in the same if and not nested because if they are not, then we still need to check for other types of boundary errors)
                            // It can happen that they are not included if the user is normalizing an informal name that combines multiple medications
                            // in a way that we cannot fault the LLM for not knowing or normalizing since it is not the LLM's purpose
                            // (e.g. "triple terapia inhalatoria" means "salbutamol + budesonida + bromuro de ipratropio")
                            && correctedMedications.All(cm => matchInTextStr.Contains(cm, StringComparison.OrdinalIgnoreCase)))
                        {
                            stats.EntityMergingErrors++;
                            error = ErrorType.EntityMerging;
                        }
                        else if (correctedMedicationStr.Length > matchInTextStr.Length && correctedMedicationStr.Contains(matchInTextStr, StringComparison.OrdinalIgnoreCase))
                        {
                            stats.UnderExtractions++;
                            error = ErrorType.UnderExtraction;
                        }
                        else if (matchInTextStr.Length > correctedMedicationStr.Length && matchInTextStr.Contains(correctedMedicationStr, StringComparison.OrdinalIgnoreCase))
                        {
                            stats.OverExtractions++;
                            error = ErrorType.OverExtraction;
                        }
                    }

                    // If it's not any other error type, and the medication string (which is the LLM's output)
                    // is different from the match in text (which is what was actually extracted from the report),
                    // then it's a generative typo, a type of benign hallucination
                    // (since the LLM predicted something that is not in the text, but it's a TP* which means that we could match it to a medication in the text)
                    if (error is null && medicationStr is not null && !medicationStr.Equals(matchInTextStr, StringComparison.OrdinalIgnoreCase))
                    {
                        stats.GenerativeTypos++;
                        error = ErrorType.GenerativeTypo;
                    }
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
                            // Check for morphological hallucinations vs Semantic Ambiguity (Mutually exclusive)
                            if (!hasStartIndex || startIndex < 0 || string.IsNullOrWhiteSpace(matchInTextStr))
                            {
                                stats.MorphologicalHallucinations++;
                                error = ErrorType.MorphologicalHallucination;
                            }
                            else
                            {
                                stats.SemanticAmbiguities++;
                                error = ErrorType.SemanticAmbiguity;
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
                    CorrectedMedication = correctedMedicationStr,
                    Error = error
                });
            }

            return stats;
        }
    }
}
