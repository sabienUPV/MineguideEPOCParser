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
                var stats = await MedicationExperimentStatsCalculator.CalculateStatsAsync(filePath, new()
                {
                    CultureName = MedicationManualValidatorControl.DefaultCultureName, // TODO: Allow user to select culture if needed
                    InputFile = filePath,
                    OutputFile = string.Empty
                });

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

                _allRows.Clear();
                foreach (var row in stats.Rows)
                {
                    _allRows.Add(row);
                }

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
