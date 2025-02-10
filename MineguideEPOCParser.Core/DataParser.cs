using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using CsvHelper;
using System.Globalization;
using System.Text;
using System.Runtime.CompilerServices;
using Serilog;

namespace MineguideEPOCParser.Core
{
    public abstract class DataParser<TConfiguration>
        where TConfiguration : DataParserConfiguration
	{
        public required TConfiguration Configuration { get; set; }

        public ILogger? Logger { get; set; }
        public IProgress<ProgressValue>? Progress { get; set; }

        /// <summary>
        /// Number of output columns that the parser will write to the output file
        /// </summary>
        public virtual int OutputColumnsCount => 1;

        /// <summary>
        /// Reads the medication from the input CSV file, lazily,
        /// applies a transformation to extract the medications from the 'T' column,
        /// and writes the results to the output CSV file.
        /// 
        /// This is lazily evaluated, so it will not read the entire input file into memory.
        /// </summary>
        public async Task ParseData(CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var csvConfig = new CsvConfiguration(new CultureInfo(Configuration.CultureName))
                {
                    CountBytes = Progress is not null && Configuration.Count is null,
                };

                await DoPreProcessing(cancellationToken);

                // Read
                using var reader = new StreamReader(Configuration.InputFile);
                using var csvReader = new CsvReader(reader, csvConfig);

                var dataRead = await ReadData(csvReader, reader, Configuration.Count, cancellationToken);

                // Generate new headers
                string[]? newHeaders = GenerateNewHeaders(dataRead);

                // Apply transformations
                var newRows = ApplyTransformations(dataRead.Rows, dataRead.InputColumnIndex, cancellationToken);

                // Write
                await using var writer = new StreamWriter(Configuration.OutputFile, false, Encoding.UTF8);
                await using var csvWriter = new CsvWriter(writer, csvConfig);

                await WriteTransformedData(csvWriter, newHeaders, newRows);

                // Report progress and log completion
                Progress?.Report(new ProgressValue
                {
                    Value = 1, // 100%
                               // RowsProcessed is not set because we don't know the total number of rows
                               // (because it normally writes more rows than it reads - one duplicated row per medication)
                               // and it should be the last value reported anyways
                });
                Logger?.Information("Data parsing completed.");
            }
            catch (OperationCanceledException)
            {
                Logger?.Warning("Data parsing was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "An unexpected error occurred: {Message}", ex.Message);
                throw;
            }
        }

        protected string[] GenerateNewHeaders(DataReadContent dataRead)
        {
            // Ensure that the number of output headers is correct
            if (Configuration.NumberOfOutputColumns != OutputColumnsCount)
            {
                throw new InvalidOperationException($"The number of output headers ({Configuration.NumberOfOutputColumns}) does not match the expected number of output columns ({OutputColumnsCount}).");
            }

            // Ensure the output headers are unique (their names might already exist in the input headers)
            var finalOutputHeaders = Configuration.OutputHeaderNames.Select(outputHeader =>
            {
                var finalOutputHeader = Utilities.ArrayEnsureUniqueHeader(dataRead.Headers, outputHeader);

                if (finalOutputHeader != outputHeader)
                {
                    Logger?.Warning("The output header name {OriginalHeaderName} was changed to {OutputHeaderName} because it already existed in the input file.", outputHeader, finalOutputHeader);
                }

                return finalOutputHeader;
            });

            IEnumerable<string> headersEnumerable;

            // If we are "overwriting" the input column, replace it with the output columns
            if (Configuration.OverwriteColumn)
            {
                headersEnumerable = GenerateNewHeadersWithOverwrite(dataRead.Headers, finalOutputHeaders);
            }
            else
            {
                // Otherwise, add the output columns to the end
                headersEnumerable = dataRead.Headers.Concat(finalOutputHeaders);
            }

            return headersEnumerable.ToArray();
        }

        private IEnumerable<string> GenerateNewHeadersWithOverwrite(IEnumerable<string> headers, IEnumerable<string> finalOutputHeaders)
        {
            foreach (var h in headers)
            {
                if (h == Configuration.InputHeaderName)
                {
                    foreach (var outputHeader in finalOutputHeaders)
                    {
                        yield return outputHeader;
                    }
                }
                else
                {
                    yield return h;
                }
            }
        }

        protected virtual Task DoPreProcessing(CancellationToken cancellationToken = default)
        {
            // Override this method to add custom pre-processing logic
            return Task.CompletedTask;
        }

        protected abstract IAsyncEnumerable<string[]> ApplyTransformations(IAsyncEnumerable<string[]> rows, int inputColumnIndex, CancellationToken cancellationToken = default);

        /// <summary>
        /// Este método se encarga de leer el archivo csv y devolver un objeto 'DataReadContent'
        /// que contiene los headers, las rows y el index de la columna 'T' que contiene el texto completo con los datos sin extraer
        /// </summary>
        /// <returns>result</returns>
        protected async Task<DataReadContent> ReadData(CsvReader csv, StreamReader sr, int? count = null, CancellationToken cancellationToken = default)
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

                    rowsEnumerable = ReadDataFromCsv(csv, sr, count, cancellationToken);
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

            var result = new DataReadContent()
            {
                Headers = headerArray,
                Rows = rowsEnumerable,
                InputColumnIndex = inputColumnIndex,
            };

            return result;
        }

        protected async IAsyncEnumerable<string[]> ReadDataFromCsv(CsvReader csv, StreamReader sr, int? count = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

        protected async Task<int> WriteTransformedData(CsvWriter csv, string[] headers, IAsyncEnumerable<string[]> rows)
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

        protected class DataReadContent
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
