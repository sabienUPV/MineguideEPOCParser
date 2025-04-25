using Microsoft.Win32;
using MineguideEPOCParser.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using static MineguideEPOCParser.Core.SystemPromptUtils;

namespace MineguideEPOCParser.GUIApp
{
    /// <summary>
    /// Lógica de interacción para MedicationParserControl.xaml
    /// </summary>
    public partial class MedicationParserControl : UserControl, IDisposable
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
                UpdateTimerTextBlock();
            }

            _dispatcherTimer.Tick += DispatcherTimerTick;

            _dispatcherTimer.Start();
        }

        private void UpdateTimerTextBlock()
        {
            TimerTextBlock.Text = _elapsedTime.ToString(@"hh\:mm\:ss");
        }

        /// <summary>
        /// Create a new logger that writes to a TextBox
        /// </summary>
        private Logger CreateLogger(string inputFile, string outputFile)
        {
            LoggingLevelSwitch ??= new LoggingLevelSwitch
            {
                // Take the default log level from the control
                MinimumLevel = GetLogLevelFromComboBox()
            };

            // Get input file name without extension
            var inputFileName = Path.GetFileNameWithoutExtension(inputFile);

            // Get directory from output file path
            var outputDirectory = Path.GetDirectoryName(outputFile);

            // If the output directory is empty, use the current directory
            var logFileDirectory = string.IsNullOrEmpty(outputDirectory) ? "." : outputDirectory;

            var logFilePath = Path.Combine(logFileDirectory, $"MineguideEPOCParser-{inputFileName}-.log");

            // Create a new logger
            var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose() // always log verbose to file, switch now only controls the log level in the UI
                //.MinimumLevel.ControlledBy(LoggingLevelSwitch)
                .WriteTo.RichTextBox(LogRichTextBox, levelSwitch: LoggingLevelSwitch)
                .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            return logger;
        }

        private LogEventLevel GetLogLevelFromComboBox()
        {
            if (LogLevelComboBox.SelectedItem is not ComboBoxItem selectedItem
                || selectedItem?.Content is not string selectedItemContent
                || !Enum.TryParse<LogEventLevel>(selectedItemContent, out var logLevel))
            {
                // Fallback default log level
                return LogEventLevel.Verbose;
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
            });
        }

        public void Dispose()
        {
            // Dispose the cancellation token source
            CancellationTokenSource?.Dispose();

            // Stop the timer
            _dispatcherTimer?.Stop();
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

            // Parse the medication
            IsParsing = true;

            // Run timer
            StartTimer();

            try
            {
                try
                {
                    // Only show the progress files processed text block if there are multiple input files being processed
                    ProgressFilesProcessedTextBlock.Visibility = inputFiles.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
                    ProgressFilesProcessedTextBlock.Text = $"Files processed: 0/{inputFiles.Length}";

                    // Parse for each input and output file
                    int filesProcessed = 0;
                    foreach (var (inputFile, outputFile) in inputFiles.Zip(outputFiles))
                    {
                        // If we are using custom system prompts from a CSV file,
                        // we need to parse the inputs file for each system prompt
                        if (PromptsFileTextBox.Text is string promptsFile && !string.IsNullOrEmpty(promptsFile))
                        {
                            ProgressPromptsProcessedTextBlock.Visibility = Visibility.Visible;

                            var promptsList = SystemPromptUtils.ParseFromCsvFile(promptsFile, cultureName);

                            ProgressPromptsProcessedTextBlock.Text = $"Prompts processed: 0/{promptsList.Count}";

                            // Parse for each system prompt in the prompts file
                            int promptsProcessed = 0;
                            foreach (var systemPrompt in promptsList)
                            {
                                var promptNumber = promptsProcessed + 1;

                                // Update the output file name to include a number representing the system prompt
                                var outputFileName = Path.GetFileNameWithoutExtension(outputFile);
                                var outputFileExtension = Path.GetExtension(outputFile);
                                var outputFileNameWithPrompt = $"{outputFileName}-prompt-{promptNumber}{outputFileExtension}";

                                var outputFileWithPrompt = Path.Combine(Path.GetDirectoryName(outputFile) ?? string.Empty, outputFileNameWithPrompt);

                                // Use the custom system prompt
                                await ParseMedicationData(
                                    inputFile,
                                    outputFileWithPrompt,
                                    cultureName,
                                    isRowCountValid ? rowCount : null,
                                    overwriteColumn,
                                    decodeHtml,
                                    systemPrompt,
                                    promptNumber,
                                    CancellationTokenSource.Token).ConfigureAwait(false);

                                ProgressPromptsProcessedTextBlock.Text = $"Prompts processed: {++promptsProcessed}/{promptsList.Count}";
                            }
                        }
                        else
                        {
                            ProgressPromptsProcessedTextBlock.Visibility = Visibility.Collapsed;

                            // Use the default system prompt
                            await ParseMedicationData(
                                inputFile,
                                outputFile,
                                cultureName,
                                isRowCountValid ? rowCount : null,
                                overwriteColumn,
                                decodeHtml,
                                null,
                                null,
                                CancellationTokenSource.Token).ConfigureAwait(false);
                        }

                        ProgressFilesProcessedTextBlock.Text = $"Files processed: {++filesProcessed}/{inputFiles.Length}";
                    }
                }
                finally
                {
                    // Stop the timer
                    _dispatcherTimer?.Stop();

                    // Update the timer text block
                    Dispatcher.Invoke(UpdateTimerTextBlock);
                }

                MessageBox.Show($"Parsing has been completed successfully.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show($"Parsing was cancelled.\nThe information that was already parsed has been written to the output file.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while parsing the medication:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reset the flag
                Dispatcher.Invoke(() => IsParsing = false);

                // Dispose the cancellation token source
                CancellationTokenSource.Dispose();

                // Set the cancellation token source to null
                CancellationTokenSource = null;

                // Set the timer to null
                _dispatcherTimer = null;

                // Set the parser to null
                MedicationParser = null;
            }

        }

        private async Task ParseMedicationData(string inputFile, string outputFile, string cultureName, int? rowCount, bool overwriteColumn, bool decodeHtml, SystemPromptObject? systemPrompt, int? systemPromptNumber = null, CancellationToken token = default)
        {
            var logger = CreateLogger(inputFile, outputFile);

            var configuration = new MedicationExtractingParserConfiguration()
            {
                CultureName = cultureName,
                InputFile = inputFile,
                OutputFile = outputFile,
                Count = rowCount,
                OverwriteColumn = overwriteColumn,
                DecodeHtmlFromInput = decodeHtml,
            };

            if (systemPrompt is not null)
            {
                configuration.SystemPrompt = systemPrompt.SystemPrompt;
                configuration.UseJsonFormat = systemPrompt.UsesJsonFormat;
            }

            try
            {
                MedicationParser = new MedicationExtractingParser()
                {
                    Configuration = configuration,
                    Logger = logger,
                    Progress = Progress,
                };

                logger.Information("### Starting parsing for input file: {InputFile}", inputFile);

                if (systemPromptNumber is null)
                {
                    logger.Debug("# Using system prompt:\n{SystemPrompt}", configuration.SystemPrompt);
                }
                else
                {
                    logger.Debug("# Using system prompt [{PromptNumber}]:\n{SystemPrompt}", systemPromptNumber, configuration.SystemPrompt);
                }

                await MedicationParser.ParseData(token);
            }
            finally
            {
                // Dispose the logger
                if (logger is IAsyncDisposable asyncDisposableLogger)
                {
                    await asyncDisposableLogger.DisposeAsync().ConfigureAwait(false);
                }
                else if (logger is IDisposable disposableLogger)
                {
                    disposableLogger.Dispose();
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Cancel the parsing
            CancellationTokenSource?.Cancel();

            // Stop the timer
            _dispatcherTimer?.Stop();

            // Update the timer text block
            UpdateTimerTextBlock();

            // Set the timer to null
            _dispatcherTimer = null;
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

            OutputFileTextBox.Text = string.Join(FileInTextSeparator, BrowseOutputCsvFiles(inputFiles, "{0}-OUTPUT"));
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
