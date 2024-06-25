namespace MineguideEPOCParser.Core
{
    public readonly struct ProgressValue
    {
        /// <summary>
        /// Progress value between 0 and 1.
        /// </summary>
        public double Value { get; init; }
        public int? RowsProcessed { get; init; }
    }
}
