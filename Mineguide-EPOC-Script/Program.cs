namespace Mineguide_EPOC_Script
{
	public static class Program
    {
        public static async Task Main(string[] args)
        {
			// TEST JUAN
			 var testConfiguration = new MedicationParser.Configuration()
			{
				CultureName = "es-ES",
				InputFile = "C:\\Users\\pjval\\OneDrive\\Prácticas 2024\\Mineguide-EPOC\\Bronquiolitis actualizado\\medicamentosUTF-8.csv",
				OutputFile = "C:\\Users\\pjval\\OneDrive\\Prácticas 2024\\Mineguide-EPOC\\Bronquiolitis actualizado\\medicamentosFinal.csv",
			};

			// Desktop folder
			var desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

			// TEST ALEJANDRO
			// var testConfiguration = new MedicationParser.Configuration()
			// {
			// 	CultureName = "es-ES",
			// 	InputFile = "D:\\OneDrive - UPV\\Alejandro\\assets\\MINEGUIDE\\Datos-Transformados\\URGENCIAS-INFORMES-EPOC-TRATAMIENTO-URGENCIAS.csv",
			// 	OutputFile = Path.Combine(desktopFolder, "medicamentosFinal.csv"),
			// };

			await MedicationParser.ParseMedication(testConfiguration);
        }
    }
}