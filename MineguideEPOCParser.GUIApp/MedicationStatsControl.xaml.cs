using Microsoft.Win32;
using MineguideEPOCParser.Core.Parsers.Configurations;
using MineguideEPOCParser.Core.Tools;
using MineguideEPOCParser.Core.Validation;
using System.Windows;
using System.Windows.Controls;

namespace MineguideEPOCParser.GUIApp
{
    public partial class MedicationStatsControl : UserControl
    {
        public MedicationStatsControl()
        {
            InitializeComponent();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InputFileTextBox.Text = openFileDialog.FileName;
            }
        }

        private async void CalculateButton_Click(object sender, RoutedEventArgs e)
        {
            var filePath = InputFileTextBox.Text;
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            {
                MessageBox.Show("Please select a valid CSV file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            CalculateButton.IsEnabled = false;
            try
            {
                var config = new MedicationManualValidatorParserConfiguration
                {
                    CultureName = MedicationManualValidatorControl.DefaultCultureName, // TODO: Allow user to select culture if needed
                    InputFile = filePath,
                    OutputFile = string.Empty,
                    ValidationFunction = (s, m, c) => Task.FromResult(Array.Empty<MedicationResult>()) // Not used here
                };

                var stats = await MedicationExperimentStatsCalculator.CalculateStatsAsync(filePath, config);

                TPTextBox.Text = stats.TP.ToString();
                TPStarTextBox.Text = stats.TPStar.ToString();
                FPTextBox.Text = stats.FP.ToString();
                FNTextBox.Text = stats.FN.ToString();
                HallucinationsTextBox.Text = stats.Hallucinations.ToString();

                PrecisionTextBox.Text = stats.Precision.ToString("F4");
                PrecisionPercentageTextBox.Text = stats.Precision.ToString("P4");

                RecallTextBox.Text = stats.Recall.ToString("F4");
                RecallPercentageTextBox.Text = stats.Recall.ToString("P4");

                F1ScoreTextBox.Text = stats.F1Score.ToString("F4");
                F1ScorePercentageTextBox.Text = stats.F1Score.ToString("P4");

                ResultsGrid.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error calculating stats: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CalculateButton.IsEnabled = true;
            }
        }
    }
}
