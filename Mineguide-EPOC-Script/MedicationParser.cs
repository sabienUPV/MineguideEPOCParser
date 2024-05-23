using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using CsvHelper;
using System.Globalization;

namespace Mineguide_EPOC_Script
{
    public static class MedicationParser
    {
        private static MedicationReadResult ReadMedication()
        {
            return ReadMedication(
                "es-ES", 
                "C:\\Users\\pjval\\OneDrive\\Prácticas 2024\\Mineguide-EPOC\\Bronquiolitis actualizado\\medicamentosUTF-8.csv"
                );
        }
        
        /// <summary>
        /// Este método se encarga de leer el archivo csv y devolver un objeto 'MedicationReadResult'
        /// que contiene los headers, las rows y el index de la columna 'T' renombrada a 'Medication'
        /// </summary>
        /// <param name="cultureName"></param>
        /// <param name="filepath"></param>
        /// <returns>result</returns>
        private static MedicationReadResult ReadMedication(string cultureName, string filepath)
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

            var result = new MedicationReadResult()
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
                    headerArray[i] = "Medication";
                    return i;
                }
            }

            throw new Exception("No se ha encontrado la columna solicitada.");
        }

        public static void ParseMedication()
        {
            var result = ReadMedication();

            if (result.Headers == null || result.Rows == null)
            {
                Console.WriteLine("Los headers o las filas son null");
                return;
            }

            if (result.TColumnIndex <= 0)
            {
                Console.WriteLine("El índice de T no es valido");
                return;
            }

            var rowsWithMedicationsList = ExtractMedications(result.Rows, result.TColumnIndex);

            WriteMedication(result.Headers, rowsWithMedicationsList);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rows"></param>
        /// <param name="tIndex"></param>
        /// <returns>result</returns>
        private static List<string[]> ExtractMedications(ICollection<string[]> rows, int tIndex)
        {
            var result = new List<string[]>();

            foreach (var row in rows)
            {
                // Recoge la columna que contiene los medicamentos
                var t = row[tIndex];
                var medications = ApiClient.CallToApi(t);

                var newRow = new string[row.Length];
                Array.Copy(row, newRow, row.Length);

                newRow[tIndex] = medications;

                result.Add(newRow);
            }

            return result;
        }

        private static void WriteMedication(string[] header, List<string[]> rows)
        {
            
        }

        private class MedicationReadResult
        {
            public string[]? Headers { get; set; }
            public List<string[]>? Rows { get; set; }
            public int TColumnIndex { get; set; }
        }
    }
}
