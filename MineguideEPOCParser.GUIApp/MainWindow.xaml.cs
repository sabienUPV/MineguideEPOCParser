using System.Windows;
using System.Windows.Controls;

namespace MineguideEPOCParser.GUIApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
	{
        private ContentControl? _currentControl;

        private static readonly (string Header, Func<ContentControl> Factory)[] _controlFactories =
        [
            ("Medication Parser", () => new MedicationParserControl()),
            ("Medication Mapper and Grouping to Group Mapper", () => new MedicationMapperGroupingControl()),
            ("Medication Grouping To Mapper", () => new MedicationGroupingToMapperControl()),
            ("File Encoding Converter", () => new FileEncodingConverterControl()),
            ("Measurements Parser", () => new MeasurementsParserControl()),
            ("Medication Manual Validator", () => new MedicationManualValidatorControl()),
            ("Random Sampler Parser", () => new RandomSamplerParserControl())
        ];

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize the main content control with the first control
            _currentControl = _controlFactories[0].Factory();
            MainContentControl.Content = _currentControl;
            // Populate the menu with control options
            foreach (var (header, factory) in _controlFactories)
            {
                var menuItem = new MenuItem { Header = header };
                menuItem.Click += async (s, args) => await ChangeContentControl(factory());
                MainMenu.Items.Add(menuItem);
            }
        }

        private async Task ChangeContentControl(ContentControl newControl)
        {
            await DisposeCurrentControl();
            _currentControl = newControl;
            MainContentControl.Content = _currentControl;
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            await DisposeCurrentControl();
        }

        private async Task DisposeCurrentControl()
        {
            if (_currentControl is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else if (_currentControl is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
        }
    }
}