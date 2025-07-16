using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

            InitializeValidateMedicationExtraction();

            InitializeWebView();
        }

        private void InitializeWebView()
        {
            // Hide loading text when website loads
            myWebView.NavigationCompleted += OnNavigationCompleted;
        }

        private void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                myWebViewLoadingText.Visibility = Visibility.Collapsed;
            }
            else
            {
                myWebViewLoadingText.Text = "Failed to load website.";
            }

            myWebView.NavigationCompleted -= OnNavigationCompleted; // Unsubscribe to avoid multiple calls
        }

        public class MedicationMatch
        {
            public int StartIndex { get; set; }
            public int Length { get; set; }
            public required string Text { get; set; }
            public required string OriginalMedication { get; set; } // The medication from your array
        }

        // Enhanced version with clickable highlights for validation
        public void HighlightMedicationsClickable(RichTextBox richTextBox, string text, string[] medications,
            Func<MedicationMatch, Task> onMedicationClick)
        {
            var matches = FindAllMedicationMatches(text, medications);
            var sortedMatches = matches.OrderBy(m => m.StartIndex).ToList();

            richTextBox.Document.Blocks.Clear();
            var paragraph = new Paragraph();
            var currentIndex = 0;

            foreach (var match in sortedMatches)
            {
                // Add normal text
                if (match.StartIndex > currentIndex)
                {
                    var normalText = text.Substring(currentIndex, match.StartIndex - currentIndex);
                    paragraph.Inlines.Add(new Run(normalText));
                }

                // Create clickable hyperlink for medication
                var hyperlink = new Hyperlink(new Run(match.Text))
                {
                    Foreground = new SolidColorBrush(Colors.DarkGreen),
                    Background = new SolidColorBrush(Colors.LightGreen),
                    FontWeight = FontWeights.Bold,
                    TextDecorations = null // Remove underline for cleaner look
                };

                // Capture the match in the closure
                var currentMatch = match;
                hyperlink.Click += async (s, e) => await onMedicationClick(currentMatch);

                paragraph.Inlines.Add(hyperlink);
                currentIndex = match.StartIndex + match.Length;
            }

            // Add remaining text
            if (currentIndex < text.Length)
            {
                var remainingText = text.Substring(currentIndex);
                paragraph.Inlines.Add(new Run(remainingText));
            }

            richTextBox.Document.Blocks.Add(paragraph);
        }

        private List<MedicationMatch> FindAllMedicationMatches(string text, string[] medications)
        {
            var matches = new List<MedicationMatch>();
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
                        matches.Add(new MedicationMatch
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

        private bool HasOverlap(List<MedicationMatch> existingMatches, int newStart, int newLength)
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

        private const string sampleText = "Patient needs Lisinopril 10mg, then Aspirin 81mg daily, and also Metformin 500mg twice daily.";
        private static readonly string[] extractedMedications = ["Aspirin", "Metformin", "Lisinopril"]; // LLM extracted these

        private void InitializeValidateMedicationExtraction()
        {
            btnLoad.Click += LoadMedications;
        }

        private void LoadMedications(object? sender, RoutedEventArgs args)
        {
            // Option 2: Clickable for validation
            HighlightMedicationsClickable(myRichTextBox, sampleText, extractedMedications, OnMedicationClicked);
        }

        private async Task OnMedicationClicked(MedicationMatch match)
        {
            await SnomedSearchRobust(match.Text);
            await SnomedClickFirstResult();

            //// Show validation dialog or inline editor
            //var result = MessageBox.Show(
            //    $"Validate medication: '{match.Text}'\n\nOriginal: {match.OriginalMedication}\nPosition: {match.StartIndex}\n\nIs this correct?",
            //    "Validate Medication",
            //    MessageBoxButton.YesNoCancel);

            //if (result == MessageBoxResult.No)
            //{
            //    // Show correction dialog or highlight for manual correction
            //    // You could open an inline editor or popup here
            //}
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
                var result = await myWebView.CoreWebView2.ExecuteScriptAsync(js);
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
                var result = await myWebView.CoreWebView2.ExecuteScriptAsync(js);

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
