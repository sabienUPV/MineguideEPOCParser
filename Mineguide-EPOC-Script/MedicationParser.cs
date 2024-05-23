using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using CsvHelper;
using System.Globalization;

namespace Mineguide_EPOC_Script
{
    public static class MedicationParser
    {
        private static MedicationContent ReadMedication()
        {
            return ReadMedication(
                "es-ES", 
                "C:\\Users\\pjval\\OneDrive\\Prácticas 2024\\Mineguide-EPOC\\Bronquiolitis actualizado\\medicamentosUTF-8.csv"
                );
        }
        
        /// <summary>
        /// Este método se encarga de leer el archivo csv y devolver un objeto 'MedicationContent'
        /// que contiene los headers, las rows y el index de la columna 'T' renombrada a 'Medication'
        /// </summary>
        /// <param name="cultureName"></param>
        /// <param name="filepath"></param>
        /// <returns>result</returns>
        private static MedicationContent ReadMedication(string cultureName, string filepath)
        {
            var config = new CsvConfiguration(new CultureInfo(cultureName));

            string[]? headerArray = null;
            List<string[]> rowsList = [];
            int tColumnIndex = -1;

            try
            {
                using var reader = new StreamReader(filepath);
                using var csv = new CsvReader(reader, config);
                if (csv.Read())
                {
                    csv.ReadHeader();
                    headerArray = csv.HeaderRecord;

                    tColumnIndex = GetTColumnIndex(headerArray);

                    if (tColumnIndex < 0)
                    {
                        throw new InvalidOperationException("T was not found");
                    }

                    while (csv.Read())
                    {
                        // Guarda todo el contenido de cada fila de el fichero .csv
                        var recordArray = csv.Parser.Record;
                        rowsList.Add(recordArray);
                    }
                }
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"El archivo no se encontró.");
                throw;
            }
            catch (HeaderValidationException)
            {
                Console.WriteLine("Los encabezados del archivo CSV no coinciden con las propiedades de la clase.");
                throw;
            }
            catch (TypeConverterException)
            {
                Console.WriteLine("Hubo un problema convirtiendo los datos del archivo CSV a los tipos de la clase.");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ocurrió un error inesperado: {ex.Message}");
                throw;
            }

            if (headerArray == null)
            {
                throw new InvalidOperationException("Header array is null");
            }

            var result = new MedicationContent()
            {
                Headers = headerArray,
                Rows = rowsList,
                TColumnIndex = tColumnIndex,
            };

            return result;
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
            // Cambio de nombre de columna de medicamentos de 'T' a 'Medication'
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

        public static void ParseMedication()
        {
            var result = ReadMedication();

            var rowsWithMedicationsList = ExtractMedications(result);

            WriteMedication(rowsWithMedicationsList);
        }

        private static MedicationContent ExtractMedications(MedicationContent originalContent)
        {
            // Add new header to the array
            string[]? newHeaders = ArrayCopyAndAdd(originalContent.Headers, "Medication");
            var newRows = new List<string[]>();

            foreach (var row in originalContent.Rows)
            {
                // Recoge la columna que contiene los medicamentos
                var t = row[originalContent.TColumnIndex];
                var medications = ApiClient.CallToApi(t);

                var newRow = ArrayCopyAndAdd(row, medications);

                newRows.Add(newRow);
            }

            Console.WriteLine(string.Join(",", newHeaders));
            foreach (var row in newRows)
            {
                Console.WriteLine(string.Join(",", row));
            }

            return new MedicationContent()
            {
                Headers = newHeaders,
                Rows = newRows,
                TColumnIndex = originalContent.TColumnIndex
            };
        }

        private static void WriteMedication(MedicationContent content)
        {
            
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

        private class MedicationContent
        {
            public string[] Headers { get; set; }
            public List<string[]> Rows { get; set; }
            public int TColumnIndex { get; set; }
        }
    }
}
