using MineguideEPOCParser.Core.Parsers;
using MineguideEPOCParser.Core.Parsers.Configurations;
using MineguideEPOCParser.Core.Utils;
using MineguideEPOCParser.Core.Validation;
using MineguideEPOCParser.GUIApp.Utils;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MineguideEPOCParser.GUIApp.Tools
{
    /// <summary>
    /// Lógica de interacción para MedicationManualValidatorControl.xaml
    /// </summary>
    public partial class MedicationManualValidatorControl : UserControl, IDisposable
    {
        // Dependency property IsParsing
        public static readonly DependencyProperty IsParsingProperty = DependencyProperty.Register(
            nameof(IsParsing),
            typeof(bool),
            typeof(MedicationManualValidatorControl),
            new PropertyMetadata(false)
        );

        // Dependency property IsNotParsing
        public static readonly DependencyProperty IsNotParsingProperty = DependencyProperty.Register(
            nameof(IsNotParsing),
            typeof(bool),
            typeof(MedicationManualValidatorControl),
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

        public MedicationManualValidatorControl()
        {
            InitializeComponent();

            CreateProgress();

            // Hook into the Loaded and Unloaded events
            this.Loaded += MedicationManualValidatorControl_Loaded;
            this.Unloaded += MedicationManualValidatorControl_Unloaded;
        }

        private void MedicationManualValidatorControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Find the parent window once the control is rendered in the UI tree
            Window parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                // Subscribe to the window's PreviewKeyDown event
                parentWindow.PreviewKeyDown += MainWindow_PreviewKeyDown;
            }
        }

        private void MedicationManualValidatorControl_Unloaded(object sender, RoutedEventArgs e)
        {
            // ALWAYS unregister to prevent memory leaks!
            Window parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                parentWindow.PreviewKeyDown -= MainWindow_PreviewKeyDown;
            }
        }

        public void Dispose()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public Progress<ProgressValue>? Progress { get; private set; }
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

        /// <summary>
        /// Hide loading text when website loads
        /// </summary>
        private void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                MyWebViewLoadingText.Visibility = Visibility.Collapsed;
            }
            else
            {
                MyWebViewLoadingText.Text = "Failed to load website.";
            }

            MyWebView.NavigationCompleted -= OnNavigationCompleted; // Unsubscribe to avoid multiple calls
        }

        public class MedicationResultUI : MedicationMatch
        {
            public Hyperlink? Hyperlink { get; set; } // Optional hyperlink for clickable highlights

            public bool HasMatchInText => StartIndex >= 0;
            public string DisplayText => HasMatchInText ? MatchInText : ExtractedMedication;

            public Color BackgroundColor => ExperimentResult switch
            {
                ExperimentResultType.TP => Colors.LightGreen,
                ExperimentResultType.TP_ => Colors.LightSkyBlue,
                ExperimentResultType.FP => Colors.OrangeRed,
                ExperimentResultType.FN => Colors.Yellow,
                _ => Colors.Transparent, // Default color if no match
            };

            public Color ForegroundColor => ExperimentResult switch
            {
                ExperimentResultType.TP => Colors.DarkGreen,
                ExperimentResultType.TP_ => Colors.DarkBlue,
                ExperimentResultType.FP => Colors.White,
                ExperimentResultType.FN => Colors.Black,
                _ => Colors.Black, // Default color if no match
            };

            public MedicationResult ToMedicationResult()
            {
                if (HasMatchInText)
                {
                    return new MedicationMatch
                    {
                        StartIndex = this.StartIndex,
                        Length = this.Length,
                        MatchInText = this.MatchInText,
                        ExtractedMedication = this.ExtractedMedication,
                        ExperimentResult = this.ExperimentResult,
                        CorrectedMedication = this.CorrectedMedication,
                        Details = this.Details
                    };
                }
                else
                {
                    return new MedicationResult
                    {
                        ExtractedMedication = this.ExtractedMedication,
                        ExperimentResult = this.ExperimentResult,
                        CorrectedMedication = this.CorrectedMedication,
                        Details = this.Details
                    };
                }
            }

            public static MedicationResultUI FromMedicationResult(MedicationResult result, Hyperlink? hyperlink = null)
            {
                if (result is MedicationMatch match)
                {
                    return new MedicationResultUI
                    {
                        StartIndex = match.StartIndex,
                        Length = match.Length,
                        MatchInText = match.MatchInText,
                        ExtractedMedication = match.ExtractedMedication,
                        ExperimentResult = match.ExperimentResult,
                        CorrectedMedication = match.CorrectedMedication,
                        Details = match.Details,
                        Hyperlink = hyperlink
                    };
                }
                else
                {
                    return new MedicationResultUI
                    {
                        StartIndex = -1,
                        Length = 0,
                        MatchInText = string.Empty,
                        ExtractedMedication = result.ExtractedMedication,
                        ExperimentResult = result.ExperimentResult,
                        CorrectedMedication = result.CorrectedMedication,
                        Details = result.Details,
                        Hyperlink = hyperlink
                    };
                }
            }
        }

        private string? _currentText;
        private List<MedicationResultUI>? _currentMedicationResults;

        /// <summary>
        /// The idea is that, while the user keeps correcting medications,
        /// We start creating a dictionary of corrections, that we will then
        /// be able to match using the Levenshtein distance to provide
        /// with default corrections for medications based on previous corrections already made.
        /// Once the user validates the new inferred correction, it gets added as well.
        /// </summary>
        private readonly Dictionary<string, string> _correctedMedications = new(StringComparer.OrdinalIgnoreCase);

        private string? FindClosestCorrectedMedication(MedicationResult medicationResult)
        {
            if (_correctedMedications is null || _correctedMedications.Count == 0)
            {
                return null; // No previously corrected medications to compare against
            }

            if (_correctedMedications.TryGetValue(medicationResult.ExtractedMedication, out var correctedMedication))
            {
                return correctedMedication;
            }

            var (previouslyExtractedMedication, distance) = _correctedMedications.Keys
                .Select(previouslyExtractedMedication =>
                {
                    return (previouslyExtractedMedication,
                            distance: MedicationAnalyzers.CalculateCaseInsensitiveLevenshteinDistance(
                                        medicationResult.ExtractedMedication,
                                        previouslyExtractedMedication));
                })
                .MinBy(md => md.distance);

            var similarityScore = MedicationAnalyzers.CalculateSimilarityScore(medicationResult.ExtractedMedication, previouslyExtractedMedication, distance);
            var isSimilarEnough = MedicationAnalyzers.IsStrongSimilarityOrBetter(similarityScore);

            return isSimilarEnough ? _correctedMedications[previouslyExtractedMedication] : null;
        }

        public void LoadMedicationResults(string text, IEnumerable<MedicationResult> medicationResults)
        {
            var sortedResults = medicationResults
                .Select(r =>
                {
                    var medicationResultUI = MedicationResultUI.FromMedicationResult(r);

                    // If previously we corrected a close enough medication,
                    // we auto-fill the corrected medication to that one by default
                    if (FindClosestCorrectedMedication(r) is string correctedMedication)
                    {
                        medicationResultUI.CorrectedMedication = correctedMedication;
                    }

                    return medicationResultUI;
                })
                .OrderBy(r => r.HasMatchInText ? 0 : 1)
                .ThenBy(r => r.StartIndex)
                .ToList();

            _currentText = text; // Store the current text for later use
            _currentMedicationResults = sortedResults; // Store results for later use
        }

        // Enhanced version with clickable highlights for validation
        public void RenderMedicationsText() => 
            HighlightMedicationsClickable(MyRichTextBox, OnMedicationClicked);

        public void HighlightMedicationsClickable(RichTextBox richTextBox, Func<MedicationResultUI, Task> onMedicationClick)
        {
            if (_currentText == null)
            {
                richTextBox.Document.Blocks.Clear();
                return; // No text or results to highlight
            }

            if (_currentMedicationResults == null || _currentMedicationResults.Count == 0)
            {
                // Just display the text without highlights
                richTextBox.Document.Blocks.Clear();
                richTextBox.Document.Blocks.Add(new Paragraph(new Run(_currentText)));
                return; // No results to highlight
            }

            richTextBox.Document.Blocks.Clear();
            var paragraph = new Paragraph();
            var currentIndex = 0;

            // Tabs might be used as newline replacements in the text so they were single lines for further processing,
            // so we replace them back with newlines for better readability in the RichTextBox
            var textToVisualize = _currentText.Replace("\t", "\n");

            foreach (var result in _currentMedicationResults)
            {
                if (result.StartIndex < 0)
                {
                    continue; // Skip non-matches for now, we will add them at the end with a separator
                }

                // Add normal text
                if (result.StartIndex > currentIndex)
                {
                    var normalText = textToVisualize.Substring(currentIndex, result.StartIndex - currentIndex);
                    paragraph.Inlines.Add(new Run(normalText));
                }

                var hyperlink = CreateMedicationHyperlink(result, richTextBox, onMedicationClick);
                paragraph.Inlines.Add(hyperlink);
                currentIndex = result.StartIndex + result.Length;
            }

            // Add remaining text
            if (currentIndex < textToVisualize.Length)
            {
                var remainingText = textToVisualize.Substring(currentIndex);
                paragraph.Inlines.Add(new Run(remainingText));
            }

            // Add non-matches that are not part of the original text at the end, separated by a line break
            var nonMatches = _currentMedicationResults.Where(r => r.StartIndex < 0).ToList();
            if (nonMatches.Count > 0)
            {
                paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new Run("------ Medications not found in text predicted by the LLM (possible hallucinations): ------") { FontWeight = FontWeights.Bold });
                paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new Run("(these medications cannot be changed from False Positives (FP), since a match in the text cannot be inferred accurately)") { FontStyle = FontStyles.Italic });
                paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new LineBreak());

                foreach (var result in nonMatches)
                {
                    var hyperlink = CreateMedicationHyperlink(result, richTextBox, onMedicationClick);
                    paragraph.Inlines.Add(hyperlink);
                    paragraph.Inlines.Add(new LineBreak());
                }
            }

            richTextBox.Document.Blocks.Add(paragraph);
        }

        private Hyperlink CreateMedicationHyperlink(MedicationResultUI result, RichTextBox richTextBox, Func<MedicationResultUI, Task> onMedicationClick)
        {
            var tooltipBuilder = new StringBuilder();
            if (result.HasMatchInText)
            {
                tooltipBuilder.AppendLine($"Text: {result.MatchInText}");
            }
            tooltipBuilder.AppendLine($"Extracted medication (from LLM): {result.ExtractedMedication}");
            tooltipBuilder.Append($"Validated experiment result: {result.ExperimentResult.ToResultString()}");
            if (!string.IsNullOrEmpty(result.CorrectedMedication))
            {
                tooltipBuilder.AppendLine();
                tooltipBuilder.Append($"Corrected medication: {result.CorrectedMedication}");
            }

            // Create clickable hyperlink for medication
            var hyperlink = new Hyperlink(new Run(result.DisplayText))
            {
                Foreground = new SolidColorBrush(result.ForegroundColor),
                Background = new SolidColorBrush(result.BackgroundColor),
                FontWeight = FontWeights.Bold,
                TextDecorations = string.IsNullOrEmpty(result.CorrectedMedication) ? null : TextDecorations.Underline,
                Focusable = true,
                ToolTip = tooltipBuilder.ToString(),
            };

            // Capture the result in the closure
            var currentResult = result;
            currentResult.Hyperlink = hyperlink;
            hyperlink.Click += async (s, e) =>
            {
                // If Ctrl+Click is pressed, select the text instead of clicking
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // Focus the RichTextBox to ensure selection works
                    richTextBox.Focus();
                    // Select the text of the hyperlink
                    richTextBox.Selection.Select(hyperlink.ContentStart, hyperlink.ContentEnd);
                    return; // Do not click the hyperlink
                }

                // Otherwise, invoke the custom medication click handler
                // (usually this will trigger a search in SNOMED)
                await onMedicationClick(currentResult);
            };
            hyperlink.PreviewKeyDown += Hyperlink_PreviewKeyDown;
            hyperlink.PreviewMouseDown += Hyperlink_PreviewMouseDown;

            return hyperlink;
        }

        private void Hyperlink_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Click hyperlink when pressing space key
            if (e.Key != Key.Space || sender is not Hyperlink hyperlink)
            {
                return;
            }

            //If Ctrl+Space is pressed, instead of clicking, we want to select the text
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Focus the RichTextBox to ensure selection works
                MyRichTextBox.Focus();
                // Select the text of the hyperlink
                MyRichTextBox.Selection.Select(hyperlink.ContentStart, hyperlink.ContentEnd);
                e.Handled = true; // Prevent default space behavior
                return;
            }

            // Otherwise, click the hyperlink
            hyperlink?.DoClick();

            e.Handled = true; // Prevent default space behavior
        }

        private void Hyperlink_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Hyperlink hyperlink)
            {
                return;
            }

            // If right click is pressed, and there is no selected text, open the correct medication dialog
            bool shouldCorrectMedication = (e.ChangedButton == MouseButton.Right && MyRichTextBox.Selection.IsEmpty);
            // If middle click is pressed, toggle the medication match between TP and FP
            bool shouldToggleExperimentResult = e.ChangedButton == MouseButton.Middle;

            // If neither of our conditions is true, do nothing
            if (!(shouldCorrectMedication || shouldToggleExperimentResult))
            {
                return;
            }

            // Get the focused medication match based on the clicked hyperlink
            var match = _currentMedicationResults?.FirstOrDefault(m => m.HasMatchInText && m.Hyperlink == hyperlink);
            if (match is null)
            {
                MessageBox.Show("Medication match not found for the clicked hyperlink.");
                return;
            }

            // If right click is pressed, and there is no selected text, open the correct medication dialog
            if (shouldCorrectMedication)
            {
                CorrectMedication(match);
            }
            else // if (shouldToggleExperimentResult)
            {
                // If middle click is pressed, toggle the medication match between TP and FP
                if (match.ExperimentResult == MedicationMatch.ExperimentResultType.FP)
                {
                    MarkMedicationAsTruePositive(match); // Change to TP
                }
                else
                {
                    MarkMedicationAsFalsePositive(match); // Change to FP
                }
            }
            
            RenderMedicationsText(); // Redraw the RichTextBox with updated highlights
            FocusMedicationMatch(match); // Focus the match after redrawing
            e.Handled = true;
        }

        public static readonly string DefaultCultureName = System.Globalization.CultureInfo.CurrentCulture.Name; // TODO: Allow user to select culture if needed

        private CancellationTokenSource? _cancellationTokenSource;
        private async void LoadMedications(object? sender, RoutedEventArgs args) => await LoadMedications();
        private async Task LoadMedications()
        {
            // Load input file
            var inputFile = BrowseInputFile();
            if (string.IsNullOrEmpty(inputFile))
            {
                MessageBox.Show("No input file selected.");
                return;
            }

            // Load output file
            var outputFile = BrowseOutputFile(inputFile);
            if (string.IsNullOrEmpty(outputFile))
            {
                MessageBox.Show("No output file selected.");
                return;
            }

            // Create parser configuration
            var configuration = new MedicationManualValidatorParserConfiguration
            {
                CultureName = DefaultCultureName, // TODO: Allow user to select culture if needed
                InputFile = inputFile,
                OutputFile = outputFile,
                ValidationFunction = ValidateMedications,
            };

            // Create cancellation token source
            _cancellationTokenSource = new CancellationTokenSource();

            // Create parser instance
            var parser = new MedicationManualValidatorParser()
            {
                Configuration = configuration,
                Progress = Progress,
            };

            IsParsing = true; // Set parsing state to true

            try
            {
                // Start the parser (with cancellation support)
                await parser.ParseData(_cancellationTokenSource.Token);

                // Show success message (or stop message if it didn't finish)
                bool stoppedMidway = _navigationDirection == NavigationDirection.Stop;
                MessageBox.Show(
                    $"""
                    Medication validation {(stoppedMidway ? "progress saved and stopped" : "completed")} successfully.

                    The validated medications have been written to: {outputFile}.

                    {(stoppedMidway ? "You can resume the validation later by using the output file as input, and your previous validations will be loaded automatically." : "All medications have been validated.")}
                    """, (stoppedMidway ? "Progress saved and stopped" : "Completed") + " successfully", MessageBoxButton.OK, MessageBoxImage.Information);

                // Reset validation (set parsing to false, clear the RichTextBox, and reset matches)
                ResetMedicationValidation();
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("The validation process was cancelled. No progress was saved.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during medication validation:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsParsing = false; // Set parsing state to false
            }
        }

        private NavigationDirection _navigationDirection = NavigationDirection.Next;

        private SemaphoreSlim? _medicationValidationSemaphore;
        private async Task<ValidationStepResult> ValidateMedications(string text, IEnumerable<MedicationResult> medicationResults, CancellationToken cancellationToken)
        {
            _navigationDirection = NavigationDirection.Next; // Default to Next for each call
            LoadMedicationResults(text, medicationResults);

            if (_currentMedicationResults is null)
            {
                throw new InvalidOperationException("No medication results loaded. Please load medications first.");
            }

            RenderMedicationsText();

            SemaphoreSlim? semaphore = null;
            try
            {
                semaphore = new SemaphoreSlim(0, 1);
                _medicationValidationSemaphore = semaphore;

                using (cancellationToken.Register(() => semaphore.Release()))
                {
                    await semaphore.WaitAsync(cancellationToken);
                }

                if (_navigationDirection == NavigationDirection.Back)
                {
                    return new ValidationStepResult(NavigationDirection.Back, []);
                }

                // Add the current corrected medications to the dictionary
                foreach (var match in _currentMedicationResults)
                {
                    if (!string.IsNullOrEmpty(match.CorrectedMedication))
                    {
                        _correctedMedications[match.ExtractedMedication] = match.CorrectedMedication;
                    }
                }

                return new ValidationStepResult(_navigationDirection, _currentMedicationResults.Select(m => m.ToMedicationResult()).ToList());
            }
            finally
            {
                semaphore?.Dispose();
                _medicationValidationSemaphore = null; // Siempre se limpia, incluso si hay excepción
            }
        }

        // Call this method when the user clicks "Next" or "Finish" after validation
        private void OnUserFinishedMedicationValidation(object? sender, RoutedEventArgs e)
        {
            _navigationDirection = NavigationDirection.Next;
            _medicationValidationSemaphore?.Release();
        }

        private void OnUserRequestedBack(object? sender, RoutedEventArgs e)
        {
            _navigationDirection = NavigationDirection.Back;
            _medicationValidationSemaphore?.Release();
        }

        private void OnStopMedicationValidation(object sender, RoutedEventArgs e)
        {
            _navigationDirection = NavigationDirection.Stop;
            _medicationValidationSemaphore?.Release();
        }

        private void OnCancelMedicationValidation(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
        }

        // True medication button click handler
        private void OnTrueMedicationClicked(object? sender, RoutedEventArgs e)
        {
            // Get focused medication match
            MedicationResultUI? selectedMatch = GetFocusedMedicationMatch();

            if (selectedMatch is null)
            {
                // Get selected text from RichTextBox
                var selectedText = GetSelectedText(out int startIndex);
                if (selectedText == null)
                {
                    MessageBox.Show("Please select a medication to mark as a True Positive (TP), OR select some text to add as a False Negative (FN).");
                    return; // No valid selection, exit
                }

                // Check if the selected text matches any medication match
                selectedMatch = FindMedicationMatchFromSelectedText(selectedText, startIndex);

                if (selectedMatch is null)
                {
                    // If an exact match was not found, check for partial substring matches (TP*)
                    var partialMatches = FindPartialMedicationMatchesInsideSelectedText(selectedText, startIndex);

                    if (partialMatches is not null && partialMatches.Count > 0)
                    {
                        // If a partial match was found, ask the user if they want to mark it as TP* (since it's not an exact match)
                        if (AskUserToMarkMedicationsAsPartialSubstringTruePositive(partialMatches, selectedText))
                        {
                            RenderMedicationsText(); // Redraw the RichTextBox with updated highlights
                            FocusMedicationMatch(partialMatches[^1]); // Focus the last partial match after marking them
                        }
                        return;
                    }

                    // If no match was found, we can add the selected text as a new medication (it's a false negative)
                    var newMatch = AddFalseNegativeMedication(selectedText, startIndex);
                    RenderMedicationsText(); // Redraw the RichTextBox with updated highlights
                    FocusMedicationMatch(newMatch); // Focus the new hyperlink
                    return;
                }
            }

            // If a match was found, mark it as a true positive
            MarkMedicationAsTruePositive(selectedMatch);
            RenderMedicationsText(); // Redraw the RichTextBox with updated highlights
            FocusMedicationMatch(selectedMatch); // Focus the match after marking it
        }

        private static void MarkMedicationAsTruePositive(MedicationResultUI match)
        {
            if (match.ExperimentResult != MedicationResult.ExperimentResultType.FP)
            {
                // If it isn't a false positive, either it is already a true positive,
                // or it is a false negative, which shouldn't be able to change into a true positive
                if (match.ExperimentResult == MedicationResult.ExperimentResultType.FN)
                {
                    MessageBox.Show("A false negative (FN) medication match cannot be marked as true positive (TP).");
                }
                return;
            }

            if (match.MatchInText == match.ExtractedMedication)
            {
                // Change the experiment result to TP
                match.ExperimentResult = MedicationResult.ExperimentResultType.TP;
            }
            else
            {
                // Change the experiment result to TP*, since the match is not exact
                match.ExperimentResult = MedicationResult.ExperimentResultType.TP_;
            }
        }

        private static bool AskUserToMarkMedicationsAsPartialSubstringTruePositive(ICollection<MedicationResultUI> matches, string selectedText)
        {
            var matchTexts = string.Join("\n", matches.Select(m => $"- '{m.MatchInText}'"));
            var result = MessageBox.Show(
                $"""
                The following medication matches were found in the selected text '{selectedText}' as partial substrings:

                {matchTexts}

                Do you want to mark these medications as partial substring true positives (TP*) of the selected text?
                This will also set the corrected medication to the selected text for all of them, but will keep the original match in text for reference.
                """,
                "Mark as Partial Substring True Positive (TP*)?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return false;
            }

            foreach (var match in matches)
            {
                // Change the experiment result to TP*, since it's a partial match
                match.ExperimentResult = MedicationResult.ExperimentResultType.TP_;

                // Update the corrected medication to the selected text
                match.CorrectedMedication = selectedText;
            }

            return true;
        }

        private MedicationResultUI AddFalseNegativeMedication(string selectedText, int startIndex)
        {
            // Initialize the validated medications list if it is null
            _currentMedicationResults ??= [];

            // Create a new MedicationResultUI for the selected text
            var newMatch = new MedicationResultUI
            {
                StartIndex = startIndex,
                Length = selectedText.Length,
                MatchInText = selectedText,
                ExtractedMedication = selectedText,
                // We are manually adding medication that hadn't been extracted by the LLM, so it's a False Negative
                ExperimentResult = MedicationResult.ExperimentResultType.FN
            };

            // Add the selected text to the validated medications list
            // (making sure it's still sorted by start index,
            // since our code assumes that the matches are sorted)
            _currentMedicationResults.AddSorted(newMatch, MedicationMatch.Comparer);

            // Return the new match for further processing if needed
            return newMatch;
        }

        // False positive medication button click handler
        private void OnFalseMedicationClicked(object? sender, RoutedEventArgs e)
        {
            if (_currentMedicationResults == null || _currentMedicationResults.Count == 0)
            {
                MessageBox.Show("No medications to mark as false positives.");
                return; // No matches to mark as false positives, exit
            }

            // Get selected medication
            MedicationResultUI? selectedMatch = GetFocusedMedicationMatch();

            if (selectedMatch is null)
            {
                // Get selected text from RichTextBox
                var selectedText = GetSelectedText(out int startIndex);
                if (selectedText is null)
                {
                    MessageBox.Show("Please select a medication to mark as a False Positive (FP) if it was extracted, OR to remove a False Negative (FN) you previously added.");
                    return; // No valid selection, exit
                }
                // Find the match by start index and selected text
                selectedMatch = FindMedicationMatchFromSelectedText(selectedText, startIndex);
                if (selectedMatch is null)
                {
                    MessageBox.Show($"Medication '{selectedText}' not found in the list.");
                    return; // No match found, exit
                }
            }

            MarkMedicationAsFalsePositive(selectedMatch); // Mark the selected medication as a false positive
            RenderMedicationsText(); // Redraw the RichTextBox with updated highlights
            FocusMedicationMatch(selectedMatch); // Focus the match after marking it
        }

        private void MarkMedicationAsFalsePositive(MedicationResultUI match)
        {
            if (match.ExperimentResult == MedicationMatch.ExperimentResultType.FN)
            {
                // If it is a false negative, and you are marking it as "false positive",
                // that means it wasn't a match in the first place, so remove it from the matches
                _currentMedicationResults?.Remove(match);
                return;
            }

            // Change the experiment result to FP
            match.ExperimentResult = MedicationMatch.ExperimentResultType.FP;
        }

        private void OnCorrectMedicationClicked(object? sender, RoutedEventArgs e)
        {
            if (_currentMedicationResults == null || _currentMedicationResults.Count == 0)
            {
                MessageBox.Show("No medications to correct.");
                return; // No matches to correct, exit
            }

            // Get focused medication match
            MedicationResultUI? selectedMatch = GetFocusedMedicationMatch();
            if (selectedMatch is null)
            {
                // Get selected text from RichTextBox
                var selectedText = GetSelectedText(out int startIndex);
                if (selectedText is null)
                {
                    MessageBox.Show("Please select a medication to correct.");
                    return; // No valid selection, exit
                }
                // Find the match by start index and selected text
                selectedMatch = FindMedicationMatchFromSelectedText(selectedText, startIndex);
                if (selectedMatch is null)
                {
                    MessageBox.Show($"Medication '{selectedText}' not found in the validated list.");
                    return; // No match found, exit
                }
            }

            CorrectMedication(selectedMatch); // Correct the selected medication
            RenderMedicationsText(); // Redraw the RichTextBox with updated highlights
            FocusMedicationMatch(selectedMatch); // Focus the match after correction
        }

        private void CorrectMedication(MedicationResultUI medicationMatch)
        {
            // If the match is marked as a False Positive (FP),
            // ask the user if the correct medication was already contained in the matched text
            // (if so, there would be a False Negative (FN) as well in that subtext which we should add)
            bool isFalsePositive = medicationMatch.ExperimentResult == MedicationMatch.ExperimentResultType.FP;
            if (isFalsePositive)
            {
                var result = MessageBox.Show(
                    $"""
                    NOTE: The selected medication '{medicationMatch.ExtractedMedication}' was marked as a **False Positive** (FP).
                    
                    If you continue, you will be prompted to mark part of the original text as a False Negative (FN).
                    You NEED to ensure that the corrected medication you enter is part of the original text EXPLICITLY.
                    If you later need to correct that medication as well (e.g., due to a typo, or a need to convert brands to generic names),
                    you can do so afterwards by doing the same 'Correct' action on the newly created FN text.

                    Do you want to continue?
                    """,
                    "Mark part of the original text as False Negative (FN)",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Information);
                if (result != MessageBoxResult.OK)
                {
                    return; // User cancelled the operation
                }
            }

            // Prompt user for corrected medication
            string? input;
            int subTextIndex = -1;
            while (true)
            {
                input = InputBoxWindow.Show(
                $"Extracted medication (from LLM): {medicationMatch.ExtractedMedication}\n\nEnter the corrected medication name:",
                $"Correct Medication ({medicationMatch.ExtractedMedication})",
                medicationMatch.CorrectedMedication ?? medicationMatch.ExtractedMedication);
                
                if (string.IsNullOrWhiteSpace(input))
                {
                    if (medicationMatch.CorrectedMedication is null)
                    {
                        // No correction was previously set, so nothing to remove
                        return; // No valid input, exit
                    }
                    var result = MessageBox.Show("No correction entered. Did you mean to remove the correction?\n\nPress 'Yes' to remove the correction for this medication.", "Remove correction?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        // User wants to remove the correction
                        medicationMatch.CorrectedMedication = null;
                    }
                    return; // No valid input, exit
                }
                
                input = input.Trim(); // Trim whitespace

                if (isFalsePositive)
                {
                    subTextIndex = medicationMatch.MatchInText.IndexOf(input, StringComparison.OrdinalIgnoreCase);
                    if (subTextIndex < 0)
                    {
                        // The corrected medication is not part of the original text,
                        // so we can't add a False Negative (FN) here.
                        // Prompt the user that they need to enter a valid subtext.
                        var result = MessageBox.Show(
                            $"""
                            The corrected medication '{input}' as **False Negative (FN)** was not found in the original text:
                            
                            {medicationMatch.MatchInText}
                            
                            Please ensure that the corrected medication is part of the original text.

                            Remember: If you meant to correct a **True Positive** (TP or TP*) medication instead,
                            please change its status from **False Positive** (FP) to **True Positive** (TP or TP*) first,
                            and then use the 'Correct' action again to correct it.

                            Do you want to try again?
                            """,
                            "Warning: False Negative (FN) not found in text",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Yes)
                        {
                            continue; // Try again
                        }
                        else
                        {
                            return; // User cancelled the operation
                        }
                    }
                }
                break; // Valid input, exit loop
            }
            
            // Update the corrected medication
            medicationMatch.CorrectedMedication = input;

            if (isFalsePositive)
            {
                var subText = medicationMatch.MatchInText.Substring(subTextIndex, medicationMatch.CorrectedMedication.Length);
                AddFalseNegativeMedication(subText, medicationMatch.StartIndex + subTextIndex);
            }
        }

        private void FocusMedicationMatch(MedicationResultUI match)
        {
            // If the match has a hyperlink, focus it
            if (match.Hyperlink == null)
            {
                throw new InvalidOperationException("The medication match does not have a hyperlink to focus.");
            }

            if (_currentMedicationResults is null)
            {
                throw new InvalidOperationException("No medication matches loaded. Please load medications first.");
            }

            var focusIndex = _currentMedicationResults.IndexOf(match);
            if (focusIndex < 0)
            {
                // The medication match is not found in the current matches list.
                // This could happen, for instance, if a FN match was removed,
                // so we return gracefully without focusing.
                return;
            }

            match.Hyperlink.Focus();
            _currentMedicationFocusIndex = focusIndex; // Update focus index
        }

        private MedicationResultUI? GetFocusedMedicationMatch(bool onlyIfMatchInText = true)
        {
            // If a hyperlink is focused, get its text
            if (_currentMedicationResults is not null && _currentMedicationFocusIndex >= 0 && _currentMedicationFocusIndex < _currentMedicationResults.Count)
            {
                var match = _currentMedicationResults[_currentMedicationFocusIndex];
                if (match.Hyperlink?.IsFocused == true)
                {
                    // If only matches that are part of the original text should be considered,
                    // and this match doesn't have a match in the text, return null
                    if (onlyIfMatchInText && !match.HasMatchInText)
                    {
                        MessageBox.Show("The currently focused medication does not have a match in the original text. Please select a medication that is part of the original text, or change the focus to it.", "No match in text", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return null;
                    }

                    return match; // Return the focused match
                }
                else
                {
                    // If the hyperlink is not focused but the focus index was set,
                    // we can safely reset it to optimize future calls
                    _currentMedicationFocusIndex = -1; // Reset focus index
                }
            }

            return null;
        }

        private string? GetSelectedText(out int startIndex)
        {
            // Get selected text from RichTextBox
            var selectedText = MyRichTextBox.Selection.Text.Trim();
            
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                startIndex = -1; // Set start index to -1 to indicate no selection
                return null;
            }

            startIndex = new TextRange(MyRichTextBox.Document.ContentStart, MyRichTextBox.Selection.Start).Text.Length;

            // If you select text outside of the original text itself (e.g. text related to the non-matching medications at the end),
            // we consider that there is no valid selection for medication matches,
            // since those are just informational and not part of the original text.
            if (startIndex < 0 || _currentText is null || startIndex >= _currentText.Length)
            {
                startIndex = -1; // Set start index to -1 to indicate invalid selection
                return null;
            }

            return selectedText;
        }

        // Find the match by start index and selected text
        private MedicationResultUI? FindMedicationMatchFromSelectedText(string selectedText, int startIndex) =>
            _currentMedicationResults?.FirstOrDefault(m => m.HasMatchInText && m.StartIndex == startIndex && m.MatchInText == selectedText);

        // Find a match that's inside of the selected text but it's not the entire text
        // (to account for partial matches, this would make the partial match found a TP* instead of FN for the selection and FP for the match,
        // since the medication found was a substring of the selected text, so it is partially correct instead of completely incorrect)
        private List<MedicationResultUI>? FindPartialMedicationMatchesInsideSelectedText(string selectedText, int startIndex) =>
            _currentMedicationResults?.Where(m => m.HasMatchInText && m.StartIndex >= startIndex && (m.StartIndex + m.Length) <= (startIndex + selectedText.Length) && selectedText.Contains(m.MatchInText)).ToList();

        private void ResetMedicationValidation()
        {
            // Clear the RichTextBox and reset matches
            MyRichTextBox.Document.Blocks.Clear();
            _currentMedicationResults = null;
            _currentMedicationFocusIndex = -1; // Reset focus index

            // Clear progress bar and text
            ProgressBar.Value = 0;
            ProgressPercentageTextBlock.Text = "0%";
            ProgressRowsProcessedTextBlock.Text = "Rows processed: 0";

            IsParsing = false; // Set parsing state to false
        }

        private int _currentMedicationFocusIndex = -1;
        private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // If Ctrl+L is pressed, trigger the load button
            if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnLoad.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                return;
            }

            // If medication matches have not been loaded, do nothing
            if (_currentMedicationResults == null)
            {
                return;
            }

            // If Ctrl+Shift+S is pressed, stop the process
            if (e.Key == Key.S && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                BtnStop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true; // Prevent default ctrl+escape behavior
                return;
            }
            // If Ctrl+Alt+C is pressed, cancel the process
            else if (e.Key == Key.C && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt))
            {
                BtnCancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                return;
            }
            // If Tab key is pressed, navigate between medication matches
            else if (e.Key == Key.Tab)
            {
                if (_currentMedicationResults.Count == 0)
                {
                    return; // No matches to navigate
                }

                // Check if the Shift key is being held down (Shift+Tab)
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    // Shift+Tab: go backwards
                    // (note, we add Count to handle negative index wrap-around)
                    _currentMedicationFocusIndex = (_currentMedicationFocusIndex - 1 + _currentMedicationResults.Count) % _currentMedicationResults.Count;
                }
                else
                {
                    // Tab: go forwards
                    _currentMedicationFocusIndex = (_currentMedicationFocusIndex + 1) % _currentMedicationResults.Count;
                }

                var match = _currentMedicationResults[_currentMedicationFocusIndex];
                match.Hyperlink?.Focus();
                e.Handled = true; // Prevent default tab behavior
                return;
            }
            else if (e.Key == Key.Space)
            {
                // Get selected text
                var selectedText = MyRichTextBox.Selection.Text;
                if (!string.IsNullOrWhiteSpace(selectedText))
                {
                    // Search text in SNOMED
                    await SnomedSearchAndClick(selectedText.Trim());
                    e.Handled = true;
                }
                return;
            }
            // Handle other keys for actions (clicking buttons) without modifiers
            else
            {
                // If any modifiers are pressed, then maybe the user wants to perform a different action
                // (e.g. Ctrl+C to copy, Ctrl+V to paste, etc.),
                // so we only handle the keys if no modifiers are pressed
                if (Keyboard.Modifiers != ModifierKeys.None)
                {
                    return; // Do not handle keys with modifiers
                }

                if (e.Key == Key.T)
                {
                    BtnTrue.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.F)
                {
                    BtnFalse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.C)
                {
                    BtnCorrect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.N)
                {
                    BtnNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.B)
                {
                    BtnBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                    return;
                }
            }
        }

        private string? BrowseInputFile() => BrowseCsvFile<Microsoft.Win32.OpenFileDialog>();

        private string? BrowseOutputFile(string? inputFile = null)
        {
            string? defaultFileName = null;
            if (inputFile is not null)
            {
                defaultFileName = System.IO.Path.GetFileNameWithoutExtension(inputFile) + "_validated.csv";
            }
            return BrowseCsvFile<Microsoft.Win32.SaveFileDialog>(defaultFileName: defaultFileName);
        }

        private static string? BrowseCsvFile<TFileDialog>(string? currentPath = null, string? defaultFileName = null)
            where TFileDialog : Microsoft.Win32.FileDialog, new()
        {
            // Create a new file dialog
            var dialog = new TFileDialog()
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            };

            if (!string.IsNullOrEmpty(currentPath))
            {
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(currentPath);
                dialog.FileName = System.IO.Path.GetFileName(currentPath);
            }
            else if (defaultFileName is not null)
            {
                dialog.FileName = defaultFileName;
            }

            // Show the dialog
            if (dialog.ShowDialog() == true)
            {
                // Return the selected file
                return dialog.FileName;
            }

            return null;
        }

        private async Task OnMedicationClicked(MedicationResultUI match)
        {
            await SnomedSearchAndClick(match.DisplayText);
        }

        private async Task SnomedSearchAndClick(string text)
        {
            await SnomedSearchRobust(text);
            await SnomedClickFirstResult();
        }

        // Method 1: More robust JavaScript with multiple fallback strategies
        private async Task SnomedSearchRobust(string searchTerm)
        {
            var escapedSearch = searchTerm.Replace("'", "\\'").Replace("\"", "\\\"");

            var js = $@"
        (function() {{
            console.log('Starting search for: {escapedSearch}');
            
            // Try multiple selectors in case the ID changes
            var selectors = [
                'input[id*=""vaadin-text-field""]',
                'input[placeholder*=""search"" i]',
                'input[type=""text""]',
                'vaadin-text-field input',
                '#input-vaadin-text-field-3'
            ];
            
            var element = null;
            for (let selector of selectors) {{
                element = document.querySelector(selector);
                if (element) {{
                    console.log('Found element with selector: ' + selector);
                    break;
                }}
            }}
            
            if (!element) {{
                console.log('No search input found');
                return 'NOT_FOUND';
            }}
            
            // Clear existing value
            element.value = '';
            element.focus();
            
            // Set the value using multiple methods
            element.value = '{escapedSearch}';
            
            // Trigger React/Vue/Angular change detection
            var nativeInputValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
            nativeInputValueSetter.call(element, '{escapedSearch}');
            
            // Fire multiple events to ensure framework detection
            var events = ['input', 'change', 'keyup', 'blur'];
            events.forEach(eventType => {{
                var event = new Event(eventType, {{ bubbles: true, cancelable: true }});
                element.dispatchEvent(event);
            }});
            
            // Try keyboard events as well
            setTimeout(() => {{
                var enterEvent = new KeyboardEvent('keydown', {{
                    key: 'Enter',
                    code: 'Enter',
                    keyCode: 13,
                    which: 13,
                    bubbles: true
                }});
                element.dispatchEvent(enterEvent);
                
                // Also try the keypress event
                var keypressEvent = new KeyboardEvent('keypress', {{
                    key: 'Enter',
                    code: 'Enter',
                    keyCode: 13,
                    which: 13,
                    bubbles: true
                }});
                element.dispatchEvent(keypressEvent);
            }}, 100);
            
            return 'SUCCESS';
        }})();
        ";

            try
            {
                var result = await MyWebView.CoreWebView2.ExecuteScriptAsync(js);
                if (result == "\"NOT_FOUND\"")
                {
                    MessageBox.Show("Could not find search input on the page. The page structure may have changed.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error executing search: {ex.Message}");
            }
        }

        private async Task SnomedClickFirstResult()
        {
            // Example to show what this does:
            // Simpler 2-liner approach, left as a comment in case we want to make it simpler without the checks later
            // const firstResultSlotName = document.querySelector('vaadin-grid').shadowRoot.querySelector('tbody#items tr:first-child slot').name
            // document.querySelector('vaadin-grid-cell-content[slot=""' + firstResultSlotName + '""]').click()

            var js = @"
        (function() {
            console.log('Setting up listener for grid updates');
    
            var grid = document.querySelector('vaadin-grid');
            if (!grid) {
                console.log('No grid found (vaadin-grid)');
                return 'GRID_NOT_FOUND';
            }
    
            // Function to attempt clicking the first result
            function clickFirstResult() {
                console.log('Grid updated, attempting to click first result');
        
                var shadow = grid.shadowRoot;
                if (!shadow) {
                    console.log('No shadowRoot found in vaadin-grid');
                    return false;
                }
        
                var row = shadow.querySelector('tbody#items tr:first-child');
                if (!row) {
                    console.log('First row in tbody#items not found - no results');
                    return false;
                }
        
                var slot = row.querySelector('slot');
                if (!slot || !slot.name) {
                    console.log('slot not found, or found without name attribute');
                    return false;
                }
        
                var cellContent = document.querySelector('vaadin-grid-cell-content[slot=""' + slot.name + '""]');
                if (!cellContent) {
                    console.log('No vaadin-grid-cell-content found for slot: ' + slot.name);
                    return false;
                }

                if (!cellContent.innerText || cellContent.innerText.trim() === '') {
                    console.log('First result cell is empty');
                    return false;
                }

                console.log('result text: ' + cellContent.innerText);

                cellContent.click();
                console.log('Successfully clicked first result');
                return true;
            }
    
            // Set up a MutationObserver to watch for ANY changes in the grid
            var observer = new MutationObserver(function(mutations) {
                console.log('Grid mutation detected, starting polling for content');

                var attempts = 0;
                var maxAttempts = 25;

                function pollForContent() {
                    attempts++;
                    console.log('Polling attempt', attempts);

                    if (clickFirstResult()) {
                        observer.disconnect(); // Stop observing after successful click
                        console.log('SUCCESS - clicked first result');
                        return; // SUCCESS
                    } else if (attempts < maxAttempts) {
                        setTimeout(pollForContent, 200);
                    } else {
                        console.log('Polling timeout - content did not load within 5 seconds');
                        // Could optionally disconnect observer here or let it keep trying on future mutations
                    }
                }

                // Start polling with initial delay
                setTimeout(pollForContent, 100);
            });
    
            // Start observing the grid for any changes
            observer.observe(grid, {
                childList: true,
                subtree: true
            });
    
            // Also observe the shadow DOM if accessible
            if (grid.shadowRoot) {
                observer.observe(grid.shadowRoot, {
                    childList: true,
                    subtree: true
                });
            }
    
            // Set a timeout to clean up the observer
            setTimeout(function() {
                observer.disconnect();
            }, 10000); // 10 second timeout
    
            return 'LISTENER_SETUP';
        })();
        ";

            try
            {
                var result = await MyWebView.CoreWebView2.ExecuteScriptAsync(js);

                if (result == "\"GRID_NOT_FOUND\"")
                {
                    MessageBox.Show("Could not find the search grid.");
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error setting up search listener: {ex.Message}");
            }
        }
    }
}
