using System.Text;

namespace MineguideEPOCParser.Core
{
	public static class FileEncodingConverter
	{
		/// <summary>
		/// Convert a file from "ISO-8859-1" encoding to "UTF-8" encoding.
		/// </summary>
		public static async Task ConvertToUTF8(string inputFile, string outputFile, CancellationToken cancellationToken = default)
		{
			await ConvertToUTF8("ISO-8859-1", inputFile, outputFile, cancellationToken);
		}

		/// <summary>
		/// Convert a file from the passed encoding to "UTF-8" encoding.
		/// </summary>
		public static async Task ConvertToUTF8(string encoding, string inputFile, string outputFile, CancellationToken cancellationToken = default)
		{
			var text = await File.ReadAllTextAsync(inputFile, cancellationToken);
			var utf8 = Encoding.UTF8.GetString(Encoding.GetEncoding(encoding).GetBytes(text));
			await File.WriteAllTextAsync(outputFile, utf8, cancellationToken);
		}
	}
}
