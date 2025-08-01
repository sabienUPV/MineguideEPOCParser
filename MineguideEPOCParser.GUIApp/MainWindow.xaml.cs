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
        private MenuItem? _selectedMenuItem;

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
            // Populate the menu with control factories
            foreach (var (header, factory) in _controlFactories)
            {
                var menuItem = new MenuItem { Header = header };
                menuItem.Click += async (s, args) =>
                {
                    await ChangeContentControl(factory());
                    HighlightMenuItem(menuItem);
                };
                MainMenu.Items.Add(menuItem);
            }

            // Set the initial content control to the first one in the list
            _currentControl = _controlFactories[0].Factory();
            MainContentControl.Content = _currentControl;

            // Highlight the first menu item by default
            if (MainMenu.Items.Count > 0)
                HighlightMenuItem((MenuItem)MainMenu.Items[0]);
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

        private void HighlightMenuItem(MenuItem menuItem)
        {
            // Remove highlight from the previously selected menu item
            _selectedMenuItem?.ClearValue(FontWeightProperty);

            // Highlight the new menu item
            menuItem.FontWeight = FontWeights.Bold;

            // Store the selected menu item
            _selectedMenuItem = menuItem;
        }
    }
}