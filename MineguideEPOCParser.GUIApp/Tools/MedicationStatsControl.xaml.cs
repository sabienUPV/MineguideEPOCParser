using Microsoft.Win32;
using MineguideEPOCParser.Core.Tools;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MineguideEPOCParser.GUIApp.Tools
{
    public partial class MedicationStatsControl : UserControl
    {
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
                    _aggregateStats.Hallucinations += stats.Hallucinations;

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
                HallucinationsTextBox.Text = _aggregateStats.Hallucinations.ToString();

                UpdateMetricsDisplay();

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

        private void UpdateMetricsDisplay()
        {
            if (_aggregateStats == null || PrecisionTextBox == null) return;

            bool strict = EvaluationModeComboBox.SelectedIndex == 0; // Strict is first

            double precision = strict ? _aggregateStats.StrictPrecision : _aggregateStats.RelaxedPrecision;
            double recall = strict ? _aggregateStats.StrictRecall : _aggregateStats.RelaxedRecall;
            double f1Score = strict ? _aggregateStats.StrictF1Score : _aggregateStats.RelaxedF1Score;

            PrecisionTextBox.Text = precision.ToString("F4");
            PrecisionPercentageTextBox.Text = precision.ToString("P4");

            RecallTextBox.Text = recall.ToString("F4");
            RecallPercentageTextBox.Text = recall.ToString("P4");

            F1ScoreTextBox.Text = f1Score.ToString("F4");
            F1ScorePercentageTextBox.Text = f1Score.ToString("P4");
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
                var filterValue = selectedItem.Content.ToString();
                if (filterValue != "All")
                {
                    if (filterValue == "Hallucination")
                    {
                        if (!row.IsHallucination) return false;
                    }
                    else if (row.Result != filterValue)
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
