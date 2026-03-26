using MineguideEPOCParser.Core.LLM;
using MineguideEPOCParser.GUIApp.Utils;
using System.Windows;

namespace MineguideEPOCParser.GUIApp
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			// Configure the API client with settings from appsettings.json
			// This runs at application startup, ensuring configuration is available before any windows are created
			ApiClient.Configuration = new AppSettingsApiConfiguration();
		}
	}
}
