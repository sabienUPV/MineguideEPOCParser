using CsvHelper.Configuration;
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
        public override int NumberOfOutputAdditionalColumns => 5; // StartIndex, Length, MatchInText, ExtractedMedication, CorrectedMedication

        public class MedicationMatchClassMap : ClassMap<MedicationMatch>
        {
            public MedicationMatchClassMap(MedicationManualValidatorParserConfiguration config)
            {
                // Map all MedicationMatch properties to the output columns
                Map(m => m.ExtractedMedication).Name(config.MedicationHeaderName);

                Map(m => m.StartIndex).Name(config.MatchStartIndexHeaderName);
                Map(m => m.Length).Name(config.MatchLengthHeaderName);
                Map(m => m.MatchInText).Name(config.MatchInTextHeaderName);
                Map(m => m.ExperimentResult).Name(config.MatchExperimentResultHeaderName);

                Map(m => m.CorrectedMedication).Name(config.MatchCorrectedMedicationHeaderName).Optional();
            }
        }

        private bool _hasMatchHeaders = false;
        protected override void ValidateHeaders(string[] headers)
        {
            base.ValidateHeaders(headers);

            // Check if the required match headers are present in the input headers
            if (Configuration.GetRequiredMedicationMatchHeaders().All(h => headers.Contains(h)))
            {
                // It already has the match information,
                // so we can just extract it from the CSV
                _hasMatchHeaders = true;

                // Register the MedicationMatch class map to the CsvReader
                var classMap = new MedicationMatchClassMap(Configuration);
                CurrentCsvReader?.Context.RegisterClassMap(classMap);
            }
            else
            {
                _hasMatchHeaders = false;
            }
        }

        protected override void CleanupParsing()
        {
            base.CleanupParsing();
            _hasMatchHeaders = false; // Reset the flag for the next parsing operation
        }

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
            List<MedicationMatch>? existingMedicationMatches = _hasMatchHeaders ? [] : null;

            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                var reportNumberValue = row[reportNumberIndex];

                // If we are still in the same report, add the row to the current report rows
                if (currentReportNumber == null || currentReportNumber == reportNumberValue)
                {
                    currentReportRows.Add(row);
                    currentReportNumber = reportNumberValue;

                    if (_hasMatchHeaders)
                    {
                        // If the medication matches are already present in the row,
                        // we can just get them from the row
                        // (we know the reader should be pointing to the current row,
                        // so we should be able to get the record directly).
                        existingMedicationMatches?.Add(CurrentCsvReader!.GetRecord<MedicationMatch>());
                    }
                }
                else
                {
                    // We have reached a new report, yield the current report rows for validation
                    await foreach (var validatedRow in ValidateMedications(currentReportRows, inputTargetColumnIndex, medicationIndex, existingMedicationMatches, cancellationToken))
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

        private async IAsyncEnumerable<string[]> ValidateMedications(List<string[]> currentReportRows, int inputTargetColumnIndex, int medicationIndex, List<MedicationMatch>? existingMedicationMatches, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Since the rows are grouped by report number, the report text should be the same for all rows,
            // so we can just take the first row to get the medication text.
            var medicationTextToValidate = currentReportRows[0][inputTargetColumnIndex];

            // Classify the duplicated report rows by their medication name,
            // and also get all medication values to an array for validation.
            var medicationRows = currentReportRows
                .GroupBy(r => r[medicationIndex]) // Using GroupBy first allows graceful handling of duplicate medications (we just take the first occurrence)
                .ToDictionary(g => g.Key, g => g.First()); // ToDictionary usually throws an exception if there are duplicates, but since we are using GroupBy first, it will not throw.

            List<MedicationMatch> medicationMatches;
            if (existingMedicationMatches is not null)
            {
                medicationMatches = existingMedicationMatches;
            }
            else
            {
                var medicationValues = medicationRows.Keys.ToArray();
                medicationMatches = MedicationMatchHelper.FindAllMedicationMatchesBySimilarity(medicationTextToValidate, medicationValues);
            }

            foreach (var validatedMedication in await Configuration.ValidationFunction(medicationTextToValidate, medicationMatches, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var medicationMatchValues = MedicationManualValidatorParserConfiguration.GetMedicationMatchValues(validatedMedication);

                string[] reportRow, newRow;
                if (medicationRows.TryGetValue(validatedMedication.ExtractedMedication, out var existingReportRow))
                {
                    // If the medication was found in the original rows, use that row
                    // (this is important to preserve the original row's values for other columns).
                    reportRow = existingReportRow;
                    newRow = Utilities.ArrayCopyAndAdd(reportRow, medicationMatchValues);
                }
                else
                {
                    // If the medication was not found in the original rows, we will create a new row
                    // based on the first row of the current report.
                    reportRow = currentReportRows[0];

                    // We actually know that all columns after the medication column will be related to that medication,
                    // and since we are creating a new "medication", we should actually set those columns to empty,
                    // and add our columns.

                    // Set the first columns (up to the medication index) to the original row values
                    // (since they are common to the same report)
                    newRow = new string[reportRow.Length + medicationMatchValues.Length];
                    Array.Copy(reportRow, 0, newRow, 0, medicationIndex);

                    // Set the medication name
                    newRow[medicationIndex] = validatedMedication.ExtractedMedication;

                    // Set the rest of the columns to empty
                    Array.Fill(newRow, string.Empty, medicationIndex + 1, reportRow.Length - medicationIndex - 1);

                    // Add the medication match values at the end
                    Array.Copy(medicationMatchValues, 0, newRow, reportRow.Length, medicationMatchValues.Length);
                }

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

        // NOTE: DON'T MANUALLY SET THE OutputAdditionalHeaderNames for this config (publicly),
        // since we are not supporting that for detecting existing medication matches.

        // MedicationMatch header names
        public string MatchStartIndexHeaderName => BuildMedicationHeader(nameof(MedicationMatch.StartIndex));
        public string MatchLengthHeaderName => BuildMedicationHeader(nameof(MedicationMatch.Length));
        public string MatchInTextHeaderName => BuildMedicationHeader(nameof(MedicationMatch.MatchInText));
        public string MatchExperimentResultHeaderName => BuildMedicationHeader(nameof(MedicationMatch.ExperimentResult));
        public string MatchCorrectedMedicationHeaderName => BuildMedicationHeader(nameof(MedicationMatch.CorrectedMedication));


        public required Func<string, IEnumerable<MedicationMatch>, CancellationToken, Task<MedicationMatch[]>> ValidationFunction { get; set; }

        public string BuildMedicationHeader(string header) => $"{MedicationHeaderName}_{header}";

        protected override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (DefaultTHeaderName, [
            MatchStartIndexHeaderName,
            MatchLengthHeaderName,
            MatchInTextHeaderName,
            MatchExperimentResultHeaderName,
            MatchCorrectedMedicationHeaderName
        ]);

        public string[] GetRequiredMedicationMatchHeaders()
        {
            // Return the headers that are required for the medication matches
            return [
                MatchStartIndexHeaderName,
                MatchLengthHeaderName,
                MatchInTextHeaderName,
                MatchExperimentResultHeaderName,
                //MatchCorrectedMedicationHeaderName // This one is optional, so we purposefully don't include it here
            ];
        }

        // We do this here to ensure the order is preserved
        // in the same way as the headers (which are defined right above, in GetDefaultColumns).
        public static string[] GetMedicationMatchValues(MedicationMatch match)
        {
            return [
                match.StartIndex.ToString(),
                match.Length.ToString(),
                match.MatchInText,
                match.ExperimentResult.ToResultString(),
                match.CorrectedMedication ?? string.Empty
            ];
        }
    }
}