using Mineguide_EPOC_Script;

namespace Mineguide_EPOC_Script
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            await MedicationParser.ParseMedication();
        }
    }
}