using MineguideEPOCParser.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Windows;

namespace MineguideEPOCParser.GUIApp
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window, IDisposable
	{
		// Dependency property IsParsing
		public static readonly DependencyProperty IsParsingProperty = DependencyProperty.Register(
			nameof(IsParsing),
			typeof(bool),
			typeof(MainWindow),
			new PropertyMetadata(false)
		);

		public bool IsParsing
		{
			get => (bool)GetValue(IsParsingProperty);
			private set => SetValue(IsParsingProperty, value);
		}

		// Cancelling
		private CancellationTokenSource? CancellationTokenSource { get; set; }

		// Logging
		private ILogger? Logger { get; set; }
		private LoggingLevelSwitch? LoggingLevelSwitch { get; set; }

		// Progress reporting
		private Progress<MedicationParser.ProgressValue>? Progress { get; set; }

		public MainWindow()
		{
			InitializeComponent();

			// Create a new logger
			CreateLogger();

			// Create a new progress object
			CreateProgress();
		}

		/// <summary>
		/// Create a new logger that writes to a TextBox
		/// </summary>
		private void CreateLogger()
		{
			LoggingLevelSwitch = new LoggingLevelSwitch
			{
				// Take the default log level from the control
				MinimumLevel = GetLogLevelFromComboBox()
			};

			Logger = new LoggerConfiguration()
				.MinimumLevel.ControlledBy(LoggingLevelSwitch)
				.WriteTo.RichTextBox(LogRichTextBox)
				.CreateLogger();
		}

		private LogEventLevel GetLogLevelFromComboBox()
		{
			return Enum.TryParse<LogEventLevel>(LogLevelComboBox.Text, out var logLevel)
				? logLevel
				: LogEventLevel.Information;
		}

		/// <summary>
		/// Create a new progress object that updates the progress bar
		/// </summary>
		private void CreateProgress()
		{
			Progress = new Progress<MedicationParser.ProgressValue>(value =>
			{
				// Value is between 0 and 1, so multiply by 100 to get percentage
				var percentage = value.Value * 100;

				// Update the progress bar
				ProgressBar.Value = percentage;

				// Update the progress percentage text
				ProgressPercentageTextBlock.Text = $"{percentage:0.00}%";

				// Update the progress rows read text
				ProgressRowsReadTextBlock.Text = $"Rows read: {value.RowsRead}";
			});
		}

		public void Dispose()
		{
			CancellationTokenSource?.Dispose();
			
			// Dispose the logger
			if (Logger is IDisposable disposableLogger)
			{
				disposableLogger.Dispose();
			}
		}

		private async void ParseButton_Click(object sender, RoutedEventArgs e)
		{
			// Get the input and output files from the text boxes
			string inputFile = InputFileTextBox.Text;
			string outputFile = OutputFileTextBox.Text;

			// Get the culture name from the combo box
			string cultureName = FileCultureComboBox.Text;

			var configuration = new MedicationParser.Configuration()
			{
				CultureName = cultureName,
				InputFile = inputFile,
				OutputFile = outputFile,
				Logger = Logger,
				Progress = Progress
			};

			// Create a new cancellation token source
			CancellationTokenSource = new CancellationTokenSource();

			// Clear the log
			LogRichTextBox.Document.Blocks.Clear();

			// Parse the medication
			
			IsParsing = true;
			
			try
			{
				await MedicationParser.ParseMedication(configuration, CancellationTokenSource.Token);
			}
			catch (OperationCanceledException)
			{
				MessageBox.Show($"Parsing was cancelled.\nThe information that was already parsed has been written to the output file.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"An error occurred while parsing the medication:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			finally
			{
				// Reset the flag
				IsParsing = false;

				// Dispose the cancellation token source
				CancellationTokenSource.Dispose();

				// Set the cancellation token source to null
				CancellationTokenSource = null;
			}
		}

		private void CancelButton_Click(object sender, RoutedEventArgs e)
		{
			// Cancel the parsing
			CancellationTokenSource?.Cancel();
		}

		private void BrowseInputFileButton_Click(object sender, RoutedEventArgs e)
		{
			InputFileTextBox.Text = BrowseCsvFile<Microsoft.Win32.OpenFileDialog>(InputFileTextBox.Text) ?? string.Empty;
		}

		private void BrowseOutputFileButton_Click(object sender, RoutedEventArgs e)
		{
			OutputFileTextBox.Text = BrowseCsvFile<Microsoft.Win32.SaveFileDialog>(OutputFileTextBox.Text, "medication.csv") ?? string.Empty;
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

		private void LogLevelComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
		{
			LogEventLevel logLevel = GetLogLevelFromComboBox();
			
			// Update the log level switch
			if (LoggingLevelSwitch is not null)
			{
				LoggingLevelSwitch.MinimumLevel = logLevel;
			}
		}
	}
}