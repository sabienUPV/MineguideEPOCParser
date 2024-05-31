using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using CsvHelper;
using System.Globalization;
using System.Text;
using System.Runtime.CompilerServices;
using Serilog;

namespace MineguideEPOCParser.Core
{
	public static class MedicationParser
	{
		/// <summary>
		/// Parses the medication from the input CSV file, lazily,
		/// calls the Ollama API to extract the medications,
		/// and writes the results to the output CSV file.
		/// 
		/// This is lazily evaluated, so it will not read the entire input file into memory.
		/// </summary>
		public static async Task ParseMedication(Configuration configuration, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var csvConfig = new CsvConfiguration(new CultureInfo(configuration.CultureName));

			// Read 
			using var reader = new StreamReader(configuration.InputFile);
			using var csvReader = new CsvReader(reader, csvConfig);

			var medicationRead = ReadMedication(csvReader, configuration.Logger, cancellationToken);

			// Add new header to the array
			string[]? newHeaders = ArrayCopyAndAdd(medicationRead.Headers, "Medication");

			var newRows = ExtractMedications(medicationRead.Rows, medicationRead.TColumnIndex, configuration.Logger, cancellationToken);

			// Write
			await using var writer = new StreamWriter(configuration.OutputFile, false, Encoding.UTF8);
			await using var csvWriter = new CsvWriter(writer, csvConfig);

			await WriteMedication(csvWriter, newHeaders, newRows, configuration.Logger);
		}

		/// <summary>
		/// Este método se encarga de leer el archivo csv y devolver un objeto 'MedicationContent'
		/// que contiene los headers, las rows y el index de la columna 'T' renombrada a 'Medication'
		/// </summary>
		/// <returns>result</returns>
		private static MedicationReadContent ReadMedication(CsvReader csv, ILogger? log = null, CancellationToken cancellationToken = default)
		{
			string[]? headerArray = null;
			IEnumerable<string[]>? rowsEnumerable = null;
			int tColumnIndex = -1;

			try
			{
				if (csv.Read())
				{
					csv.ReadHeader();
					headerArray = csv.HeaderRecord;

					tColumnIndex = GetTColumnIndex(headerArray);

					if (tColumnIndex < 0)
					{
						throw new InvalidOperationException("T column was not found");
					}

					rowsEnumerable = ReadMedicationFromCsv(csv, cancellationToken);
				}
			}
			catch (FileNotFoundException ex)
			{
				log?.Error(ex, "El archivo no se encontró.");
				throw;
			}
			catch (HeaderValidationException ex)
			{
				log?.Error(ex, "Los encabezados del archivo CSV no coinciden con las propiedades de la clase.");
				throw;
			}
			catch (TypeConverterException ex)
			{
				log?.Error(ex, "Hubo un problema convirtiendo los datos del archivo CSV a los tipos de la clase.");
				throw;
			}
			catch (OperationCanceledException)
			{
				// Expected when the operation is cancelled. Do nothing and retrow the exception.
				throw;
			}
			catch (Exception ex)
			{
				log?.Error(ex, "Ocurrió un error inesperado: {Message}", ex.Message);
				throw;
			}

			if (headerArray == null)
			{
				throw new InvalidOperationException("Header array is null");
			}

			if (rowsEnumerable == null)
			{
				throw new InvalidOperationException("Rows enumerable is null");
			}

			var result = new MedicationReadContent()
			{
				Headers = headerArray,
				Rows = rowsEnumerable,
				TColumnIndex = tColumnIndex,
			};

			return result;
		}

		private static IEnumerable<string[]> ReadMedicationFromCsv(CsvReader csv, CancellationToken cancellationToken = default)
		{
			while (csv.Read())
			{
				yield return csv.Parser.Record;
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		/// <summary>
		/// Este método recoge y devuelve la posición de la columna
		/// donde se encuentran los medicamentos
		/// </summary>
		/// <param name="headerArray"></param>
		/// <returns>i</returns>
		/// <exception cref="Exception"></exception>
		private static int GetTColumnIndex(string[] headerArray)
		{
			// Guardado del índice donde se encuentran los medicamentos 'T'
			for (int i = 0; i < headerArray.Length; i++)
			{
				if (headerArray[i].Equals("T", StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}

			throw new Exception("No se ha encontrado la columna solicitada.");
		}

		private static async IAsyncEnumerable<string[]> ExtractMedications(IEnumerable<string[]> rows, int tColumnIndex, ILogger? logger = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			foreach (var row in rows)
			{
				// Recoge la columna que contiene los medicamentos
				var t = row[tColumnIndex];
				var medications = await ApiClient.CallToApi(t, logger, cancellationToken);

				var newRow = ArrayCopyAndAdd(row, medications);
				yield return newRow;
			}
		}

		private static async Task WriteMedication(CsvWriter csv, string[] headers, IAsyncEnumerable<string[]> rows, ILogger? log = null)
		{
			foreach (var header in headers)
			{
				csv.WriteField(header);
			}

			log?.Debug("Written headers: {Headers}", string.Join(",", headers));

			csv.NextRecord();

			int rowNumber = 1;

			await foreach (var row in rows)
			{
				foreach (var field in row)
				{
					csv.WriteField(field);
				}
				log?.Debug("Written row {RowNumber}: {Row}", rowNumber, string.Join(",", row));
				csv.NextRecord();
				rowNumber++;
			}
		}

		private static T[] ArrayCopyAndAdd<T>(T[] sourceArray, T elementToAdd)
		{
			// Create a new array with one more element at the end,
			// and copy the original array to it
			var destinationArray = new T[sourceArray.Length + 1];
			Array.Copy(sourceArray, destinationArray, sourceArray.Length);

			// Add element at the end of the array
			destinationArray[^1] = elementToAdd;

			return destinationArray;
		}

		public class Configuration
		{
			public required string CultureName { get; set; }
			public required string InputFile { get; set; }
			public required string OutputFile { get; set; }

			public ILogger? Logger { get; set; }
		}

		private class MedicationReadContent
		{
			public required string[] Headers { get; init; }

			/// <summary>
			/// Note: The rows are lazily evaluated.
			/// </summary>
			public required IEnumerable<string[]> Rows { get; init; }

			public int TColumnIndex { get; init; }
		}
	}
}
