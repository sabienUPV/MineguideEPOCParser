using System.Net;
using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    public class MeasurementsExtractingParser : DataParser<ComplexMeasurementsExtractingParserConfiguration>
    {
        public const string SystemPrompt = """
        You are meant to parse any medical data sent to you in SPANISH.
        Follow STRICTLY these instructions by order priority:
        - ONLY return the names, values and units of measurements you find AS IS. Don't try to analyze any other context around them. If you see: "FEV1: 50%" or "FVC: 5000ml" or "FEV1/FVC 65%", then that data SHOULD be included, regardless of the origin or correctness. For now, we are just trying to extract these values, not evaluate them.
        - Notice that the same measurement might be included in multiple different units (i.e: ml and %). You should include both of them in different objects. We want all possible representations of measurements, even if it looks redundant. For example, if you have 2 FEV1 measurements in ml and %, and then 2 FVC measurements, also in ml and %, you would end up with 4 JSON objects, 2 for the 2 FEV1 measurements, and another 2 for the other 2 FVC measurements.
        - If the text is blank, return an empty JSON object.
        - The JSON format should be: { "Measurements": [{"Type": <Name of the measurement>, "Value": <number WITHOUT the Unit>, "Unit": <"%" or "l" or "ml" (it should be present AFTER THE NUMBER... if it's not, set it to null>} ] }
        """;

        // 4 Output columns: TextSearched, Type, Value, Unit
        public override int NumberOfOutputAdditionalColumns => 4;

        /// <summary>
        /// Calls the Ollama API to extract the measurements from the text in the input column.
        /// </summary>
        protected override async IAsyncEnumerable<string[]> ApplyTransformations(IAsyncEnumerable<string[]> rows, int inputTargetColumnIndex, string[] headers, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                // Recoge la columna que contiene las medidas
                var t = row[inputTargetColumnIndex];

                // If the input text is HTML encoded, decode it
                if (Configuration.DecodeHtmlFromInput)
				{
					t = WebUtility.HtmlDecode(t);
				}

                // Normalize the spelling of some measurements
                t = NormalizeText(t);

                List<string> subTextsToSearch;
                if (Configuration.MeasurementsToLookFor == null)
                {
                    subTextsToSearch = [t];
                }
                else
                {
                    // Extract the text to search if looking for specific measurements for improved efficiency
                    subTextsToSearch = [];
                    foreach (var text in ExtractTextToSearch(t))
                    {
                        subTextsToSearch.Add(text);
                    }

                    if (subTextsToSearch.Count == 0)
                    {
                        Logger?.Warning("No lines of text contain any of the measurements to look for: {MeasurementsToLookFor}.\n\nOriginal text: {T}", string.Join(", ", Configuration.MeasurementsToLookFor), t);
                        continue;
                    }
                }

                var allMeasurements = new List<Measurement>();

                foreach (var textToSearch in subTextsToSearch)
                {
                    // Call the API to extract the measurements
                    var measurementsData = await ApiClient.CallToApiJson<MeasurementsData>(textToSearch, "llama3.1:latest", SystemPrompt, Logger, cancellationToken);
                    if (measurementsData == null)
                    {
                        Logger?.Warning($"No measurements found in the subtext: {textToSearch}");
                        continue;
                    }
                    allMeasurements.AddRange(measurementsData.Measurements);
                }

                if (allMeasurements.Count == 0)
                {
                    Logger?.Warning($"No measurements found in the text: {t}");
                    continue;
                }

                if (!Configuration.OverwriteInputTargetColumn)
                {
                    // In the 'T' column, replace the multiline original text with a single line text
                    // (only if we are not overwriting it)
                    t = t.Replace('\n', '\t').Replace("\r", "");
                }

                // For all text searched, use a pipe symbol as a separator
                // (because the CSV parser we use with the output doesn't support multiline text)
                var allTextSearched = string.Join("|", subTextsToSearch);

                // Devuelve cada medida en una fila, ordenados alfabéticamente por tipo
                foreach (var measurement in allMeasurements.OrderBy(m => m.Type))
                {
                    // Duplicate the row for each measurement, including the measurement
                    string[] newRow;

                    // If the unit is missing, deduce it
                    if (string.IsNullOrEmpty(measurement.Unit))
                    {
                        measurement.Unit = DeduceMissingUnit(measurement.Value);
                    }

                    if (Configuration.OverwriteInputTargetColumn)
                    {
                        newRow = GenerateNewRowWithOverwrite(row, inputTargetColumnIndex, [allTextSearched, measurement.Type, measurement.Value.ToString(), measurement.Unit]).ToArray();
                    }
                    else
                    {
                        // If we are not overwriting the column, add the exact text that was searched, the measurement type, value and unit to the end
                        newRow = row.Append(allTextSearched).Append(measurement.Type).Append(measurement.Value.ToString()).Append(measurement.Unit).ToArray();

                        // In the 'T' column, replace the multiline original text with a single line text
                        newRow[inputTargetColumnIndex] = t;
                    }

                    yield return newRow;
                }
            }
        }

        /// <summary>
        /// <list type="bullet">
        /// <item>If the value is less than 15, assume liters (l)</item>
        /// <item>If the value is between 15 and 99, assume a percentage (%)</item>
        /// <item>If the value is greater than or equal to 100, assume mililiters (ml)</item>
        /// </list>
        /// </summary>
        private static string DeduceMissingUnit(double value) => value switch
        {
            < 15 => "l",
            < 100 => "%",
            _ => "ml"
        };

        // Improved version of ExtractTextToSearch,
        // that fixes the issue of measurements that contain other measurements
        // finding all the indexes for each measurement (largest to smallest),
        // and checking for already occupied indexes to avoid false positives
        private IEnumerable<string> ExtractTextToSearch(string t)
        {
            if (Configuration.MeasurementsToLookFor == null)
            {
                throw new InvalidOperationException($"{nameof(Configuration)}.{nameof(Configuration.MeasurementsToLookFor)} is null");
            }

            // Order the measurements by length to process the longest ones first
            // (this makes sure that the measurements that contain other measurements are processed first)
            // (i.e: FEV1/FVC would be processed before FEV1 and FVC, which are contained in its name)
            var measurementTypes = Configuration.MeasurementsToLookFor.OrderByDescending(m => m.Length).ToArray();

            // To simplify our algorithm, split the text into lines and process each line separately
            var lines = t.Split('\n');

            foreach (var line in lines)
            {
                foreach (var text in ExtractTextToSearchFromLine(line, measurementTypes))
                {
                    yield return text;
                }
            }
        }

        /// <summary>
        /// Note: Assumes that the measurements are ordered by length in descending order
        /// </summary>
        private static IEnumerable<string> ExtractTextToSearchFromLine(string line, string[] orderedMeasurementTypes)
        {
            // We keep track of the boundaries of the occupied indexes
            // The trick is that even indexes are the start of a measurement type, and odd indexes are the end
            // Example: For "lorem FEV1: 50% ipsum FEV1/FVC 65% dolor", the boundaries would be [6, 10, 22, 30].
            // This means that the type is "FEV1" from 6 to 10, and "FEV1/FVC" from 22 to 30.
            // and thus, the values would be found between 10 and 22, and between 30 and the end of the line, respectively.
            List<int> occupationBoundaries = [];

            foreach (var measurement in orderedMeasurementTypes)
            {
                var currentIndex = -1;

                while (currentIndex < line.Length)
                {
                    var measurementIndex = line.IndexOf(measurement, currentIndex + 1, StringComparison.OrdinalIgnoreCase);
                    if (measurementIndex < 0)
                    {
                        break;
                    }

                    var measurementTypeEndIndex = measurementIndex + measurement.Length;

                    // Update the occupation boundaries (making sure to keep them sorted)
                    // only if this measurement is NOT contained in a previously found measurement
                    if (!IsMeasurementContainedInAnotherMeasurement(measurementIndex, occupationBoundaries))
                    {
                        occupationBoundaries.AddSorted(measurementIndex);
                        occupationBoundaries.AddSorted(measurementTypeEndIndex);
                    }

                    // Update the current index to the end of the measurement type
                    currentIndex = measurementTypeEndIndex;
                }
            }

            // Now we can extract the text between the boundaries
            for (int i = 0; i < occupationBoundaries.Count - 1; i += 2)
            {
                var startIndex = occupationBoundaries[i];
                var indexBetweenTypeAndValue = occupationBoundaries[i + 1];
                var endIndex = i < occupationBoundaries.Count - 2 ? occupationBoundaries[i + 2] : line.Length;

                var text = line[startIndex..endIndex].Trim();

                int measurementTypeLength = indexBetweenTypeAndValue - startIndex;

                // If there are no numbers after the measurement type, we ignore it
                if (AnyNumbersAfterMeasurement(measurementTypeLength, text))
                {
                    yield return text;
                }
            }
        }

        private static bool IsMeasurementContainedInAnotherMeasurement(int measurementIndex, List<int> occupationBoundaries)
        {
            // NOTE: This does not consider invalid words that are made out of two measurements
            // (like, if "Some" was a measurement, "thing" was another, and you found "Something",
            // you would end up with 2 false positives for "Some" and "thing".
            // Luckily, we don't expect this to ever happen in our case, so we can ignore it)

            // Note: This assumes that the occupation boundaries are sorted, and that the list has an even number of elements
            for (int i = 0; i < occupationBoundaries.Count; i += 2)
            {
                var start = occupationBoundaries[i];
                var end = occupationBoundaries[i + 1];
                if (start <= measurementIndex && measurementIndex < end)
                {
                    // The measurement is contained in another measurement
                    return true;
                }
            }
            return false;
        }

        // NOTE: This does NOT take into account the fact that one measurement's name might be contained in another's
        // (i.e: FEV1/FVC contains FEV1 and FVC in its name).
        // So it won't work properly for those, because if FEV1/FVC is found first, both FEV1 and FVC will also match with that
        // and generate a false positive
        [Obsolete("This method is not accurate and should not be used")]
        private IEnumerable<(string Measurement, string Text)> ExtractTextToSearch_OLD(string t)
        {
            if (Configuration.MeasurementsToLookFor == null)
            {
                throw new InvalidOperationException($"{nameof(Configuration)}.{nameof(Configuration.MeasurementsToLookFor)} is null");
            }

            // If we are looking for specific measurements,
            // split the text into lines of text that contain one of these measurements

            var currentIndex = -1;
            while (currentIndex < t.Length)
            {
                List<(int Index, string Measurement)> nextMeasurementsByIndex = [];
                foreach (var measurement in Configuration.MeasurementsToLookFor)
                {
                    var currentMeasurementIndex = t.IndexOf(measurement, currentIndex + 1, StringComparison.OrdinalIgnoreCase);

                    // Save the indexes of any found measurements
                    if (currentMeasurementIndex >= 0)
                    {
                        nextMeasurementsByIndex.Add((currentMeasurementIndex, measurement));
                    }
                }

                // If no measurement was found, finish
                if (nextMeasurementsByIndex.Count == 0)
                {
                    break;
                }

                // We sort the measurements by index to process them in order
                nextMeasurementsByIndex.Sort(Comparer<(int Index, string Measurement)>.Create((x, y) => x.Index.CompareTo(y.Index)));

                // Find the first measurement index
                var nextMeasurement = nextMeasurementsByIndex[0];

                // Find the next line break after the very next measurement
                var nextLineBreakIndex = t.IndexOf('\n', nextMeasurement.Index + 1);

                // Add any measurements before the next line break, each in different strings
                var end = nextLineBreakIndex >= 0 ? nextLineBreakIndex : t.Length;
                for (int i = 1; i < nextMeasurementsByIndex.Count; i++)
                {
                    var lastMeasurement = nextMeasurementsByIndex[i - 1];

                    var currentMeasurement = nextMeasurementsByIndex[i];

                    if (currentMeasurement.Index < end)
                    {
                        var text = t[lastMeasurement.Index..currentMeasurement.Index];

                        // If there are no numbers after the measurement, we ignore it
                        if (AnyNumbersAfterMeasurement(lastMeasurement.Measurement.Length, text))
                        {
                            yield return (lastMeasurement.Measurement, text);
                        }

                        nextMeasurement = currentMeasurement;
                    }
                }

                // Now we process the last measurement before the line break

                if (nextLineBreakIndex < 0)
                {
                    // If there are no more line breaks, add the remaining text and finish
                    var text = t[nextMeasurement.Index..];
                    
                    if (AnyNumbersAfterMeasurement(nextMeasurement.Measurement.Length, text))
                    {
                        yield return (nextMeasurement.Measurement, text);
                    }

                    break;
                }
                else
                {
                    // Add the text from the measurement to the next line break (excluding the line break)
                    var text = t[nextMeasurement.Index..nextLineBreakIndex];

                    if (AnyNumbersAfterMeasurement(nextMeasurement.Measurement.Length, text))
                    {
                        yield return (nextMeasurement.Measurement, text);
                    }

                    if (nextLineBreakIndex + 1 >= t.Length)
                    {
                        // If the line break is the last character, we don't need to continue
                        break;
                    }
                }

                currentIndex = nextLineBreakIndex;
            }
        }

        /// <summary>
        /// Checks if there are any numbers after the measurement in the text.
        /// If there are NOT, then the measurement is not valid for us, since we are expecting a value.
        /// (This can happen with measurements like "FEVI normal" for example, which are not measurable values,
        /// so we should ignore them)
        /// </summary>
        private static bool AnyNumbersAfterMeasurement(int measurementLength, string text)
        {
            // Skip the measurement (we are expecting it to be at the beginning of the text)
            var nextIndex = measurementLength;
            while (nextIndex < text.Length)
            {
                if (char.IsDigit(text[nextIndex]))
                {
                    return true;
                }
                nextIndex++;
            }
            return false;
        }

        private static string NormalizeText(string text)
        {
            // Normalize FEV1 spelling (possible spellings: FEV1, FEV 1, FEVI) and remove carriage returns
            return text.Replace("FEV 1", "FEV1").Replace("FEVI", "FEV1").Replace("\r", "");
        }

        private static string NormalizeText(string measurement, string text)
        {
            // Normalize FEV1 spelling (possible spellings: FEV1, FEV 1, FEVI)
            if (measurement == "FEV 1")
            {
                text = text.Replace("FEV 1", "FEV1");
            }
            else if (measurement == "FEVI")
            {
                text = text.Replace("FEVI", "FEV1");
            }

            // Remove carriage returns
            text = text.Replace("\r", "");

            return text;
        }

        private static IEnumerable<string> GenerateNewRowWithOverwrite(string[] row, int inputTargetColumnIndex, IEnumerable<string> outputValues)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i == inputTargetColumnIndex)
                {
                    foreach (var outputValue in outputValues)
                    {
                        yield return outputValue;
                    }
                }
                else
                {
                    yield return row[i];
                }
            }
        }
    }
}
