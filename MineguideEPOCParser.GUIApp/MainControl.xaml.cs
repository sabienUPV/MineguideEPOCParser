using System.Windows;
using System.Windows.Controls;

namespace MineguideEPOCParser.GUIApp
{
    /// <summary>
    /// Lógica de interacción para MainControl.xaml
    /// </summary>
    public partial class MainControl : UserControl, IDisposable, IAsyncDisposable
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

        public MainControl()
        {
            InitializeComponent();
        }

        private void MainControl_Loaded(object sender, RoutedEventArgs e)
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
            await DisposeCurrentControlAsync();
            _currentControl = newControl;
            MainContentControl.Content = _currentControl;
        }

        // 1. The Async Dispose (Preferred)
        public async ValueTask DisposeAsync()
        {
            await DisposeCurrentControlAsync();
        }

        // 2. The Sync Dispose (Fallback)
        public void Dispose()
        {
            if (_currentControl is IDisposable disposable)
            {
                disposable.Dispose();
            }
            // If it only implements IAsyncDisposable, calling it synchronously from Dispose() 
            // is dangerous in WPF (can cause deadlocks), so we rely on the host calling DisposeAsync().
        }

        // 3. The actual cleanup logic
        private async Task DisposeCurrentControlAsync()
        {
            if (_currentControl == null) return;

            // Always prefer Async if available
            if (_currentControl is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_currentControl is IDisposable disposable)
            {
                disposable.Dispose();
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
