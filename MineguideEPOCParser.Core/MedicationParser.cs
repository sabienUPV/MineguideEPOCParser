using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using CsvHelper;
using System.Globalization;
using System.Text;
using System.Runtime.CompilerServices;
using Serilog;

namespace MineguideEPOCParser.Core
{
	public abstract class MedicationParser
	{
        public required MedicationParserConfiguration Configuration { get; set; }

        public ILogger? Logger { get; set; }
        public IProgress<ProgressValue>? Progress { get; set; }

        /// <summary>
        /// After parsing, this property contains the name of the final output header the result was written to.
        /// 
        /// <para>
        /// This is normally the same as <see cref="MedicationParserConfiguration.OutputHeaderName"/> in <see cref="Configuration"/>;
        /// but if the header already existed in the input file,
        /// the parser will add a number at the end to make it unique.
        /// </para>
        /// </summary>
        public string? FinalOutputHeaderName { get; private set; }

        /// <summary>
        /// Reads the medication from the input CSV file, lazily,
        /// applies a transformation to extract the medications from the 'T' column,
        /// and writes the results to the output CSV file.
        /// 
        /// This is lazily evaluated, so it will not read the entire input file into memory.
        /// </summary>
        public async Task ParseMedication(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var csvConfig = new CsvConfiguration(new CultureInfo(Configuration.CultureName))
                {
                    CountBytes = Progress is not null && Configuration.Count is null,
                };

                // Read
                using var reader = new StreamReader(Configuration.InputFile);
                using var csvReader = new CsvReader(reader, csvConfig);

                var medicationRead = await ReadMedication(csvReader, reader, Configuration.Count, cancellationToken);

                // Add new header to the array
                var finalHeader = Utilities.ArrayEnsureUniqueHeader(medicationRead.Headers, Configuration.OutputHeaderName);
                FinalOutputHeaderName = finalHeader;

                if (finalHeader != Configuration.OutputHeaderName)
                {
                    Logger?.Warning("The output header name was changed to {OutputHeaderName} because it already existed in the input file.", finalHeader);
                }

                string[]? newHeaders = Utilities.ArrayCopyAndAdd(medicationRead.Headers, finalHeader);

                // Apply transformations
                var newRows = ApplyTransformations(medicationRead.Rows, medicationRead.InputColumnIndex, cancellationToken);

                // Write
                await using var writer = new StreamWriter(Configuration.OutputFile, false, Encoding.UTF8);
                await using var csvWriter = new CsvWriter(writer, csvConfig);

                await WriteMedication(csvWriter, newHeaders, newRows);

                // Report progress and log completion
                Progress?.Report(new ProgressValue
                {
                    Value = 1, // 100%
                               // RowsProcessed is not set because we don't know the total number of rows
                               // (because it normally writes more rows than it reads - one duplicated row per medication)
                               // and it should be the last value reported anyways
                });
                Logger?.Information("Medication parsing completed.");
            }
            catch (OperationCanceledException)
            {
                Logger?.Warning("Medication parsing was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "An unexpected error occurred: {Message}", ex.Message);
                throw;
            }
        }

        protected abstract IAsyncEnumerable<string[]> ApplyTransformations(IAsyncEnumerable<string[]> rows, int inputColumnIndex, CancellationToken cancellationToken = default);

        /// <summary>
        /// Este método se encarga de leer el archivo csv y devolver un objeto 'MedicationContent'
        /// que contiene los headers, las rows y el index de la columna 'T' que contiene el texto completo con las medicaciones sin extraer
        /// </summary>
        /// <returns>result</returns>
        protected async Task<MedicationReadContent> ReadMedication(CsvReader csv, StreamReader sr, int? count = null, CancellationToken cancellationToken = default)
        {
            string[]? headerArray = null;
            IAsyncEnumerable<string[]>? rowsEnumerable = null;
            int inputColumnIndex = -1;

            try
            {
                if (await csv.ReadAsync())
                {
                    csv.ReadHeader();
                    headerArray = csv.HeaderRecord;

                    inputColumnIndex = GetInputColumnIndex(headerArray);

                    if (inputColumnIndex < 0)
                    {
                        throw new InvalidOperationException($"{Configuration.InputHeaderName} column was not found");
                    }

                    rowsEnumerable = ReadMedicationFromCsv(csv, sr, count, cancellationToken);
                }
            }
            catch (FileNotFoundException ex)
            {
                Logger?.Error(ex, "El archivo no se encontró.");
                throw;
            }
            catch (HeaderValidationException ex)
            {
                Logger?.Error(ex, "Los encabezados del archivo CSV no coinciden con las propiedades de la clase.");
                throw;
            }
            catch (TypeConverterException ex)
            {
                Logger?.Error(ex, "Hubo un problema convirtiendo los datos del archivo CSV a los tipos de la clase.");
                throw;
            }
            catch (OperationCanceledException)
            {
                // Expected when the operation is cancelled. Do nothing and retrow the exception.
                throw;
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Ocurrió un error inesperado: {Message}", ex.Message);
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
                InputColumnIndex = inputColumnIndex,
            };

            return result;
        }

