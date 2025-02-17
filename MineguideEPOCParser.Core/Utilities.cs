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
