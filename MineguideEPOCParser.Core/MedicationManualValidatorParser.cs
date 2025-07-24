using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    /// <summary>
    /// Takes the input file, groups medications by the report number,
    /// yields to the validator control so the user can validate the medications and add or remove medications,
    /// and then returns the resulting rows with the validated medications.
    /// </summary>
    public class MedicationManualValidatorParser : DataParser<MedicationManualValidatorParserConfiguration>
    {
        public override int NumberOfOutputAdditionalColumns => 4; // StartIndex, Length, Text, OriginalMedication

        protected override async IAsyncEnumerable<string[]> ApplyTransformations(
            IAsyncEnumerable<string[]> rows,
            int inputTargetColumnIndex,
            string[] headers, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Get the report number from the specified header
            var reportNumberIndex = GetColumnIndex(headers, Configuration.ReportNumberHeaderName);

            if (reportNumberIndex < 0)
            {
                throw new InvalidOperationException($"The report number header '{Configuration.ReportNumberHeaderName}' was not found in the input file.");
            }

            var medicationIndex = GetColumnIndex(headers, Configuration.MedicationHeaderName);

            if (medicationIndex < 0)
            {
                throw new InvalidOperationException($"The medication header '{Configuration.MedicationHeaderName}' was not found in the input file.");
            }

            // Optimization: We can assume all rows from the same report number are grouped together
            string? currentReportNumber = null;
            List<string[]> currentReportRows = [];

            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                var reportNumberValue = row[reportNumberIndex];

                // If we are still in the same report, add the row to the current report rows
                if (currentReportNumber == null || currentReportNumber == reportNumberValue)
                {
                    currentReportRows.Add(row);
                    currentReportNumber = reportNumberValue;
                }
                else
                {
                    // We have reached a new report, yield the current report rows for validation
                    await foreach (var validatedRow in ValidateMedications(currentReportRows, inputTargetColumnIndex, medicationIndex, cancellationToken))
                    {
                        yield return validatedRow;
                    }

                    // Start a new report
                    currentReportRows.Clear();
                    currentReportRows.Add(row);
                    currentReportNumber = reportNumberValue;
                }
            }
        }

        private async IAsyncEnumerable<string[]> ValidateMedications(List<string[]> currentReportRows, int inputTargetColumnIndex, int medicationIndex, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Since the rows are grouped by report number, the report text should be the same for all rows,
            // so we can just take the first row to get the medication text.
            var medicationTextToValidate = currentReportRows[0][inputTargetColumnIndex];

            // Get all medication values from the duplicated report rows
            var medicationValues = currentReportRows.Select(r => r[medicationIndex]).ToArray();

            foreach (var validatedMedication in await Configuration.ValidationFunction(medicationTextToValidate, medicationValues, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var reportRow = currentReportRows[0];
                
                var newRow = Utilities.ArrayCopyAndAdd(
                    reportRow,
                    MedicationManualValidatorParserConfiguration.GetMedicationMatchValues(validatedMedication)
                );

                // Set the medication value in the new row to the extracted medication
                // (because we don't know if the order in the original rows is the same as the order in the medication values,
                // and also there could be new medications (FP, false positives) that were not in the original rows,
                // we are basing our row on the first row, and just replacing the medication value each time).
                newRow[medicationIndex] = validatedMedication.ExtractedMedication;

                // Yield the new row
                yield return newRow;
            }
        }
    }

    public class MedicationManualValidatorParserConfiguration : DataParserConfiguration
    {
        public const string DefaultReportNumberHeaderName = "Numero";

        public string ReportNumberHeaderName { get; set; } = DefaultReportNumberHeaderName;
        public string MedicationHeaderName { get; set; } = DefaultMedicationHeaderName;

        public required Func<string, string[], CancellationToken, Task<MedicationMatch[]>> ValidationFunction { get; set; }

        public string BuildMedicationHeader(string header) => $"{MedicationHeaderName}_{header}";

        protected override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (DefaultTHeaderName, [
            BuildMedicationHeader(nameof(MedicationMatch.StartIndex)),
            BuildMedicationHeader(nameof(MedicationMatch.Length)),
            BuildMedicationHeader(nameof(MedicationMatch.MatchInText)),
            BuildMedicationHeader(nameof(MedicationMatch.ExperimentResult))
        ]);

        // We do this here to ensure the order is preserved
        // in the same way as the headers (which are defined right above, in GetDefaultColumns).
        public static string[] GetMedicationMatchValues(MedicationMatch match)
        {
            return [
                match.StartIndex.ToString(),
                match.Length.ToString(),
                match.MatchInText,
                match.ExtractedMedication,
                match.ExperimentResult.ToResultString(),
            ];
        }
    }
}