using CsvHelper.Configuration;
using System.Diagnostics.CodeAnalysis;
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

                // Also map the details columns
                var detailsColumns = MedicationAnalyzers.MedicationDetails.GetDetailsColumnsExceptMedication();
                Map(m => m.Details).Convert(convertFromStringFunction: data =>
                {
                    var medication = data.Row.GetField<string>(DataParserConfiguration.DefaultMedicationHeaderName)
                        ?? throw new InvalidOperationException($"The medication header '{DataParserConfiguration.DefaultMedicationHeaderName}' was not found in the input file. Cannot map the MedicationDetails without the medication name.");

                    var details = new MedicationAnalyzers.MedicationDetails() { Medication = medication };
                    foreach (var propertyName in detailsColumns)
                    {
                        // Use reflection to set the properties of the MedicationDetails object based on the column names and values from the CSV
                        var propertyInfo = typeof(MedicationAnalyzers.MedicationDetails).GetProperty(propertyName);
                        
                        if (propertyInfo == null || !propertyInfo.CanWrite) continue;

                        var value = data.Row.GetField(propertyInfo.PropertyType, propertyName);
                        propertyInfo.SetValue(details, value);
                    }

                    return details;
                });
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
            Report? currentReport = null;

            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                var reportNumberValue = row[reportNumberIndex];

                // If we have reached a new report number,
                // validate the current report rows, yield the results,
                // and start a new report
                if (currentReport is not null && currentReport.ReportNumber != reportNumberValue)
                {
                    // We have reached a new report, yield the current report rows for validation
                    await foreach (var validatedRow in ValidateMedications(currentReport.Rows, inputTargetColumnIndex, medicationIndex, currentReport.MedicationMatches, cancellationToken))
                    {
                        yield return validatedRow;
                    }

                    // Start a new report
                    currentReport = new Report(reportNumberValue, _hasMatchHeaders);
                }

                // If we don't have a current report, create a new one
                currentReport ??= new Report(reportNumberValue, _hasMatchHeaders);

                // Add the row to the current report rows
                currentReport.Rows.Add(row);

                if (_hasMatchHeaders)
                {
                    // If the medication matches are already present in the row,
                    // we can just get them from the row
                    // (we know the reader should be pointing to the current row,
                    // so we should be able to get the record directly).
                    currentReport.MedicationMatches?.Add(CurrentCsvReader!.GetRecord<MedicationMatch>());
                }
            }

            // Don't forget to yield the last report
            if (currentReport is not null)
            {
                await foreach (var validatedRow in ValidateMedications(currentReport.Rows, inputTargetColumnIndex, medicationIndex, currentReport.MedicationMatches, cancellationToken))
                {
                    yield return validatedRow;
                }
            }
        }

        public class Report
        {
            public required string ReportNumber { get; init; }
            public List<string[]> Rows { get; init; }
            public List<MedicationResult>? MedicationMatches { get; init; }

            [SetsRequiredMembers]
            public Report(string reportNumber, bool hasMatchHeaders)
            {
                ReportNumber = reportNumber;
                Rows = [];
                MedicationMatches = hasMatchHeaders ? [] : null;
            }
        }

        private async IAsyncEnumerable<string[]> ValidateMedications(List<string[]> currentReportRows, int inputTargetColumnIndex, int medicationIndex, List<MedicationResult>? existingMedicationMatches, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Since the rows are grouped by report number, the report text should be the same for all rows,
            // so we can just take the first row to get the medication text.
            var medicationTextToValidate = currentReportRows[0][inputTargetColumnIndex];

            // Classify the duplicated report rows by their medication name,
            // and also get all medication values to an array for validation.
            var medicationRows = currentReportRows
                .GroupBy(r => r[medicationIndex]) // Using GroupBy first allows graceful handling of duplicate medications (we just take the first occurrence)
                .ToDictionary(g => g.Key, g => g.First()); // ToDictionary usually throws an exception if there are duplicates, but since we are using GroupBy first, it will not throw.

            List<MedicationResult> medicationMatches;
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

                var medicationMatchValues = validatedMedication.GetMedicationMatchValues(Configuration.Culture);

                // We actually know that all columns after the medication column will be related to that medication,
                // so we just keep the columns before the medication column, add the medication column with the validated medication name,
                // and then add all the medication match values after that.

                // 1. Get the base row (either the existing one, or the first row of the report as a template)
                string[] baseRow = medicationRows.TryGetValue(validatedMedication.ExtractedMedication, out var existingRow)
                    ? existingRow
                    : currentReportRows[0];

                // 2. Create the correctly sized array: 
                // Base columns (up to medicationIndex) + The Medication Column (+2 because there is another column called InputRowNumber after that) + The fresh stats
                
                const int columnsAfterMedication = 1; // The InputRowNumber column that is added after the medication column

                int firstIndexOfDetails = Math.Min(medicationIndex + columnsAfterMedication + 1, baseRow.Length);

                string[] newRow = new string[firstIndexOfDetails + medicationMatchValues.Length];

                // 3. Copy the standard report data (everything UNTIL the medication details columns start)
                Array.Copy(baseRow, 0, newRow, 0, firstIndexOfDetails);

                // 4. Set the medication name (Crucial for new rows, harmlessly overwrites with the same text for existing rows)
                newRow[medicationIndex] = validatedMedication.ExtractedMedication;

                // 5. Graft the freshly calculated validation stats immediately after the InputRowNumber column (which is after the medication column)
                Array.Copy(medicationMatchValues, 0, newRow, firstIndexOfDetails, medicationMatchValues.Length);

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

        // This parser is special! And it is meant to be used with the output of the MedicationExtractingParser,
        // so we want to skip adding duplicate headers because we want to overwrite
        // the existing medication details columns with the new validated values, instead of adding new columns with duplicate header names.
        public override bool SkipDuplicateHeaders => true;

        // NOTE: DON'T MANUALLY SET THE OutputAdditionalHeaderNames for this config (publicly),
        // since we are not supporting that for detecting existing medication matches.

        // MedicationMatch header names
        public string MatchStartIndexHeaderName => BuildMedicationHeader(nameof(MedicationMatch.StartIndex));
        public string MatchLengthHeaderName => BuildMedicationHeader(nameof(MedicationMatch.Length));
        public string MatchInTextHeaderName => BuildMedicationHeader(nameof(MedicationMatch.MatchInText));
        public string MatchExperimentResultHeaderName => BuildMedicationHeader(nameof(MedicationResult.ExperimentResult));
        public string MatchCorrectedMedicationHeaderName => BuildMedicationHeader(nameof(MedicationResult.CorrectedMedication));


        public required Func<string, IEnumerable<MedicationResult>, CancellationToken, Task<MedicationResult[]>> ValidationFunction { get; set; }

        public string BuildMedicationHeader(string header) => $"{MedicationHeaderName}_{header}";

        // StartIndex, Length, MatchInText, ExtractedMedication, CorrectedMedication, + all the details columns except medication (since that would be redundant with the ExtractedMedication column)
        public override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (DefaultTHeaderName, [
            // The order is important here! We want the details columns to go first to match the columns from the input file (which comes from MedicationExtractingParser)
            ..MedicationAnalyzers.MedicationDetails.GetDetailsColumnsExceptMedication(),
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
    }
}
