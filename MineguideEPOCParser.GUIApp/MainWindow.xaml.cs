using System.Windows;

namespace MineguideEPOCParser.GUIApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
	{
		public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            await MedicationParserCtrl.DisposeAsync();
        }
    }
}