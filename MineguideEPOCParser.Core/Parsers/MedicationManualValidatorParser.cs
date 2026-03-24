using CsvHelper;
using CsvHelper.Configuration;
using MineguideEPOCParser.Core.Parsers.Configurations;
using MineguideEPOCParser.Core.Utils;
using MineguideEPOCParser.Core.Validation;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core.Parsers
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

                        object? value;
                        try
                        {
                            // Attempt to get the value from the CSV and convert it to the correct type
                            value = data.Row.GetField(propertyInfo.PropertyType, propertyName);
                        }
                        catch
                        {
                            // If there's an error (e.g., missing column, conversion failure),
                            // we drop the entire details object to avoid partial/incorrect data.
                            // This works because if the details are missing, the validator will just recalculate them.
                            return null;
                        }

                        propertyInfo.SetValue(details, value);
                    }

                    return details;
                }).Optional();
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
            _recoveredResults = null; // Clear recovered results
        }

        private Dictionary<string, MedicationResult[]>? _recoveredResults;

        protected override async Task DoPreProcessing(CancellationToken cancellationToken)
        {
            await base.DoPreProcessing(cancellationToken);

            // --- 0. SESSION RESUMPTION & REVIEW MODE ---
            // This logic allows the parser to act as an editor for previously generated files.
            // If a JSON checkpoint (the current session) isn't found, we attempt to "hydrate" 
            // the validation history from an existing CSV output or its .tmp counterpart.
            var checkpointPath = Configuration.OutputFile + ".json";
            if (!File.Exists(checkpointPath))
            {
                var filesToTry = new[] { Configuration.OutputFile, Configuration.OutputFile + ".tmp" };
                foreach (var file in filesToTry)
                {
                    if (File.Exists(file))
                    {
                        var recovered = await RecoverResultsFromCsv(file, cancellationToken);
                        if (recovered.Count > 0)
                        {
                            _recoveredResults = recovered;
                            Logger?.Information("Review Mode: Loaded {Count} report results from existing file: {FilePath}", recovered.Count, file);
                            break;
                        }
                    }
                }
            }
        }

        private async Task<Dictionary<string, MedicationResult[]>> RecoverResultsFromCsv(string filePath, CancellationToken cancellationToken)
        {
            var results = new Dictionary<string, List<MedicationResult>>();
            try
            {
                using var reader = new StreamReader(filePath);
                var csvConfig = new CsvConfiguration(Configuration.Culture)
                {
                    HeaderValidated = null,
                    MissingFieldFound = null,
                };
                using var csv = new CsvReader(reader, csvConfig);

                var classMap = new MedicationMatchClassMap(Configuration);
                csv.Context.RegisterClassMap(classMap);

                if (await csv.ReadAsync() && csv.ReadHeader())
                {
                    while (await csv.ReadAsync())
                    {
                        var reportNumber = csv.GetField<string>(Configuration.ReportNumberHeaderName);
                        if (string.IsNullOrEmpty(reportNumber)) continue;

                        var match = csv.GetRecord<MedicationMatch>();

                        if (!results.TryGetValue(reportNumber, out var list))
                        {
                            list = new List<MedicationResult>();
                            results[reportNumber] = list;
                        }

                        // Ensure we only store one result per unique medication name in a report
                        if (!list.Any(m => m.ExtractedMedication == match.ExtractedMedication))
                        {
                            list.Add(match);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Failed to recover results from {FilePath}. The file might be corrupted or in an incompatible format.", filePath);
            }

            return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
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

            // --- 1. PRE-LOAD AND GROUP REPORTS ---
            // Optimization: We can assume all rows from the same report number are grouped together.
            // We gather all reports into a list first to support the navigation loop (Go Back).
            var reports = new List<Report>();
            Report? currentReport = null;

            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                var reportNumberValue = row[reportNumberIndex];

                // If we have reached a new report number, start a new report container
                if (currentReport is not null && currentReport.ReportNumber != reportNumberValue)
                {
                    reports.Add(currentReport);
                    currentReport = new Report(reportNumberValue);
                }

                // If we don't have a current report, create a new one
                currentReport ??= new Report(reportNumberValue);

                // Add the row to the current report rows
                currentReport.Rows.Add(row);

                if (_hasMatchHeaders)
                {
                    // If the medication matches are already present in the row,
                    // we can just get them from the row (the reader points to the current record).
                    try
                    {
                        var match = CurrentCsvReader!.GetRecord<MedicationMatch>();
                        currentReport.AddMatch(match);
                    }
                    catch
                    {
                        // For unfinished files, some rows might not have the match information yet, so we just skip them.
                    }
                }
            }

            // Don't forget to add the last report
            if (currentReport is not null)
            {
                reports.Add(currentReport);
            }

            if (reports.Count == 0) yield break;

            // --- 2. PROGRESS CHECKPOINT RECOVERY ---
            // Load progress from a sidecar JSON file if it exists to prevent losing work on crash/exit.
            var checkpointPath = Configuration.OutputFile + ".json";
            var checkpointSerializationSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new KnownTypesBinder
                {
                    KnownTypes = [typeof(MedicationResult), typeof(MedicationMatch)]
                },
                Formatting = Formatting.Indented
            };
            var resultsHistory = new Dictionary<string, MedicationResult[]>();
            if (File.Exists(checkpointPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(checkpointPath, cancellationToken);
                    resultsHistory = JsonConvert.DeserializeObject<Dictionary<string, MedicationResult[]>>(json, checkpointSerializationSettings) ?? [];
                    Logger?.Information("Loaded progress from checkpoint: {CheckpointPath}", checkpointPath);
                }
                catch (Exception ex)
                {
                    Logger?.Warning(ex, "Failed to load checkpoint file {CheckpointPath}. Starting from scratch.", checkpointPath);
                }
            }
            else if (_recoveredResults != null)
            {
                // Fallback to results recovered from CSV during PreProcessing
                resultsHistory = _recoveredResults;
                _recoveredResults = null; // Free memory
            }

            // --- 3. NAVIGATION & VALIDATION LOOP ---
            int i = 0;

            // Smart Resumption: If we have history, skip ahead to the first report that hasn't been 
            // validated yet to avoid redundant work. If the file is 100% complete, we start from 
            // the beginning to allow for a full review/edit of the data.
            if (resultsHistory.Count > 0)
            {
                while (i < reports.Count
                    && (reports[i].MedicationMatches is not null
                        || resultsHistory.ContainsKey(reports[i].ReportNumber)))
                {
                    i++;
                }
                
                // If everything was already validated, we assume the user wants to review from the start.
                if (i == reports.Count) i = 0;
                
                Logger?.Information("Resuming/Reviewing validation from report index {Index} ({ReportNumber})", i, reports[i].ReportNumber);
            }

            while (i < reports.Count)
            {
                var report = reports[i];
                
                // Update progress bar in the GUI
                Progress?.Report(new ProgressValue { Value = (double)i / reports.Count, RowsProcessed = i });

                // Prepare initial matches for validation: check history first, then existing headers, then auto-discovery
                List<MedicationResult> initialMatches;
                if (resultsHistory.TryGetValue(report.ReportNumber, out var previousResults))
                {
                    initialMatches = previousResults.ToList();
                }
                else if (report.MedicationMatches is not null)
                {
                    initialMatches = report.MedicationMatches;
                }
                else
                {
                    // Since rows are grouped, take the first row's target column as the text to validate.
                    var medicationTextToValidate = report.Rows[0][inputTargetColumnIndex];
                    var medicationValues = report.Rows.Select(r => r[medicationIndex]).Distinct().ToArray();
                    initialMatches = MedicationMatchHelper.FindAllMedicationMatchesBySimilarity(medicationTextToValidate, medicationValues);
                }

                // Yield control to the GUI for user validation
                var validationResult = await Configuration.ValidationFunction(report.Rows[0][inputTargetColumnIndex], initialMatches, cancellationToken);

                // Handle Navigation
                if (validationResult.Direction == NavigationDirection.Back)
                {
                    i = Math.Max(0, i - 1);
                    continue;
                }
                else if (validationResult.Direction == NavigationDirection.Stop)
                {
                    break;
                }

                // Save results to memory and update the checkpoint file
                resultsHistory[report.ReportNumber] = validationResult.Results;
                
                try
                {
                    var json = JsonConvert.SerializeObject(resultsHistory, checkpointSerializationSettings);
                    await File.WriteAllTextAsync(checkpointPath, json, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "Failed to save progress checkpoint.");
                }

                i++;
            }

            // --- 4. DATA RECONSTRUCTION & YIELD ---
            // Now that validation is finished (or stopped), we reconstruct the CSV rows.
            // We iterate through ALL reports to ensure the output file is complete and 
            // can be used to "Continue" later without having lost any data.
            foreach (var report in reports)
            {
                if (resultsHistory.TryGetValue(report.ReportNumber, out var validatedResults))
                {
                    // Yield validated rows (either from this session, checkpoint, or review mode)
                    foreach (var row in GenerateValidatedRows(report, validatedResults, medicationIndex))
                    {
                        yield return row;
                    }
                }
                else
                {
                    // If the report wasn't validated, yield its original rows.
                    // We must ensure the column count matches the expected output to avoid shifting.
                    foreach (var row in report.Rows)
                    {
                        yield return PadRowToMatchOutputHeaders(row, medicationIndex);
                    }
                }
            }
            
            // Clean up checkpoint file on successful completion
            if (File.Exists(checkpointPath))
            {
                File.Delete(checkpointPath);
            }
        }

        private int GetFirstIndexOfDetails(string[] baseRow, int medicationIndex)
        {
            const int columnsAfterMedication = 1; // The InputRowNumber column added after the medication column
            return Math.Min(medicationIndex + columnsAfterMedication + 1, baseRow.Length);
        }

        private string[] PadRowToMatchOutputHeaders(string[] rawRow, int medicationIndex)
        {
            // We use the configured additional header count to ensure structural consistency
            int extraColumnCount = Configuration.OutputAdditionalHeaderNames.Length;
            
            int firstIndexOfDetails = GetFirstIndexOfDetails(rawRow, medicationIndex);
            
            // Create a new row with the correct total size
            string[] newRow = new string[firstIndexOfDetails + extraColumnCount];
            
            // Copy the standard report data
            Array.Copy(rawRow, 0, newRow, 0, Math.Min(rawRow.Length, firstIndexOfDetails));
            
            // The rest will remain as null/empty strings, which is exactly what we want 
            // for unvalidated data.
            
            return newRow;
        }

        public class Report
        {
            public required string ReportNumber { get; init; }
            public List<string[]> Rows { get; init; }
            public List<MedicationResult>? MedicationMatches { get; private set; }

            [SetsRequiredMembers]
            public Report(string reportNumber)
            {
                ReportNumber = reportNumber;
                Rows = [];
                // MedicationMatches left to null on purpose
            }

            public void AddMatch(MedicationResult match)
            {
                if (MedicationMatches is null) MedicationMatches = [match];
                else MedicationMatches.Add(match);
            }
        }

        private IEnumerable<string[]> GenerateValidatedRows(Report report, MedicationResult[] validatedResults, int medicationIndex)
        {
            // Classify the duplicated report rows by their medication name.
            // Using GroupBy allows graceful handling of duplicates (we just take the first occurrence).
            var medicationRows = report.Rows
                .GroupBy(r => r[medicationIndex])
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var validatedMedication in validatedResults)
            {
                var medicationMatchValues = validatedMedication.GetMedicationMatchValues(Configuration.Culture);

                // We know that all columns after the medication column are related to that medication.
                // We keep the columns before, add the validated medication name, then add the fresh stats.

                // 1. Get the base row (either the existing one, or the first row of the report as a template)
                string[] baseRow = medicationRows.TryGetValue(validatedMedication.ExtractedMedication, out var existingRow)
                    ? existingRow
                    : report.Rows[0];

                // 2. Create the correctly sized array: 
                // Base columns (up to medicationIndex) + The Medication Column (+1 for InputRowNumber) + The fresh stats
                int firstIndexOfDetails = GetFirstIndexOfDetails(baseRow, medicationIndex);
                string[] newRow = new string[firstIndexOfDetails + medicationMatchValues.Length];

                // 3. Copy the standard report data (everything UNTIL the medication details columns start)
                Array.Copy(baseRow, 0, newRow, 0, firstIndexOfDetails);

                // 4. Set the medication name (overwrites or sets for new rows)
                newRow[medicationIndex] = validatedMedication.ExtractedMedication;

                // 5. Insert the freshly calculated validation stats immediately after the InputRowNumber column
                Array.Copy(medicationMatchValues, 0, newRow, firstIndexOfDetails, medicationMatchValues.Length);

                yield return newRow;
            }
        }
    }
}
