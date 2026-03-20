using System.Windows;

namespace MineguideEPOCParser.GUIApp
{
    public partial class InputBoxWindow : Window
    {
        // Property to hold the final result
        public string InputResult { get; private set; } = string.Empty;

        // Private constructor so it can only be called via the static Show method
        private InputBoxWindow(string prompt, string title, string defaultResponse)
        {
            InitializeComponent();
            
            PromptText.Text = prompt;
            this.Title = title;
            InputTextBox.Text = defaultResponse;
            
            // Highlight default text so the user can easily overwrite it
            InputTextBox.SelectAll();
            InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputResult = InputTextBox.Text;
            this.DialogResult = true; // This closes the window and returns true to ShowDialog()
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // This closes the window and returns false
        }

        /// <summary>
        /// The static method that acts just like Microsoft.VisualBasic.Interaction.InputBox
        /// </summary>
        public static string Show(string prompt, string title = "", string defaultResponse = "")
        {
            var window = new InputBoxWindow(prompt, title, defaultResponse);
            
            bool? result = window.ShowDialog();
            
            if (result == true)
            {
                return window.InputResult;
            }
            
            // If the user clicks Cancel or closes the window, VB InputBox returns an empty string.
            // We mimic that behavior here.
            return string.Empty; 
        }
    }
}