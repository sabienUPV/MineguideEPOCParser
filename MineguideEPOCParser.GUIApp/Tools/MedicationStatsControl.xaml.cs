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
                var aggregateStats = new MedicationExperimentStats();
                _allRows.Clear();

                foreach (var filePath in existingFiles)
                {
                    var stats = await MedicationExperimentStatsCalculator.CalculateStatsAsync(filePath, new()
                    {
                        CultureName = MedicationManualValidatorControl.DefaultCultureName, // TODO: Allow user to select culture if needed
                        InputFile = filePath,
                        OutputFile = string.Empty
                    });

                    aggregateStats.TP += stats.TP;
                    aggregateStats.TPStar += stats.TPStar;
                    aggregateStats.FP += stats.FP;
                    aggregateStats.FN += stats.FN;
                    aggregateStats.Hallucinations += stats.Hallucinations;

                    foreach (var row in stats.Rows)
                    {
                        _allRows.Add(row);
                    }
                }

                TPTextBox.Text = aggregateStats.TP.ToString();
                TPStarTextBox.Text = aggregateStats.TPStar.ToString();
                TPExactPlusFuzzyTextBox.Text = aggregateStats.TPExactPlusFuzzy.ToString();
                FPTextBox.Text = aggregateStats.FP.ToString();
                FNTextBox.Text = aggregateStats.FN.ToString();
                HallucinationsTextBox.Text = aggregateStats.Hallucinations.ToString();

                PrecisionTextBox.Text = aggregateStats.Precision.ToString("F4");
                PrecisionPercentageTextBox.Text = aggregateStats.Precision.ToString("P4");

                RecallTextBox.Text = aggregateStats.Recall.ToString("F4");
                RecallPercentageTextBox.Text = aggregateStats.Recall.ToString("P4");

                F1ScoreTextBox.Text = aggregateStats.F1Score.ToString("F4");
                F1ScorePercentageTextBox.Text = aggregateStats.F1Score.ToString("P4");

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

        private void FilterChanged(object sender, EventArgs e)
        {
            _filteredView?.Refresh();
            UpdateTotalCounts();
        }

        private void UpdateTotalCounts()
        {
            if (_filteredView != null)
            {
                var rows = _filteredView.Cast<object>();
                TotalMedicationsCountLabel.Content = $"Medications: {rows.Count()}";
                TotalReportsCountLabel.Content = $"Reports: {rows.Select(r => ((MedicationStatRow)r).ReportNumber).Distinct().Count()}";
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
