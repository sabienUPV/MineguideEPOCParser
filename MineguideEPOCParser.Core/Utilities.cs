using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace MineguideEPOCParser.Core
{
    public static class Utilities
    {
        public static T[] ArrayCopyAndAdd<T>(T[] sourceArray, T elementToAdd)
        {
            // Create a new array with one more element at the end,
            // and copy the original array to it
            var destinationArray = new T[sourceArray.Length + 1];
            Array.Copy(sourceArray, destinationArray, sourceArray.Length);

            // Add element at the end of the array
            destinationArray[^1] = elementToAdd;

            return destinationArray;
        }

        public static T[] ArrayCopyAndAdd<T>(T[] sourceArray, T[] elementsToAdd)
        {
            var destinationArray = new T[sourceArray.Length + elementsToAdd.Length];
            Array.Copy(sourceArray, 0, destinationArray, 0, sourceArray.Length);
            Array.Copy(elementsToAdd, 0, destinationArray, sourceArray.Length, elementsToAdd.Length);
            return destinationArray;
        }

        public static T[] ArrayCopyAndReplace<T>(T[] sourceArray, int indexToReplace, T elementToReplace)
        {
            if (indexToReplace < 0 || indexToReplace >= sourceArray.Length)
            {
                throw new ArgumentException("Index to replace is out of bounds of the source array.", nameof(indexToReplace));
            }

            // Create a new array with the same length as the original array
            var destinationArray = new T[sourceArray.Length];
            // Copiar los elementos antes del índice a reemplazar
            Array.Copy(sourceArray, 0, destinationArray, 0, indexToReplace);
            // Insertar el nuevo elemento
            destinationArray[indexToReplace] = elementToReplace;
            // Copiar los elementos después del índice reemplazado
            Array.Copy(
                sourceArray,
                indexToReplace + 1,
                destinationArray,
                indexToReplace + 1,
                sourceArray.Length - indexToReplace - 1
            );

            return destinationArray;
        }

        public static T[] ArrayCopyAndReplace<T>(T[] sourceArray, int indexToReplace, T[] elementsToReplace)
        {
            if (indexToReplace < 0 || indexToReplace >= sourceArray.Length)
            {
                throw new ArgumentException("Index to replace is out of bounds of the source array.", nameof(indexToReplace));
            }

            var destinationArray = new T[sourceArray.Length + elementsToReplace.Length - 1];

            // Copiar los elementos antes del índice a reemplazar
            Array.Copy(sourceArray, 0, destinationArray, 0, indexToReplace);

            // Insertar los nuevos elementos
            Array.Copy(elementsToReplace, 0, destinationArray, indexToReplace, elementsToReplace.Length);

            // Copiar el resto de los elementos después del índice reemplazado
            Array.Copy(
                sourceArray,
                indexToReplace + 1,
                destinationArray,
                indexToReplace + elementsToReplace.Length,
                sourceArray.Length - indexToReplace - 1
            );

            return destinationArray;
        }

        public static string ArrayEnsureUniqueHeader(string[] headers, string header)
        {
            // If the header is already in the headers, add a number at the end
            if (headers.Contains(header))
            {
                int i = 1;
                while (headers.Contains(header + i))
                {
                    i++;
                }
                return header + i;
            }
            return header;
        }

        /// <summary>
        /// <see href="https://stackoverflow.com/a/46294791"/>
        /// </summary>
        public static void AddSorted<T>(this List<T> list, T value)
        {
            int x = list.BinarySearch(value);
            list.Insert((x >= 0) ? x : ~x, value);
        }

        /// <summary>
        /// <see href="https://stackoverflow.com/a/67928507"/>
        /// </summary>
        public static T? DeserializeObject<T>(string json, JsonSerializerSettings settings, DuplicatePropertyNameHandling duplicateHandling)
        {
            JsonSerializer jsonSerializer = JsonSerializer.CreateDefault(settings);
            using (var stringReader = new StringReader(json))
            using (var jsonTextReader = new JsonTextReader(stringReader))
            {
                jsonTextReader.DateParseHandling = DateParseHandling.None;
                JsonLoadSettings loadSettings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = duplicateHandling
                };
                var jtoken = JToken.ReadFrom(jsonTextReader, loadSettings);
                return jtoken.ToObject<T>(jsonSerializer);
            }
        }
    }
}
