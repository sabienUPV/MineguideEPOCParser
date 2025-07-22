using MineguideEPOCParser.Core;
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
    public partial class MedicationManualValidatorControl : UserControl
    {
        public MedicationManualValidatorControl()
        {
            InitializeComponent();
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

            public MedicationMatch ToMedicationMatch()
            {
                return new MedicationMatch
                {
                    StartIndex = this.StartIndex,
                    Length = this.Length,
                    Text = this.Text,
                    OriginalMedication = this.OriginalMedication,
                    CorrectedMedication = this.CorrectedMedication
                };
            }
        }

        private string? _currentText;
        private List<MedicationMatchUI>? _currentMedicationMatches;

        // Enhanced version with clickable highlights for validation
        public void RedrawMedicationsText() => 
            HighlightMedicationsClickable(MyRichTextBox, OnMedicationClicked);

        public void RenderMedicationText(string text, string[] medications)
            => HighlightMedicationsClickable(MyRichTextBox, text, medications, OnMedicationClicked);

        public void HighlightMedicationsClickable(RichTextBox richTextBox, string text, string[] medications,
            Func<MedicationMatchUI, Task> onMedicationClick)
        {
            var matches = FindAllMedicationMatches(text, medications);
            var sortedMatches = matches.OrderBy(m => m.StartIndex).ToList();

            _currentText = text; // Store the current text for later use
            _currentMedicationMatches = sortedMatches; // Store matches for later use

            HighlightMedicationsClickable(richTextBox, onMedicationClick);
        }

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

                var correctedMedication = match.CorrectedMedication;
                var isCorrected = !string.IsNullOrWhiteSpace(correctedMedication) && correctedMedication != match.OriginalMedication;

                // Create clickable hyperlink for medication
                var hyperlink = new Hyperlink(new Run(match.Text))
                {
                    Foreground = new SolidColorBrush(Colors.DarkGreen),
                    Background = new SolidColorBrush(isCorrected ? Colors.YellowGreen : Colors.LightGreen),
                    FontWeight = FontWeights.Bold,
                    TextDecorations = null, // Remove underline for cleaner look
                    Focusable = true,
                    ToolTip = $"Original: {match.OriginalMedication}\nCorrected: {match.CorrectedMedication}"
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
            // Only handle middle click in the hyperlink
            if (sender is not Hyperlink hyperlink || e.ChangedButton != MouseButton.Middle)
            {
                return;
            }

            // If middle click is pressed, correct the medication match
            var match = _currentMedicationMatches?.FirstOrDefault(m => m.Hyperlink == hyperlink);
            if (match is not null)
            {
                CorrectMedication(match);
            }
            else 
            {
                MessageBox.Show("Medication match not found for the clicked hyperlink.");
            }

            e.Handled = true;
        }

        private List<MedicationMatchUI> FindAllMedicationMatches(string text, string[] medications)
        {
            var matches = new List<MedicationMatchUI>();
            var textLower = text.ToLower();

            foreach (string medication in medications)
            {
                var medicationLower = medication.ToLower();
                var startIndex = 0;

                while (true)
                {
                    var index = textLower.IndexOf(medicationLower, startIndex);
                    if (index == -1) break;

                    // Check for potential overlaps with existing matches
                    var actualText = text.Substring(index, medication.Length);

                    if (!HasOverlap(matches, index, medication.Length))
                    {
                        matches.Add(new MedicationMatchUI
                        {
                            StartIndex = index,
                            Length = medication.Length,
                            Text = actualText,
                            OriginalMedication = medication
                        });
                    }

                    startIndex = index + 1; // Move past this occurrence
                }
            }

            return matches;
        }

        private bool HasOverlap(List<MedicationMatchUI> existingMatches, int newStart, int newLength)
        {
            var newEnd = newStart + newLength - 1;

            return existingMatches.Any(match =>
            {
                var existingEnd = match.StartIndex + match.Length - 1;

                // Check if ranges overlap
                return !(newEnd < match.StartIndex || newStart > existingEnd);
            });
        }

        // Usage examples

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
            };

            try
            {
                // Start the parser (with cancellation support)
                await parser.ParseData(_cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show($"The validation process was cancelled.\nThe information that was already validated has been written to the output file.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private SemaphoreSlim? _medicationValidationSemaphore;
        private async Task<string[]> ValidateMedications(string text, string[] medications, CancellationToken cancellationToken)
        {
            RenderMedicationText(text, medications);

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

                // Return validated medications, or original if not set
                return _currentMedicationMatches?.Select(m => m.CorrectedMedication).ToArray() ?? medications;
            }
            finally
            {
                semaphore?.Dispose();
                _medicationValidationSemaphore = null; // Siempre se limpia, incluso si hay excepción
            }
        }

        // Add medication button click handler
        private void OnAddMedicationClicked(object? sender, RoutedEventArgs e)
        {
            // Initialize the validated medications list if it is null
            _currentMedicationMatches ??= [];

            // Get selected text from RichTextBox
            var selectedText = GetSelectedText(out int startIndex);
            if (selectedText == null)
            {
                return; // No valid selection, exit
            }
            // Create a new MedicationMatchUI for the selected text
            var newMatch = new MedicationMatchUI
            {
                StartIndex = startIndex,
                Length = selectedText.Length,
                Text = selectedText,
                OriginalMedication = string.Empty, // Set as empty to symbolize that it didn't exist before the user validation
                CorrectedMedication = selectedText // Use the selected text as the original medication
            };

            // Add the selected text to the validated medications list
            _currentMedicationMatches.Add(newMatch);

            // Redraw the RichTextBox with updated highlights
            RedrawMedicationsText();
        }

        private void OnCorrectMedicationClicked(object? sender, RoutedEventArgs e)
        {
            if (_currentMedicationMatches == null || _currentMedicationMatches.Count == 0)
            {
                MessageBox.Show("No medications to correct.");
                return; // No matches to correct, exit
            }

            // Get focused medication match
            MedicationMatchUI? selectedMatch = GetFocusedMedication();
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
                selectedMatch = _currentMedicationMatches.FirstOrDefault(m => m.StartIndex == startIndex && m.Text == selectedText);
                if (selectedMatch is null)
                {
                    MessageBox.Show($"Medication '{selectedText}' not found in the validated list.");
                    return; // No match found, exit
                }
            }

            // Correct the selected medication
            CorrectMedication(selectedMatch);
        }

        private void CorrectMedication(MedicationMatchUI medicationMatch)
        {
            // Prompt user for corrected medication
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter the corrected medication name:",
                "Correct Medication",
                medicationMatch.CorrectedMedication);
            if (string.IsNullOrWhiteSpace(input))
            {
                MessageBox.Show("No correction entered. Operation cancelled.");
                return; // No valid input, exit
            }
            // Update the corrected medication
            medicationMatch.CorrectedMedication = input.Trim();
            // Redraw the RichTextBox with updated highlights
            RedrawMedicationsText();
        }

        // Remove medication button click handler
        private void OnRemoveMedicationClicked(object? sender, RoutedEventArgs e)
        {
            if (_currentMedicationMatches == null || _currentMedicationMatches.Count == 0)
            {
                MessageBox.Show("No medications to remove.");
                return; // No matches to remove, exit
            }

            // Get selected medication
            MedicationMatchUI? selectedMatch = GetFocusedMedication();

            if (selectedMatch is null)
            {
                // Get selected text from RichTextBox
                var selectedText = GetSelectedText(out int startIndex);
                if (selectedText is null)
                {
                    MessageBox.Show("Please select a medication to remove.");
                    return; // No valid selection, exit
                }
                // Find the match by start index and selected text
                selectedMatch = _currentMedicationMatches.FirstOrDefault(m => m.StartIndex == startIndex && m.Text == selectedText);
                if (selectedMatch is null)
                {
                    MessageBox.Show($"Medication '{selectedText}' not found in the validated list.");
                    return; // No match found, exit
                }
            }

            // Remove the selected text from the validated medications list
            if (_currentMedicationMatches?.Remove(selectedMatch) == true)
            {
                // Redraw the RichTextBox with updated highlights
                if (_currentMedicationMatches != null)
                {
                    RedrawMedicationsText();
                }
            }
            else
            {
                MessageBox.Show($"Medication '{selectedMatch.CorrectedMedication}' not found in the validated list.");
            }
        }

        private MedicationMatchUI? GetFocusedMedication()
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
                MessageBox.Show("Please select some text.");
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

            // Clear the RichTextBox and reset matches
            MyRichTextBox.Document.Blocks.Clear();
            _currentMedicationMatches = null;
            _currentMedicationFocusIndex = -1; // Reset focus index
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
            // Handle other keys for actions (clicking buttons)
            else if (e.Key == Key.A)
            {
                BtnAdd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.C)
            {
                BtnCorrect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.R)
            {
                BtnRemove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

        private string? BrowseInputFile() => BrowseCsvFile<Microsoft.Win32.OpenFileDialog>();

        private string? BrowseOutputFile(string? inputFile = null)
        {
            string? defaultFileName = null;
            if (inputFile is not null)
            {
                defaultFileName = System.IO.Path.GetFileNameWithoutExtension(inputFile) + "_gold.csv";
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
            await SnomedSearchAndClick(match.Text);
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
