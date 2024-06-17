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
    }
}
