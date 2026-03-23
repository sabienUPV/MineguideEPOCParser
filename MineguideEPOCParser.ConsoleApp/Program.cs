using ConsoleAppFramework;
using MineguideEPOCParser.Core.Parsers;
using MineguideEPOCParser.Core.Parsers.Configurations;
using Serilog;

// 1. Configure Serilog statically and globally
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Verbose() // Log everything by default
    .WriteTo.Console()
    .CreateLogger();

// 2. Initialize the app
var app = ConsoleApp.Create();

// 3. Register the main command pointing to our method
app.Add("", ParserCommands.RunAsync);

// 4. Run the app
await app.RunAsync(args);

// 5. Ensure all logs are written and flushed before exiting
await Log.CloseAndFlushAsync();

/// <summary>
/// Contains the commands for our console application.
/// </summary>
public static class ParserCommands
{
    /// <summary>
    /// Starts the medication parsing process by extracting the data.
    /// </summary>
    /// <param name="inputFile">-i, The path to the input file to parse.</param>
    /// <param name="outputFile">-o, The path where the output should be saved.</param>
    /// <param name="culture">-c, Culture code for parsing (e.g., 'en-US'). Set to your computer's culture by default.</param>
    public static async Task RunAsync(
        string inputFile,
        string outputFile,
        string? culture = null,
        CancellationToken ct = default)
    {
        Log.Information("Application started. Press Ctrl+C to cancel...\n");

        try
        {
            var runConfiguration = new MedicationExtractingParserConfiguration
            {
                InputFile = inputFile,
                OutputFile = outputFile,
                CultureName = culture ?? System.Globalization.CultureInfo.CurrentCulture.Name
            };

            var medicationParser = new MedicationExtractingParser()
            {
                Configuration = runConfiguration,
                Logger = Log.Logger // Use the static logger
            };

            // ConsoleAppFramework automatically binds the CancellationToken to Ctrl+C (SIGINT)
            await medicationParser.ParseData(ct);
            Log.Information("ParseMedication completed successfully.");
        }
        catch (OperationCanceledException)
        {
            Log.Information("ParseMedication has been cancelled by the user.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred during parsing.");
        }
    }
}