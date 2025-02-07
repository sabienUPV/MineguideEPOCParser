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
        /// After parsing, this property contains the name of the final output header the result was written to.
        /// 
        /// <para>
        /// This is normally the same as <see cref="DataParserConfiguration.OutputHeaderName"/> in <see cref="Configuration"/>;
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

                // Add new header to the array
                string[]? newHeaders;
                if (Configuration.OverwriteColumn && Configuration.InputHeaderName == Configuration.OutputHeaderName)
                {
                    // If we are overwriting the column, and the input and output headers are the same,
                    // we don't need to modify the headers array
                    newHeaders = dataRead.Headers;
                    FinalOutputHeaderName = Configuration.OutputHeaderName;
                }
                else
                {
                    // Ensure the output header is unique
                    // (if the column is new, then we need to ensure its uniqueness;
                    // and if we are overwriting the column, we already checked that it doesn't match the input header,
                    // but we still need to ensure its uniqueness against the other headers)

                    var finalHeader = Utilities.ArrayEnsureUniqueHeader(dataRead.Headers, Configuration.OutputHeaderName);
                    FinalOutputHeaderName = finalHeader;

                    if (finalHeader != Configuration.OutputHeaderName)
                    {
                        Logger?.Warning("The output header name was changed to {OutputHeaderName} because it already existed in the input file.", finalHeader);
                    }

                    if (Configuration.OverwriteColumn)
                    {
                        // If we are overwriting the column, we replace the input header with the output header
                        newHeaders = dataRead.Headers.Select((header, i) => i == dataRead.InputColumnIndex ? finalHeader : header).ToArray();
                    }
                    else
                    {
                        // If we are not overwriting the column, we add the output header at the end
                        newHeaders = Utilities.ArrayCopyAndAdd(dataRead.Headers, finalHeader);
                    }
                }

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
