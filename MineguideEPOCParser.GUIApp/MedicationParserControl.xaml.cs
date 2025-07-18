using Microsoft.Win32;
using MineguideEPOCParser.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace MineguideEPOCParser.GUIApp
{
    /// <summary>
    /// Lógica de interacción para MedicationParserControl.xaml
    /// </summary>
    public sealed partial class MedicationParserControl : UserControl, IAsyncDisposable
    {
        // Dependency property IsParsing
        public static readonly DependencyProperty IsParsingProperty = DependencyProperty.Register(
            nameof(IsParsing),
            typeof(bool),
            typeof(MedicationParserControl),
            new PropertyMetadata(false)
        );

        // Dependency property IsNotParsing
        public static readonly DependencyProperty IsNotParsingProperty = DependencyProperty.Register(
            nameof(IsNotParsing),
            typeof(bool),
            typeof(MedicationParserControl),
            new PropertyMetadata(true)
        );

        public bool IsParsing
        {
            get => (bool)GetValue(IsParsingProperty);
            private set
            {
                SetValue(IsParsingProperty, value);
                SetValue(IsNotParsingProperty, !value);
            }
        }

        public bool IsNotParsing
        {
            get => (bool)GetValue(IsNotParsingProperty);
            private set
            {
                SetValue(IsNotParsingProperty, value);
                SetValue(IsParsingProperty, !value);
            }
        }

        // Input/Output files
        private const char FileInTextSeparator = ';';
        private string[] InputFiles => InputFileTextBox.Text.Split(FileInTextSeparator, StringSplitOptions.RemoveEmptyEntries);
        private string[] OutputFiles => OutputFileTextBox.Text.Split(FileInTextSeparator, StringSplitOptions.RemoveEmptyEntries);

        // Parser
        private MedicationExtractingParser? MedicationParser { get; set; }

        // Timer
        private DispatcherTimer? _dispatcherTimer;
        private TimeSpan _elapsedTime = TimeSpan.Zero;

        // Cancelling
        private CancellationTokenSource? CancellationTokenSource { get; set; }

        // Logging
        private ILogger? ExecutionLogger { get; set; }
        private LoggingLevelSwitch? LoggingLevelSwitch { get; set; }

        // Progress reporting
        private Progress<ProgressValue>? Progress { get; set; }

        public MedicationParserControl()
        {
            InitializeComponent();

            // Create a new progress object
            CreateProgress();

#if DEBUG
            // Setup test autocompletions
            SetupTestAutocompletions();
#endif
        }

        /// <summary>
        /// Starts a timer while parsing
        /// </summary>
        private void StartTimer()
        {
            _dispatcherTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1),
            };

            void DispatcherTimerTick(object? sender, EventArgs e)
            {
                _elapsedTime = _elapsedTime.Add(TimeSpan.FromSeconds(1));
                UpdateTimerTextBlock(isAlwaysRunningInUIThread: true); // Using DispatcherTimer ensures us that we are in the UI thread
            }

            _dispatcherTimer.Tick += DispatcherTimerTick;

            _dispatcherTimer.Start();
        }

        private void UpdateTimerTextBlock(bool isAlwaysRunningInUIThread = false)
        {
            PerformNonCriticalUIAction(() => TimerTextBlock.Text = _elapsedTime.ToString(@"hh\:mm\:ss"),
                "Error updating timer text block in UI", isAlwaysRunningInUIThread);
        }

        private static string GetExecutionBaseNameFromTimestamp(string timestamp) => $"{timestamp}_MineguideEPOCParser_output";

        private const string LogsFolderName = "logs";
        private Logger CreateExecutionLogger(string timestamp, string outputFolder)
        {
            string executionLogFolder = Path.Combine(outputFolder, LogsFolderName);
            string executionLogBaseName = GetExecutionBaseNameFromTimestamp(timestamp);

            // Create execution-specific log directory
            Directory.CreateDirectory(executionLogFolder);

            // Create log level switch for RichTextBox
            LoggingLevelSwitch ??= new LoggingLevelSwitch
            {
                // Take the default log level from the control
                MinimumLevel = GetLogLevelFromComboBox()
            };

            // Create a new logger
            var baseLogger = new LoggerConfiguration()
                .MinimumLevel.Verbose() // always log verbose to file
                .Enrich.WithProperty("ExecutionTimestamp", timestamp)
                // Central JSON log for this specific execution
                .WriteTo.File(new CompactJsonFormatter(),
                             Path.Combine(executionLogFolder, $"{executionLogBaseName}.json"))
                // Execution-wide text log for quick viewing
                .WriteTo.File(Path.Combine(executionLogFolder, $"{executionLogBaseName}.log"))
                .WriteTo.RichTextBox(LogRichTextBox, levelSwitch: LoggingLevelSwitch) // UI logging
                .CreateLogger();

            return baseLogger;
        }

        private static Logger CreateFileLogger(string executionTimestamp, ILogger baseLogger, string inputFile, string outputFolder)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Get input file name without extension
            var inputFileName = Path.GetFileNameWithoutExtension(inputFile);

            string executionFileLogBaseName = $"{timestamp}_output_{inputFileName}";
            string executionFileLogFolder = Path.Combine(outputFolder, LogsFolderName, executionFileLogBaseName);

            // Create file-specific log directory for this execution
            Directory.CreateDirectory(executionFileLogFolder);

            // Create a new logger
            var fileLogger = new LoggerConfiguration()
                .MinimumLevel.Verbose() // always log verbose
                .Enrich.WithProperty("ExecutionTimestamp", executionTimestamp)
                .Enrich.WithProperty("FileExecutionTimestamp", timestamp)
                .Enrich.WithProperty("InputFile", inputFile)
                .Enrich.WithProperty("InputFileName", inputFileName)
                .Enrich.WithProperty("OutputFolder", outputFolder)
                // File-specific JSON log for analysis
                .WriteTo.File(new CompactJsonFormatter(),
                             Path.Combine(executionFileLogFolder, $"{executionFileLogBaseName}.json"))
                // File-specific text log for quick viewing
                .WriteTo.File(Path.Combine(executionFileLogFolder, $"{executionFileLogBaseName}.log"))
                .WriteTo.Logger(baseLogger) // Also write to the base logger (this passes up all properties too)
                .CreateLogger();

            return fileLogger;
        }

        private LogEventLevel GetLogLevelFromComboBox()
        {
            if (LogLevelComboBox.SelectedItem is not ComboBoxItem selectedItem
                || selectedItem?.Content is not string selectedItemContent
                || !Enum.TryParse<LogEventLevel>(selectedItemContent, out var logLevel))
            {
                // Fallback default log level
                return LogEventLevel.Warning;
            }

            return logLevel;
        }

        /// <summary>
        /// Create a new progress object that updates the progress bar
        /// </summary>
        private void CreateProgress()
        {
            Progress = new Progress<ProgressValue>(value =>
            {
                PerformNonCriticalUIAction(() =>
                {
                    // Value is between 0 and 1, so multiply by 100 to get percentage
                    var percentage = value.Value * 100;

                    // Update the progress bar
                    ProgressBar.Value = percentage;

                    // Update the progress percentage text
                    ProgressPercentageTextBlock.Text = $"{percentage:0.00}%";

                    // Update the progress rows processed text
                    if (value.RowsProcessed.HasValue)
                    {
                        ProgressRowsProcessedTextBlock.Text = $"Rows processed: {value.RowsProcessed}";
                    }
                }, "Error updating progress in UI");
            });
        }

        /// <summary>
        /// Perform a non-critical UI action, such as updating a label or a progress bar, so that if it fails the parsing execution does not stop.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="exceptionLogMessage"></param>
        /// <param name="isAlwaysRunningInUIThread">
        /// false by default. If true, we know that this code will ALWAYS be run from the UI thread,
        /// which means that we don't need to invoke it from the Dispatcher. Only set to true if you know that for sure.
        /// </param>
        private void PerformNonCriticalUIAction(Action action, string exceptionLogMessage, bool isAlwaysRunningInUIThread = false)
        {
            UIUtilities.PerformNonCriticalUIAction(this, action, ExecutionLogger, exceptionLogMessage, isAlwaysRunningInUIThread);
        }

        private bool _isDisposed = true;
        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;

            await DisposeExecutionResourcesAsync();
            _isDisposed = true;
        }

        private async ValueTask DisposeExecutionResourcesAsync()
        {
            // Dispose the cancellation token source
            CancellationTokenSource?.Dispose();

            // Set the cancellation token source to null
            CancellationTokenSource = null;

            // Stop the timer and set it to null (needs to be called from UI thread)
            PerformNonCriticalUIAction(() =>
            {
                _dispatcherTimer?.Stop();
                _dispatcherTimer = null;
            }, "Error trying to stop the UI timer while disposing it");

            // Set the parser to null
            MedicationParser = null;

            // Dispose the execution logger
            await DisposeExecutionLoggerAsync();
        }

        private async ValueTask DisposeExecutionLoggerAsync()
        {
            // We save the execution logger first
            // to a local variable and set the property to null
            // so that, if disposing it throws an Exception,
            // the DisposeLoggerAsync method doesn't try to log to the ExecutionLogger
            // while being partially disposed
            var executionLogger = ExecutionLogger;

            // If the execution logger is already null, we don't need to dispose it
            if (executionLogger is null) return;

            // Set the execution logger to null to make sure no one tries to call it while disposing (e.g. in the DisposeLoggerAsync method)
            ExecutionLogger = null;

            // Dispose the execution logger
            await DisposeLoggerAsync(executionLogger);
        }

        private async ValueTask DisposeLoggerAsync(ILogger logger)
        {
            try
            {
                // Dispose the logger
                if (logger is IAsyncDisposable asyncDisposableLogger)
                {
                    await asyncDisposableLogger.DisposeAsync();
                }
                else if (logger is IDisposable disposableLogger)
                {
                    disposableLogger.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Prevent any non-critical exceptions from stopping execution (like disposing the logger)
                // because we need the parsing to continue
                // Log the exception if we can
                ExecutionLogger?.Error(ex, "Error while trying to dispose a logger");
            }
        }

        private async void ParseButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the input and output files from the text boxes
            var inputFiles = InputFiles;
            var outputFiles = OutputFiles;

            // Check for empty input or output files
            if (inputFiles.Length == 0 || outputFiles.Length == 0)
            {
                MessageBox.Show("Please select at least one input and one output file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Check for mismatched input and output files
            if (inputFiles.Length != outputFiles.Length)
            {
                MessageBox.Show("The number of input and output files must be the same.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Get prompts file (if set)
            string promptsFile = PromptsFileTextBox.Text;
            bool hasPromptsFile = !string.IsNullOrEmpty(promptsFile);

            // Get the culture name from the combo box
            string cultureName = FileCultureComboBox.Text;

            bool isRowCountValid = int.TryParse(RowCountTextBox.Text, out var rowCount);

            bool overwriteColumn = OverwriteColumnCheckBox.IsChecked == true;

            bool decodeHtml = DecodeHtmlCheckBox.IsChecked == true;


            // Clear the log UI
            LogRichTextBox.Document.Blocks.Clear();

            // Clear the progress bar, percentage text, and rows processed text
            ProgressBar.Value = 0;
            ProgressPercentageTextBlock.Text = "0%";
            ProgressRowsProcessedTextBlock.Text = "Rows processed: 0";
            TimerTextBlock.Text = "00:00:00";
            _elapsedTime = TimeSpan.Zero;

            // Create a new cancellation token source
            CancellationTokenSource = new CancellationTokenSource();

            // Notify the UI the medication is starting being parsed
            IsParsing = true;

            // Generate timestamp for execution
            var executionTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Get main base folder for logs from the first output's folder
            var targetBaseFolderForExecutionLog = Path.GetDirectoryName(outputFiles[0]) ?? string.Empty;
            var baseFolderName = GetExecutionBaseNameFromTimestamp(executionTimestamp);
            var finalBaseFolderForExecutionLog = Path.Combine(targetBaseFolderForExecutionLog, baseFolderName);

            ExecutionLogger = CreateExecutionLogger(executionTimestamp, finalBaseFolderForExecutionLog);

            // Run timer
            StartTimer();

            try
            {
                try
                {
                    PerformNonCriticalUIAction(() =>
                    {
                        // Only show the progress files processed text block if there are multiple input files being processed
                        if (inputFiles.Length > 1)
                        {
                            ProgressFilesProcessedTextBlock.Visibility = Visibility.Visible;
                            ProgressFilesProcessedTextBlock.Text = $"Files processed: 0/{inputFiles.Length}";
                        }
                        else
                        {
                            ProgressFilesProcessedTextBlock.Visibility = Visibility.Collapsed;
                        }
                    }, "Error setting up ProgressFilesProcessedTextBlock in UI");

                    // Parse for each input and output file
                    int filesProcessed = 0;
                    foreach (var (inputFile, outputFile) in inputFiles.Zip(outputFiles))
                    {
                        var targetOutputFolder = Path.GetDirectoryName(outputFile) ?? string.Empty;
                        var finalOutputFolder = Path.Combine(targetOutputFolder, baseFolderName);

                        var fileLogger = CreateFileLogger(executionTimestamp, ExecutionLogger, inputFile, finalOutputFolder);

                        fileLogger.Information("### Starting parsing for input file: {InputFile}", inputFile);

                        // If we are using custom system prompts from a CSV file,
                        // we need to parse the inputs file for each system prompt
                        if (hasPromptsFile)
                        {
                            var promptsList = SystemPromptUtils.ParseFromCsvFile(promptsFile, cultureName);
                            int promptsCount = promptsList.Count;
                            if (promptsCount == 0)
                            {
                                throw new InvalidOperationException("The prompts file is empty.");
                            }

                            PerformNonCriticalUIAction(() =>
                            {
                                ProgressPromptsProcessedTextBlock.Visibility = Visibility.Visible;
                                ProgressPromptsProcessedTextBlock.Text = $"Prompts processed: 0/{promptsCount}";
                            }, "Error setting up ProgressPromptsProcessedTextBlock in UI");

                            // Parse for each system prompt in the prompts file
                            int promptsProcessed = 0;
                            foreach (var systemPrompt in promptsList)
                            {
                                var promptNumber = promptsProcessed + 1;

                                // Update the output file name to include the timestamp and a number representing the system prompt
                                var outputFileName = Path.GetFileNameWithoutExtension(outputFile);
                                var outputFileExtension = Path.GetExtension(outputFile);
                                var outputFileNameWithPrompt = $"{executionTimestamp}_{outputFileName}_prompt-{promptNumber}{outputFileExtension}";

                                var outputFileWithPrompt = Path.Combine(finalOutputFolder, outputFileNameWithPrompt);

                                // Use the custom system prompt
                                await ParseMedicationData(
                                    fileLogger,
                                    inputFile,
                                    outputFileWithPrompt,
                                    cultureName,
                                    isRowCountValid ? rowCount : null,
                                    overwriteColumn,
                                    decodeHtml,
                                    (systemPrompt, promptNumber),
                                    CancellationTokenSource.Token);

                                promptsProcessed++;

                                PerformNonCriticalUIAction(() =>
                                {
                                    ProgressPromptsProcessedTextBlock.Text = $"Prompts processed: {promptsProcessed}/{promptsCount}";
                                }, "Error updating ProgressPromptsProcessedTextBlock in UI");
                            }
                        }
                        else
                        {
                            PerformNonCriticalUIAction(() =>
                            {
                                ProgressPromptsProcessedTextBlock.Visibility = Visibility.Collapsed;
                            }, "Error hiding ProgressPromptsProcessedTextBlock in UI");

                            // Update the output file name to include the timestamp
                            var outputFileName = Path.GetFileNameWithoutExtension(outputFile);
                            var outputFileExtension = Path.GetExtension(outputFile);
                            var outputFileNameWithTimestamp = $"{executionTimestamp}_{outputFileName}{outputFileExtension}";

                            var outputFileWithTimestamp = Path.Combine(finalOutputFolder, outputFileNameWithTimestamp);

                            // Use the default system prompt
                            await ParseMedicationData(
                                fileLogger,
                                inputFile,
                                outputFileWithTimestamp,
                                cultureName,
                                isRowCountValid ? rowCount : null,
                                overwriteColumn,
                                decodeHtml,
                                null,
                                CancellationTokenSource.Token);
                        }

                        filesProcessed++;

                        PerformNonCriticalUIAction(() =>
                        {
                            ProgressFilesProcessedTextBlock.Text = $"Files processed: {filesProcessed}/{inputFiles.Length}";
                        }, "Error updating ProgressFilesProcessedTextBlock in UI");
                    }
                }
                finally
                {
                    PerformNonCriticalUIAction(() =>
                    {
                        // Stop the timer (needs to be called from UI thread)
                        _dispatcherTimer?.Stop();

                        // Update the timer text block
                        UpdateTimerTextBlock(isAlwaysRunningInUIThread: true);
                    }, "Error trying to stop the UI timer after parsing");
                }

                // Log the success
                ExecutionLogger?.Information("Parsing has been completed successfully.");
                MessageBox.Show($"Parsing has been completed successfully.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                // Log the cancellation
                ExecutionLogger?.Warning("Parsing was cancelled by the user");
                MessageBox.Show($"Parsing was cancelled.\nThe information that was already parsed has been written to the output file.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // Log the exception (we consider it "Fatal" because it halts the entire parsing process)
                ExecutionLogger?.Fatal(ex, "A fatal error occurred while parsing the medication");
                MessageBox.Show($"An error occurred while parsing the medication:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reset the flag
                PerformNonCriticalUIAction(() => IsParsing = false, "Error while trying to update parsing state in UI");

                await DisposeExecutionResourcesAsync();
            }
        }

        private async Task ParseMedicationData(ILogger fileLogger, string inputFile, string outputFile, string cultureName, int? rowCount, bool overwriteColumn, bool decodeHtml, (SystemPromptUtils.SystemPromptObject Data, int Number)? systemPromptData, CancellationToken token = default)
        {
            // Create logger specific to the current prompt
            ILogger logger = fileLogger;

            if (systemPromptData is not null)
            {
                logger = logger
                    .ForContext("OutputFile", outputFile)
                    .ForContext("SystemPrompt", systemPromptData.Value.Data.SystemPrompt)
                    .ForContext("SystemPromptFormat", systemPromptData.Value.Data.Format)
                    .ForContext("SystemPromptNumber", systemPromptData.Value.Number);
            }

            var configuration = new MedicationExtractingParserConfiguration()
            {
                CultureName = cultureName,
                InputFile = inputFile,
                OutputFile = outputFile,
                RowLimit = rowCount,
                OverwriteInputTargetColumn = overwriteColumn,
                DecodeHtmlFromInput = decodeHtml,
            };

            if (systemPromptData is not null)
            {
                var data = systemPromptData.Value.Data;
                configuration.SystemPrompt = data.SystemPrompt;
                configuration.UseJsonFormat = data.UsesJsonFormat;
            }

            try
            {
                MedicationParser = new MedicationExtractingParser()
                {
                    Configuration = configuration,
                    Logger = logger,
                    Progress = Progress,
                };

                fileLogger.Information("### Output file: {OutputFile}", outputFile);

                if (systemPromptData is null)
                {
                    logger.Debug("# Using system prompt:\n{SystemPrompt}", configuration.SystemPrompt);
                }
                else
                {
                    logger.Debug("# Using system prompt [{PromptNumber}]:\n{SystemPrompt}", systemPromptData.Value.Number, configuration.SystemPrompt);
                }

                await MedicationParser.ParseData(token);
            }
            finally
            {
                // Dispose the logger
                await DisposeLoggerAsync(logger);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Cancel the parsing
            CancellationTokenSource?.Cancel();
        }

        private void BrowseInputFileButton_Click(object sender, RoutedEventArgs e)
        {
            var files = BrowseInputCsvFiles(InputFiles);
            InputFileTextBox.Text = files is null ? string.Empty : string.Join(FileInTextSeparator, files);
        }

        private void BrowseOutputFileButton_Click(object sender, RoutedEventArgs e)
        {
            var inputFiles = InputFiles;

            if (inputFiles.Length == 0)
            {
                MessageBox.Show("Please select at least one input file first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            OutputFileTextBox.Text = string.Join(FileInTextSeparator, BrowseOutputCsvFiles(inputFiles, "output_{0}"));
        }

        private void BrowsePromptsFileButton_Click(object sender, RoutedEventArgs e)
        {
            PromptsFileTextBox.Text = BrowsePromptsCsvFile(PromptsFileTextBox.Text) ?? string.Empty;
        }

        const string CsvFilesFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
        private static string[]? BrowseInputCsvFiles(string[] currentPaths)
        {
            // Create a new open file dialog
            var dialog = new OpenFileDialog()
            {
                Title = "Select input files",
                Filter = CsvFilesFilter,
                Multiselect = true,
            };

            // Setup the dialog with the current path and default file name
            if (currentPaths.Length > 0)
            {
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(currentPaths[0]);
            }

            // Show the dialog
            if (dialog.ShowDialog() == true)
            {
                // Return the selected files
                return dialog.FileNames;
            }

            return null;
        }

        private static IEnumerable<string> BrowseOutputCsvFiles(string[] inputFiles, string? defaultFileNameTemplate = null)
        {
            string? lastOutputFile = null;
            foreach (var inputFile in inputFiles)
            {
                var outputFile = BrowseOutputCsvFile(inputFile, System.IO.Path.GetDirectoryName(lastOutputFile), defaultFileNameTemplate);
                if (outputFile is null)
                {
                    yield break;
                }
                yield return outputFile;
                lastOutputFile = outputFile;
            }
        }

        private static string? BrowseOutputCsvFile(string inputFile, string? lastOutputDir = null, string? defaultFileNameTemplate = null)
        {
            // Create a new save file dialog
            var dialog = new SaveFileDialog
            {
                Title = $"Select output file (input file: {System.IO.Path.GetFileName(inputFile)})",
                Filter = CsvFilesFilter,
                InitialDirectory = lastOutputDir ?? System.IO.Path.GetDirectoryName(inputFile)
            };

            if (defaultFileNameTemplate is not null)
            {
                dialog.FileName = string.Format(defaultFileNameTemplate, System.IO.Path.GetFileNameWithoutExtension(inputFile)) + System.IO.Path.GetExtension(inputFile);
            }

            // Show the dialog
            if (dialog.ShowDialog() == true)
            {
                // Return the selected file
                return dialog.FileName;
            }

            return null;
        }

        private static string? BrowsePromptsCsvFile(string currentPath)
        {
            // Create a new open file dialog
            var dialog = new OpenFileDialog()
            {
                Title = "Select prompts file",
                Filter = CsvFilesFilter,
            };

            // Setup the dialog with the current path
            if (System.IO.Path.GetDirectoryName(currentPath) is string currentDir)
            {
                dialog.InitialDirectory = currentDir;
            }

            // Show the dialog
            if (dialog.ShowDialog() == true)
            {
                // Return the selected file
                return dialog.FileName;
            }

            return null;
        }

        private void LogLevelComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Update the log level switch
            if (LoggingLevelSwitch is not null)
            {
                LoggingLevelSwitch.MinimumLevel = GetLogLevelFromComboBox();
            }
        }

#if DEBUG
        // TEST Autocompletions
        private void SetupTestAutocompletions()
        {
            // Add two columns to the grid for the test buttons
            var columnSpacing1 = new ColumnDefinition { Width = new GridLength(10) };
            var column1 = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) };
            var columnSpacing2 = new ColumnDefinition { Width = new GridLength(10) };
            var column2 = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) };

            // Add the columns to the grid
            ParametersGrid.ColumnDefinitions.Add(columnSpacing1);
            ParametersGrid.ColumnDefinitions.Add(column1);
            ParametersGrid.ColumnDefinitions.Add(columnSpacing2);
            ParametersGrid.ColumnDefinitions.Add(column2);

            // Create the test buttons
            var testJuanInputButton = new Button
            {
                Content = "TEST Juan Input",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var testAlejandroInputButton = new Button
            {
                Content = "TEST Alejandro Input",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var testJuanOutputButton = new Button
            {
                Content = "TEST Juan Output",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var testAlejandroOutputButton = new Button
            {
                Content = "TEST Alejandro Output",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Add the buttons to the grid

            ParametersGrid.Children.Add(testJuanInputButton);
            ParametersGrid.Children.Add(testAlejandroInputButton);
            ParametersGrid.Children.Add(testJuanOutputButton);
            ParametersGrid.Children.Add(testAlejandroOutputButton);

            const int inputRow = 0, outputRow = 2;

            int juanColumn = ParametersGrid.ColumnDefinitions.Count - 3;
            int alejandroColumn = ParametersGrid.ColumnDefinitions.Count - 1;

            Grid.SetRow(testJuanInputButton, inputRow);
            Grid.SetRow(testAlejandroInputButton, inputRow);
            Grid.SetRow(testJuanOutputButton, outputRow);
            Grid.SetRow(testAlejandroOutputButton, outputRow);

            Grid.SetColumn(testJuanInputButton, juanColumn);
            Grid.SetColumn(testJuanOutputButton, juanColumn);
            Grid.SetColumn(testAlejandroInputButton, alejandroColumn);
            Grid.SetColumn(testAlejandroOutputButton, alejandroColumn);

            // Add the event handlers

            testJuanInputButton.Click += TEST_Juan_Input_Button_Click;
            testAlejandroInputButton.Click += TEST_Alejandro_Input_Button_Click;
            testJuanOutputButton.Click += TEST_Juan_Output_Button_Click;
            testAlejandroOutputButton.Click += TEST_Alejandro_Output_Button_Click;
        }
        private void TEST_Juan_Input_Button_Click(object sender, RoutedEventArgs e)
        {
            InputFileTextBox.Text = TestConfigurations.JuanInputFile;
        }
        private void TEST_Alejandro_Input_Button_Click(object sender, RoutedEventArgs e)
        {
            InputFileTextBox.Text = TestConfigurations.AlejandroInputFile;
        }
        private void TEST_Juan_Output_Button_Click(object sender, RoutedEventArgs e)
        {
            OutputFileTextBox.Text = TestConfigurations.JuanOutputFile;
        }
        private void TEST_Alejandro_Output_Button_Click(object sender, RoutedEventArgs e)
        {
            OutputFileTextBox.Text = TestConfigurations.AlejandroOutputFile;
        }
#endif
    }
}