        protected async IAsyncEnumerable<string[]> ReadMedicationFromCsv(CsvReader csv, StreamReader sr, int? count = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int rowsRead = 0;
            while (await csv.ReadAsync())
            {
                rowsRead++;

                var row = csv.Parser.Record;

                Logger?.Information("Read row {RowNumber}", rowsRead);
                Logger?.Verbose("{Row}", string.Join(",", row));

                yield return row;

                Progress?.Report(CalculateProgress(csv, sr, rowsRead, count));

                cancellationToken.ThrowIfCancellationRequested();

                if (rowsRead == count)
                {
                    break;
                }
            }
        }

        protected static ProgressValue CalculateProgress(CsvReader csv, StreamReader sr, int rowsRead, int? count = null)
        {
            if (count is null)
            {
                // If we don't know the number of rows (count),
                // we calculate the progress using the number of bytes in the stream as reference,
                // and counting the bytes for each row (CsvParser.ByteCount)
                return new ProgressValue
                {
                    Value = (double)csv.Context.Parser.ByteCount / sr.BaseStream.Length,
                    RowsProcessed = rowsRead,
                };
            }

            // If we explicitly specified the number of rows (count),
            // the progress is just the percentage of number of rows read from the total
            return new ProgressValue
            {
                Value = (double)rowsRead / count.Value,
                RowsProcessed = rowsRead,
            };
        }

        /// <summary>
        /// Este método recoge y devuelve la posición de la columna
        /// donde se encuentran los medicamentos
        /// </summary>
        /// <param name="headerArray"></param>
        /// <exception cref="Exception"></exception>
        protected int GetInputColumnIndex(string[] headerArray)
            => GetColumnIndex(headerArray, Configuration.InputHeaderName);

        /// <summary>
        /// Get the index of the column whose header is the one provided
        /// </summary>
        /// <param name="headerArray"></param>
        /// <exception cref="Exception"></exception>
        protected static int GetColumnIndex(string[] headerArray, string headerName)
        {
            for (int i = 0; i < headerArray.Length; i++)
            {
                if (headerArray[i].Equals(headerName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            throw new Exception("No se ha encontrado la columna solicitada.");
        }

        protected async Task<int> WriteMedication(CsvWriter csv, string[] headers, IAsyncEnumerable<string[]> rows)
        {
            foreach (var header in headers)
            {
                csv.WriteField(header);
            }

            Logger?.Debug("Written headers: {Headers}", string.Join(",", headers));

            await csv.NextRecordAsync();

            int rowsWritten = 0;

            await foreach (var row in rows)
            {
                foreach (var field in row)
                {
                    csv.WriteField(field);
                }

                var rowNumber = rowsWritten + 1;
                Logger?.Information("Written row {RowNumber}", rowNumber);
                Logger?.Verbose("{Row}", string.Join(",", row));

                await csv.NextRecordAsync();
                rowsWritten++;
            }

            return rowsWritten;
        }

        public class MedicationParserConfiguration
        {
            public required string CultureName { get; set; }
            public required string InputFile { get; set; }
            public required string OutputFile { get; set; }

            public const string THeaderName = "T";
            public const string MedicationHeaderName = "Medication";

            public string InputHeaderName { get; set; } = THeaderName;
            public string OutputHeaderName { get; set; } = MedicationHeaderName;

            public int? Count { get; set; }
        }

        public readonly struct ProgressValue
        {
            /// <summary>
            /// Progress value between 0 and 1.
            /// </summary>
            public double Value { get; init; }
            public int? RowsProcessed { get; init; }
        }

        protected class MedicationReadContent
        {
            public required string[] Headers { get; init; }

            /// <summary>
            /// Note: The rows are lazily evaluated.
            /// </summary>
            public required IAsyncEnumerable<string[]> Rows { get; init; }

            public int InputColumnIndex { get; init; }
        }
    }
}
