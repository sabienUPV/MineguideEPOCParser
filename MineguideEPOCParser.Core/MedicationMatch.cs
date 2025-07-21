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
    }
}
