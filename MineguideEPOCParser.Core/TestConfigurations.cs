namespace MineguideEPOCParser.Core
{
	public static class TestConfigurations
	{
		public const string JuanInputFile = "C:\\Users\\pjval\\OneDrive\\Prácticas 2024\\Mineguide-EPOC\\Bronquiolitis actualizado\\medicamentosUTF-8.csv";
		public const string JuanOutputFile = "C:\\Users\\pjval\\OneDrive\\Prácticas 2024\\Mineguide-EPOC\\Bronquiolitis actualizado\\medicamentosFinal.csv";

		public const string AlejandroInputFile = "D:\\OneDrive - UPV\\Alejandro\\assets\\MINEGUIDE\\Datos-Transformados\\URGENCIAS-INFORMES-EPOC-TRATAMIENTO-URGENCIAS.csv";
		public static readonly string AlejandroOutputFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "medicamentosFinal.csv");

		public const string DefaultCultureName = "es-ES";

		public static MedicationParser.MedicationParserConfiguration JuanConfig()
		{
			return new MedicationParser.MedicationParserConfiguration()
			{
				CultureName = DefaultCultureName,
				InputFile = JuanInputFile,
				OutputFile = JuanOutputFile,
			};
		}

		public static MedicationParser.MedicationParserConfiguration AlejandroConfig()
		{
			return new MedicationParser.MedicationParserConfiguration()
			{
				CultureName = DefaultCultureName,
				InputFile = AlejandroInputFile,
				OutputFile = AlejandroOutputFile,
			};
		}
	}
}
