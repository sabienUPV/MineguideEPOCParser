namespace Mineguide_EPOC_Script
{
	public static class Program
    {
        private static readonly CancellationTokenSource Cts = new();

        public static async Task Main(string[] args)
        {
            // Desktop folder
            var desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            // TEST JUAN
            var testConfiguration = JuanConfig();

			// TEST ALEJANDRO
			//testConfiguration = AlejandroConfig(desktopFolder);

            Console.WriteLine("Application started.");
            Console.WriteLine("Press the ENTER key to cancel...\n");

            Task cancelTask = Task.Run(() =>
            {
                while (Console.ReadKey().Key != ConsoleKey.Enter)
                {
                    Console.WriteLine("Press ENTER key to cancel the operation...");
                }

                Console.WriteLine("Cancelling operation...");
                Cts.Cancel();
            });

            var finishedTask = await Task.WhenAny([cancelTask, MedicationParser.ParseMedication(testConfiguration)]);

            if (finishedTask == cancelTask)
            {
                try
                {
                    await MedicationParser.ParseMedication(testConfiguration);
                    Console.WriteLine("ParseMedication task completed");
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("ParseMedication task has been cancelled.");
                }
                catch (IOException)
                {
                    Console.WriteLine("Waiting until the output file is closed");
                }
                Cts.Dispose();
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
