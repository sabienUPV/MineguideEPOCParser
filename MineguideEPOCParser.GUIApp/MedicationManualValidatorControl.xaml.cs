using MineguideEPOCParser.Core;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MineguideEPOCParser.GUIApp
{
    /// <summary>
    /// Lógica de interacción para MedicationManualValidatorControl.xaml
    /// </summary>
    public partial class MedicationManualValidatorControl : UserControl, IAsyncDisposable
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
        }

        public async ValueTask DisposeAsync()
        {
            await StopMedicationValidation();
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

        public class MedicationMatchUI : MedicationMatch
        {
            public Hyperlink? Hyperlink { get; set; } // Optional hyperlink for clickable highlights

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

            public MedicationMatch ToMedicationMatch()
            {
                return new MedicationMatch
                {
                    StartIndex = this.StartIndex,
                    Length = this.Length,
                    MatchInText = this.MatchInText,
                    ExtractedMedication = this.ExtractedMedication,
                    ExperimentResult = this.ExperimentResult,
                    CorrectedMedication = this.CorrectedMedication
                };
            }

            public static MedicationMatchUI FromMedicationMatch(MedicationMatch match, Hyperlink? hyperlink = null)
            {
                return new MedicationMatchUI
                {
                    StartIndex = match.StartIndex,
                    Length = match.Length,
                    MatchInText = match.MatchInText,
                    ExtractedMedication = match.ExtractedMedication,
                    ExperimentResult = match.ExperimentResult,
                    CorrectedMedication = match.CorrectedMedication,
                    Hyperlink = hyperlink
                };
            }
        }

        private string? _currentText;
        private List<MedicationMatchUI>? _currentMedicationMatches;

        public void LoadMedicationMatches(string text, IEnumerable<MedicationMatch> medicationMatches)
        {
            var sortedMatches = medicationMatches
                .Select(m => MedicationMatchUI.FromMedicationMatch(m))
                .OrderBy(m => m.StartIndex)
                .ToList();

            _currentText = text; // Store the current text for later use
            _currentMedicationMatches = sortedMatches; // Store matches for later use
        }

        // Enhanced version with clickable highlights for validation
        public void RenderMedicationsText() => 
            HighlightMedicationsClickable(MyRichTextBox, OnMedicationClicked);

        public void HighlightMedicationsClickable(RichTextBox richTextBox, Func<MedicationMatchUI, Task> onMedicationClick)
        {
            if (_currentText == null)
            {
                richTextBox.Document.Blocks.Clear();
                return; // No text or matches to highlight
            }

            if (_currentMedicationMatches == null || _currentMedicationMatches.Count == 0)
            {
                // Just display the text without highlights
                richTextBox.Document.Blocks.Clear();
                richTextBox.Document.Blocks.Add(new Paragraph(new Run(_currentText)));
                return; // No matches to highlight
            }

            richTextBox.Document.Blocks.Clear();
            var paragraph = new Paragraph();
            var currentIndex = 0;

            // Tabs might be used as newline replacements in the text so they were single lines for further processing,
            // so we replace them back with newlines for better readability in the RichTextBox
            var textToVisualize = _currentText.Replace("\t", "\n");

            foreach (var match in _currentMedicationMatches)
            {
                // Add normal text
                if (match.StartIndex > currentIndex)
                {
                    var normalText = textToVisualize.Substring(currentIndex, match.StartIndex - currentIndex);
                    paragraph.Inlines.Add(new Run(normalText));
                }

                // Create tooltip with match details
                var tooltipBuilder = new StringBuilder();
                tooltipBuilder.AppendLine($"Text: {match.MatchInText}");
                tooltipBuilder.AppendLine($"Extracted medication (from LLM): {match.ExtractedMedication}");
                tooltipBuilder.Append($"Validated experiment result: {match.ExperimentResult.ToResultString()}");
                if (!string.IsNullOrEmpty(match.CorrectedMedication))
                {
                    tooltipBuilder.AppendLine();
                    tooltipBuilder.Append($"Corrected medication: {match.CorrectedMedication}");
                }

                // Create clickable hyperlink for medication
                var hyperlink = new Hyperlink(new Run(match.MatchInText))
                {
                    Foreground = new SolidColorBrush(match.ForegroundColor),
                    Background = new SolidColorBrush(match.BackgroundColor),
                    FontWeight = FontWeights.Bold,
                    TextDecorations = string.IsNullOrEmpty(match.CorrectedMedication) ? null : TextDecorations.Underline,
                    Focusable = true,
                    ToolTip = tooltipBuilder.ToString(),
                };

                // Capture the match in the closure
                var currentMatch = match;
                currentMatch.Hyperlink = hyperlink;
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
                    await onMedicationClick(currentMatch);
                };
                hyperlink.PreviewKeyDown += Hyperlink_PreviewKeyDown;
                hyperlink.PreviewMouseDown += Hyperlink_PreviewMouseDown;

                paragraph.Inlines.Add(hyperlink);
                currentIndex = match.StartIndex + match.Length;
            }

            // Add remaining text
            if (currentIndex < textToVisualize.Length)
            {
                var remainingText = textToVisualize.Substring(currentIndex);
                paragraph.Inlines.Add(new Run(remainingText));
            }

            richTextBox.Document.Blocks.Add(paragraph);
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
            var match = _currentMedicationMatches?.FirstOrDefault(m => m.Hyperlink == hyperlink);
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

        public const string DefaultCultureName = "es-ES";

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
                CultureName = DefaultCultureName,
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

                // Show success message
                MessageBox.Show($"Medication validation completed successfully.\nThe validated medications have been written to: {outputFile}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                // Reset validation (set parsing to false, clear the RichTextBox, and reset matches)
                ResetMedicationValidation();
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show($"The validation process was cancelled.\nThe information that was already validated has been written to the output file.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private SemaphoreSlim? _medicationValidationSemaphore;
        private async Task<MedicationMatch[]> ValidateMedications(string text, IEnumerable<MedicationMatch> medicationMatches, CancellationToken cancellationToken)
        {
            LoadMedicationMatches(text, medicationMatches);

            if (_currentMedicationMatches is null)
            {
                throw new InvalidOperationException("No medication matches loaded. Please load medications first.");
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

                cancellationToken.ThrowIfCancellationRequested();

                return _currentMedicationMatches.Select(m => m.ToMedicationMatch()).ToArray();
            }
            finally
            {
                semaphore?.Dispose();
                _medicationValidationSemaphore = null; // Siempre se limpia, incluso si hay excepción
            }
        }

        // True medication button click handler
        private void OnTrueMedicationClicked(object? sender, RoutedEventArgs e)
        {
            // Get focused medication match
            MedicationMatchUI? selectedMatch = GetFocusedMedicationMatch();

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
                selectedMatch = _currentMedicationMatches?.FirstOrDefault(m => m.StartIndex == startIndex && m.MatchInText == selectedText);

                if (selectedMatch is null)
                {
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

        private static void MarkMedicationAsTruePositive(MedicationMatchUI match)
        {
            if (match.ExperimentResult != MedicationMatch.ExperimentResultType.FP)
            {
                // If it isn't a false positive, either it is already a true positive,
                // or it is a false negative, which shouldn't be able to change into a true positive
                if (match.ExperimentResult == MedicationMatch.ExperimentResultType.FN)
                {
                    MessageBox.Show("A false negative (FN) medication match cannot be marked as true positive (TP).");
                }
                return;
            }

            if (match.MatchInText == match.ExtractedMedication)
            {
                // Change the experiment result to TP
                match.ExperimentResult = MedicationMatch.ExperimentResultType.TP;
            }
            else
            {
                // Change the experiment result to TP*, since the match is not exact
                match.ExperimentResult = MedicationMatch.ExperimentResultType.TP_;
            }
        }

        private MedicationMatchUI AddFalseNegativeMedication(string selectedText, int startIndex)
        {
            // Initialize the validated medications list if it is null
            _currentMedicationMatches ??= [];

            // Create a new MedicationMatchUI for the selected text
            var newMatch = new MedicationMatchUI
            {
                StartIndex = startIndex,
                Length = selectedText.Length,
                MatchInText = selectedText,
                ExtractedMedication = selectedText,
                // We are manually adding medication that hadn't been extracted by the LLM, so it's a False Negative
                ExperimentResult = MedicationMatch.ExperimentResultType.FN
            };

            // Add the selected text to the validated medications list
            // (making sure it's still sorted by start index,
            // since our code assumes that the matches are sorted)
            _currentMedicationMatches.AddSorted(newMatch, MedicationMatch.Comparer);

            // Return the new match for further processing if needed
            return newMatch;
        }

        // False positive medication button click handler
        private void OnFalseMedicationClicked(object? sender, RoutedEventArgs e)
        {
            if (_currentMedicationMatches == null || _currentMedicationMatches.Count == 0)
            {
                MessageBox.Show("No medications to mark as false positives.");
                return; // No matches to mark as false positives, exit
            }

            // Get selected medication
            MedicationMatchUI? selectedMatch = GetFocusedMedicationMatch();

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
                selectedMatch = _currentMedicationMatches.FirstOrDefault(m => m.StartIndex == startIndex && m.MatchInText == selectedText);
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

        private void MarkMedicationAsFalsePositive(MedicationMatchUI match)
        {
            if (match.ExperimentResult == MedicationMatch.ExperimentResultType.FN)
            {
                // If it is a false negative, and you are marking it as "false positive",
                // that means it wasn't a match in the first place, so remove it from the matches
                _currentMedicationMatches?.Remove(match);
                return;
            }

            // Change the experiment result to FP
            match.ExperimentResult = MedicationMatch.ExperimentResultType.FP;
        }

        private void OnCorrectMedicationClicked(object? sender, RoutedEventArgs e)
        {
            if (_currentMedicationMatches == null || _currentMedicationMatches.Count == 0)
            {
                MessageBox.Show("No medications to correct.");
                return; // No matches to correct, exit
            }

            // Get focused medication match
            MedicationMatchUI? selectedMatch = GetFocusedMedicationMatch();
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
                selectedMatch = _currentMedicationMatches.FirstOrDefault(m => m.StartIndex == startIndex && m.MatchInText == selectedText);
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

        private static void CorrectMedication(MedicationMatchUI medicationMatch)
        {
            // Prompt user for corrected medication
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                $"Extracted medication (from LLM): {medicationMatch.ExtractedMedication}\n\nEnter the corrected medication name:",
                $"Correct Medication ({medicationMatch.ExtractedMedication})",
                medicationMatch.CorrectedMedication ?? string.Empty);
            if (string.IsNullOrWhiteSpace(input))
            {
                MessageBox.Show("No correction entered. Operation cancelled.");
                return; // No valid input, exit
            }
            // Update the corrected medication
            medicationMatch.CorrectedMedication = input.Trim();
        }

        private void FocusMedicationMatch(MedicationMatchUI match)
        {
            // If the match has a hyperlink, focus it
            if (match.Hyperlink == null)
            {
                throw new InvalidOperationException("The medication match does not have a hyperlink to focus.");
            }

            if (_currentMedicationMatches is null)
            {
                throw new InvalidOperationException("No medication matches loaded. Please load medications first.");
            }

            var focusIndex = _currentMedicationMatches.IndexOf(match);
            if (focusIndex < 0)
            {
                throw new InvalidOperationException("The medication match is not found in the current matches list.");
            }

            match.Hyperlink.Focus();
            _currentMedicationFocusIndex = focusIndex; // Update focus index
        }

        private MedicationMatchUI? GetFocusedMedicationMatch()
        {
            // If a hyperlink is focused, get its text
            if (_currentMedicationMatches is not null && _currentMedicationFocusIndex >= 0 && _currentMedicationFocusIndex < _currentMedicationMatches.Count)
            {
                var match = _currentMedicationMatches[_currentMedicationFocusIndex];
                if (match.Hyperlink?.IsFocused == true)
                {
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
            return selectedText;
        }

        // Call this method when the user clicks "Next" or "Finish" after validation
        private void OnUserFinishedMedicationValidation(object? sender, RoutedEventArgs e) => OnUserFinishedMedicationValidation();
        private void OnUserFinishedMedicationValidation()
        {
            _medicationValidationSemaphore?.Release();
        }

        private async void StopMedicationValidation(object sender, RoutedEventArgs e) => await StopMedicationValidation();

        private async Task StopMedicationValidation()
        {
            // Cancel any ongoing parsing operation
            if (_cancellationTokenSource is not null)
            {
                await _cancellationTokenSource.CancelAsync();
            }

            ResetMedicationValidation();
        }
        private void ResetMedicationValidation()
        {
            // Clear the RichTextBox and reset matches
            MyRichTextBox.Document.Blocks.Clear();
            _currentMedicationMatches = null;
            _currentMedicationFocusIndex = -1; // Reset focus index

            // Clear progress bar and text
            ProgressBar.Value = 0;
            ProgressPercentageTextBlock.Text = "0%";
            ProgressRowsProcessedTextBlock.Text = "Rows processed: 0";

            IsParsing = false; // Set parsing state to false
        }

        private int _currentMedicationFocusIndex = -1;
        private async void MyRichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // If Ctrl+L is pressed, trigger the load button
            if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnLoad.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                return;
            }

            // If medication matches have not been loaded, do nothing
            if (_currentMedicationMatches == null)
            {
                return;
            }

            // If Ctrl+Shift+S is pressed, clear the RichTextBox and reset matches
            if (e.Key == Key.S && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                await StopMedicationValidation();
                e.Handled = true; // Prevent default ctrl+escape behavior
                return;
            }
            // If Tab key is pressed, navigate between medication matches
            else if (e.Key == Key.Tab)
            {
                if (_currentMedicationMatches.Count == 0)
                {
                    return; // No matches to navigate
                }

                // Check if the Shift key is being held down (Shift+Tab)
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    // Shift+Tab: go backwards
                    // (note, we add Count to handle negative index wrap-around)
                    _currentMedicationFocusIndex = (_currentMedicationFocusIndex - 1 + _currentMedicationMatches.Count) % _currentMedicationMatches.Count;
                }
                else
                {
                    // Tab: go forwards
                    _currentMedicationFocusIndex = (_currentMedicationFocusIndex + 1) % _currentMedicationMatches.Count;
                }

                var match = _currentMedicationMatches[_currentMedicationFocusIndex];
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

        private async Task OnMedicationClicked(MedicationMatchUI match)
        {
            await SnomedSearchAndClick(match.MatchInText);
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
