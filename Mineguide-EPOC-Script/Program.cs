namespace Mineguide_EPOC_Script
{
	public static class Program
    {
        public static async Task Main(string[] args)
        {
            using CancellationTokenSource cts = new();

            // Desktop folder
            var desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            // TEST JUAN
            var testConfiguration = JuanConfig();

			// TEST ALEJANDRO
			// var testConfiguration = AlejandroConfig(desktopFolder);

            Console.WriteLine("Application started.");
            Console.WriteLine("Press the ENTER key to cancel...\n");

            Task cancelTask = Task.Run(() =>
            {
                while (Console.ReadKey().Key != ConsoleKey.Enter)
                {
                    Console.WriteLine("Press ENTER key to cancel the operation...");
                }

                Console.WriteLine("Cancelling operation...");
                cts.Cancel();
            });

            try
            {
				await MedicationParser.ParseMedication(testConfiguration, cts.Token);
				Console.WriteLine("ParseMedication completed");
			}
            catch (OperationCanceledException)
            {
				Console.WriteLine("ParseMedication has been cancelled.");
			}
        }

        private static MedicationParser.Configuration JuanConfig()
        {
            var testConfiguration = new MedicationParser.Configuration()
            {
                CultureName = "es-ES",
                InputFile = "C:\\Users\\pjval\\OneDrive\\Prácticas 2024\\Mineguide-EPOC\\Bronquiolitis actualizado\\medicamentosUTF-8.csv",
                OutputFile = "C:\\Users\\pjval\\OneDrive\\Prácticas 2024\\Mineguide-EPOC\\Bronquiolitis actualizado\\medicamentosFinal.csv",
            };
            return testConfiguration;
        }

        private static MedicationParser.Configuration AlejandroConfig(string desktopFolder)
        {
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
