namespace MineguideEPOCParser.Core
{
    public class MedicationMatch
    {
        public int StartIndex { get; set; }
        public int Length { get; set; }
        public required string Text { get; set; }
        public required string OriginalMedication { get; set; } // The medication from your array
        
        private string? _correctedMedication;
        public string CorrectedMedication
        {
            get => _correctedMedication ?? OriginalMedication;
            set => _correctedMedication = value;
        }


        // Comparer by StartIndex
        public class StartIndexComparer : IComparer<MedicationMatch>
        {
            public int Compare(MedicationMatch? x, MedicationMatch? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                return x.StartIndex.CompareTo(y.StartIndex);
            }
        }

        // Comparer instance
        public static IComparer<MedicationMatch> Comparer { get; } = new StartIndexComparer();
    }
}
