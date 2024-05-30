using MineguideEPOCParser.Core;

namespace MineguideEPOCParser.ConsoleApp
{
	internal static class TestConfigurations
	{
		internal static MedicationParser.Configuration JuanConfig()
		{
			var testConfiguration = new MedicationParser.Configuration()
			{
				CultureName = "es-ES",
				InputFile = "C:\\Users\\pjval\\OneDrive\\Prácticas 2024\\Mineguide-EPOC\\Bronquiolitis actualizado\\medicamentosUTF-8.csv",
				OutputFile = "C:\\Users\\pjval\\OneDrive\\Prácticas 2024\\Mineguide-EPOC\\Bronquiolitis actualizado\\medicamentosFinal.csv",
			};
			return testConfiguration;
		}

		internal static MedicationParser.Configuration AlejandroConfig()
		{
			// Desktop folder
			var desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

			var testConfiguration = new MedicationParser.Configuration()
			{
				CultureName = "es-ES",
				InputFile = "D:\\OneDrive - UPV\\Alejandro\\assets\\MINEGUIDE\\Datos-Transformados\\URGENCIAS-INFORMES-EPOC-TRATAMIENTO-URGENCIAS.csv",
				OutputFile = Path.Combine(desktopFolder, "medicamentosFinal.csv"),
			};
			return testConfiguration;
		}
	}
}
