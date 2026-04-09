using Microsoft.Win32;
using MineguideEPOCParser.Core.Tools;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MineguideEPOCParser.GUIApp.Tools
{
    public partial class MedicationStatsControl : UserControl
    {
        public static readonly NumberFormatInfo NumberFormat = CultureInfo.InvariantCulture.NumberFormat;

        private readonly ObservableCollection<MedicationStatRow> _allRows = [];
        private readonly ICollectionView? _filteredView;
        private MedicationExperimentStats? _aggregateStats;

        public MedicationStatsControl()
        {
            InitializeComponent();
            _filteredView = CollectionViewSource.GetDefaultView(_allRows);
            _filteredView.Filter = FilterRows;
            DetailsDataGrid.ItemsSource = _filteredView;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InputFileTextBox.Text = string.Join("|", openFileDialog.FileNames);
            }
        }

        private async void CalculateButton_Click(object sender, RoutedEventArgs e)
        {
            var filePathsStr = InputFileTextBox.Text;
            if (string.IsNullOrWhiteSpace(filePathsStr))
            {
                MessageBox.Show("Please select one or more valid CSV files.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var filePaths = filePathsStr.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var existingFiles = filePaths.Where(f => System.IO.File.Exists(f)).ToList();

            if (existingFiles.Count == 0)
            {
                MessageBox.Show("None of the selected files exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            CalculateButton.IsEnabled = false;
            try
            {
                _aggregateStats = new MedicationExperimentStats();
                _allRows.Clear();

                foreach (var filePath in existingFiles)
                {
                    var stats = await MedicationExperimentStatsCalculator.CalculateStatsAsync(filePath, new()
                    {
                        CultureName = MedicationManualValidatorControl.DefaultCultureName, // TODO: Allow user to select culture if needed
                        InputFile = filePath,
                        OutputFile = string.Empty
                    });

                    _aggregateStats.TP += stats.TP;
                    _aggregateStats.TPStar += stats.TPStar;
                    _aggregateStats.FP += stats.FP;
                    _aggregateStats.FN += stats.FN;
                    _aggregateStats.MorphologicalHallucinations += stats.MorphologicalHallucinations;
                    _aggregateStats.GenerativeTypos += stats.GenerativeTypos;
                    _aggregateStats.UnderExtractions += stats.UnderExtractions;
                    _aggregateStats.OverExtractions += stats.OverExtractions;
                    _aggregateStats.SemanticAmbiguities += stats.SemanticAmbiguities;

                    foreach (var row in stats.Rows)
                    {
                        _allRows.Add(row);
                    }
                }

                TPTextBox.Text = _aggregateStats.TP.ToString();
                TPStarTextBox.Text = _aggregateStats.TPStar.ToString();
                TPExactPlusFuzzyTextBox.Text = _aggregateStats.TPRelaxed.ToString();
                FPTextBox.Text = _aggregateStats.FP.ToString();
                FNTextBox.Text = _aggregateStats.FN.ToString();

                UpdateMetricsDisplay();
                UpdateErrorAnalysisDisplay();

                UpdateTotalCounts();
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

        private void EvaluationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateMetricsDisplay();
        }

        private void ShowPercentagesCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (ShowPercentagesCheckBox == null) return;

            var visibility = ShowPercentagesCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            PrecisionPercentageTextBox.Visibility = visibility;
            RecallPercentageTextBox.Visibility = visibility;
            F1ScorePercentageTextBox.Visibility = visibility;
            BoundaryErrorsPercentageTextBox.Visibility = visibility;
            SemanticAmbiguitiesPercentageTextBox.Visibility = visibility;
            UnderExtractionsPercentageTextBox.Visibility = visibility;
            OverExtractionsPercentageTextBox.Visibility = visibility;
            EntityMergingErrorsPercentageTextBox.Visibility = visibility;
            HallucinationsPercentageTextBox.Visibility = visibility;
            MorphHallucinationsPercentageTextBox.Visibility = visibility;
            GenTyposPercentageTextBox.Visibility = visibility;
        }

        private void UpdateMetricsDisplay()
        {
            if (_aggregateStats == null || PrecisionTextBox == null) return;

            bool strict = EvaluationModeComboBox.SelectedIndex == 0; // Strict is first

            double precision = strict ? _aggregateStats.StrictPrecision : _aggregateStats.RelaxedPrecision;
            double recall = strict ? _aggregateStats.StrictRecall : _aggregateStats.RelaxedRecall;
            double f1Score = strict ? _aggregateStats.StrictF1Score : _aggregateStats.RelaxedF1Score;

            // For the metrics we use more precise formatting (4 decimal places) and also show the percentage format for better readability
            const string metricFormat = "F4"; // 4 decimal places for metrics, as they can be close together and we want to show small differences
            const string percentageFormat = "P4"; // 4 decimal places for percentages to match the precision of the metrics

            PrecisionTextBox.Text = precision.ToString(metricFormat, NumberFormat);
            PrecisionPercentageTextBox.Text = precision.ToString(percentageFormat, NumberFormat);

            RecallTextBox.Text = recall.ToString(metricFormat, NumberFormat);
            RecallPercentageTextBox.Text = recall.ToString(percentageFormat, NumberFormat);

            F1ScoreTextBox.Text = f1Score.ToString(metricFormat, NumberFormat);
            F1ScorePercentageTextBox.Text = f1Score.ToString(percentageFormat, NumberFormat);
        }

        private void UpdateErrorAnalysisDisplay()
        {
            if (_aggregateStats == null) return;

            int totalErrors = _aggregateStats.TotalErrorsForAnalysis;

            // For error analysis, we don't need to be as precise as for metrics, so we can use 2 decimal places for percentages.
            // The absolute counts are more important here, but the percentages help to understand the distribution of error types.
            const string percentageFormat = "P2"; // 2 decimal places for error type percentages

            // Percentages are based on the total of all error types (for pie chart analysis)

            // Categories

            // Boundary errors
            var totalBoundaryErrors = _aggregateStats.BoundaryErrors;
            BoundaryErrorsTextBox.Text = totalBoundaryErrors.ToString();
            double boundaryErrorsPerc = totalErrors == 0 ? 0 : (double)totalBoundaryErrors / totalErrors;
            BoundaryErrorsPercentageTextBox.Text = boundaryErrorsPerc.ToString(percentageFormat, NumberFormat);

            // Total hallucinations
            var totalHallucinations = _aggregateStats.Hallucinations;
            HallucinationsErrorTextBox.Text = totalHallucinations.ToString();
            double hallucinationPerc = totalErrors == 0 ? 0 : (double)totalHallucinations / totalErrors;
            HallucinationsPercentageTextBox.Text = hallucinationPerc.ToString(percentageFormat, NumberFormat);

            // Semantic Ambiguity
            SemanticAmbiguitiesTextBox.Text = _aggregateStats.SemanticAmbiguities.ToString();
            double semanticPerc = totalErrors == 0 ? 0 : (double)_aggregateStats.SemanticAmbiguities / totalErrors;
            SemanticAmbiguitiesPercentageTextBox.Text = semanticPerc.ToString(percentageFormat, NumberFormat);

            // Subcategories (percentages based per category)

            // Boundary errors

            // Under-extraction
            UnderExtractionsTextBox.Text = _aggregateStats.UnderExtractions.ToString();
            double underExtractionPerc = totalBoundaryErrors == 0 ? 0 : (double)_aggregateStats.UnderExtractions / totalBoundaryErrors;
            UnderExtractionsPercentageTextBox.Text = underExtractionPerc.ToString(percentageFormat, NumberFormat);

            // Over-extraction
            OverExtractionsTextBox.Text = _aggregateStats.OverExtractions.ToString();
            double overExtractionPerc = totalBoundaryErrors == 0 ? 0 : (double)_aggregateStats.OverExtractions / totalBoundaryErrors;
            OverExtractionsPercentageTextBox.Text = overExtractionPerc.ToString(percentageFormat, NumberFormat);

            // Entity Merging Errors
            EntityMergingErrorsTextBox.Text = _aggregateStats.EntityMergingErrors.ToString();
            double entityMergingPerc = totalBoundaryErrors == 0 ? 0 : (double)_aggregateStats.EntityMergingErrors / totalBoundaryErrors;
            EntityMergingErrorsPercentageTextBox.Text = entityMergingPerc.ToString(percentageFormat, NumberFormat);

            // Hallucinations

            // Morph. hallucinations
            MorphHallucinationsErrorTextBox.Text = _aggregateStats.MorphologicalHallucinations.ToString();
            double morphHallucinationPerc = totalHallucinations == 0 ? 0 : (double)_aggregateStats.MorphologicalHallucinations / totalHallucinations;
            MorphHallucinationsPercentageTextBox.Text = morphHallucinationPerc.ToString(percentageFormat, NumberFormat);

            // Gen. Typos
            GenTyposErrorTextBox.Text = _aggregateStats.GenerativeTypos.ToString();
            double genTypoPerc = totalHallucinations == 0 ? 0 : (double)_aggregateStats.GenerativeTypos / totalHallucinations;
            GenTyposPercentageTextBox.Text = genTypoPerc.ToString(percentageFormat, NumberFormat);
        }

        private void FilterChanged(object sender, EventArgs e)
        {
            _filteredView?.Refresh();
            UpdateTotalCounts();
        }

        private void UpdateTotalCounts()
        {
            if (_filteredView != null)
            {
                int totalMedications = 0;
                var uniqueReports = new HashSet<int>();

                foreach (MedicationStatRow row in _filteredView)
                {
                    totalMedications++;
                    uniqueReports.Add(row.ReportNumber);
                }

                TotalMedicationsCountLabel.Content = $"Medications: {totalMedications}";
                TotalReportsCountLabel.Content = $"Reports: {uniqueReports.Count}";
            }
        }

        private bool FilterRows(object obj)
        {
            if (obj is not MedicationStatRow row) return false;

            // Result filter
            if (ResultFilterComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                // 1. Check if the selected item has an ErrorType enum in its Tag
                if (selectedItem.Tag is ErrorType errorTypeToFilter)
                {
                    if (row.Error != errorTypeToFilter) return false;
                }
                else
                {
                    // 2. Fallback for "All" or string-based Result matches
                    var filterValue = selectedItem.Content?.ToString();
                    if (filterValue != "All" && row.Result != filterValue)
                    {
                        return false;
                    }
                }
            }

            // Medication filter
            var medFilter = MedicationFilterTextBox.Text;
            if (!string.IsNullOrWhiteSpace(medFilter))
            {
                if (row.Medication == null || !row.Medication.Contains(medFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
