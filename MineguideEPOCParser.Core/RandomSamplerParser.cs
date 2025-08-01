﻿using System.Runtime.CompilerServices;

namespace MineguideEPOCParser.Core
{
    /// <summary>
    /// Takes the input file and randomly samples a specified number of rows.
    /// </summary>
    public class RandomSamplerParser : DataParser<RandomSamplerParserConfiguration>
    {
        public override int NumberOfOutputAdditionalColumns => 0; // No additional output columns, just sampling the input

        private Random? _random;

        protected override void InitParsing()
        {
            _random = new Random(Configuration.Seed);
        }

        protected override async IAsyncEnumerable<string[]> ApplyTransformations(
            IAsyncEnumerable<string[]> rows,
            int inputTargetColumnIndex,
            string[] headers, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Use a dictionary to ensure unique keys efficiently
            var randomizedRows = new Dictionary<int, string[]>();

            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                int key;
                do
                {
                    key = _random!.Next();
                } while (randomizedRows.ContainsKey(key)); // Fast lookup

                randomizedRows.Add(key, row);
            }

            // Sort by random key and take the sample
            foreach (var kvp in randomizedRows.OrderBy(t => t.Key).Take(Configuration.SampleSize))
            {
                yield return kvp.Value;
            }
        }
    }

    public class RandomSamplerParserConfiguration : DataParserConfiguration
    {
        // Randomly generated default seed value for reproducibility.
        public const int DefaultSeed = 1947;
        // Default sample size for the random sampling.
        public const int DefaultSampleSize = 100;

        public int SampleSize { get; set; } = DefaultSampleSize;
        public int Seed { get; set; } = DefaultSeed;

        protected override (string? inputTargetHeader, string[] outputAdditionalHeaders) GetDefaultColumns() => (null, []);
    }
}
