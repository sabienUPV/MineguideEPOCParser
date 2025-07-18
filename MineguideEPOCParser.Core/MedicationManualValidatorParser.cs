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
        public override int NumberOfOutputAdditionalColumns => 0; // No additional output columns, just validating the input, possibly adding/removing rows

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
                    await foreach(var validatedRow in ValidateMedications(currentReportRows, inputTargetColumnIndex, medicationIndex, cancellationToken))
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

            foreach (var validatedMedication in await Configuration.ValidationFunction(medicationTextToValidate, medicationValues))
            {
                // Create a new row for each validated medication
                var newRow = new string[currentReportRows[0].Length];
                Array.Copy(currentReportRows[0], newRow, currentReportRows[0].Length);
                // Set the medication value in the new row
                newRow[medicationIndex] = validatedMedication;
                // Yield the new row
                yield return newRow;
            }
    }

    public class MedicationManualValidatorParserConfiguration : DataParserConfiguration
    {
        public const string DefaultReportNumberHeaderName = "Numero";

        public string ReportNumberHeaderName { get; set; } = DefaultReportNumberHeaderName;
        public string MedicationHeaderName { get; set; } = DefaultMedicationHeaderName;

        public required Func<string, string[], Task<string[]>> ValidationFunction { get; set; }

        protected override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (DefaultTHeaderName, []);
    }
}
