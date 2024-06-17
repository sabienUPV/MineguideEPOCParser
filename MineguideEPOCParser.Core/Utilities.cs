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
    }
}
