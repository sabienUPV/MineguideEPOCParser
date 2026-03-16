using Cocona;
using MineguideEPOCParser.Core;
using Serilog;

// 1. Keep your Serilog setup exactly as is
await using var log = new LoggerConfiguration()
    .MinimumLevel.Verbose() // Log everything by default
    .WriteTo.Console()
    .CreateLogger();

// 2. Initialize the Cocona App
var app = CoconaApp.Create();

// 3. Define your main command. Cocona maps these parameters to CLI arguments automatically!
app.AddCommand(async (
    [Option('i', Description = "The path to the input file to parse.")] string inputFile,
    [Option('o', Description = "The path where the output should be saved.")] string outputFile,
    [Option('c', Description = "Culture code for parsing (e.g., 'en-US'). Set to your computer's culture by default")] string? culture,
    CancellationToken ct) =>
{
    log.Information("Application started. Press Ctrl+C to cancel...\n");

    try
    {
        // 4. Map the CLI arguments to your configuration object
        // (Replace "YourConfigurationClass" with whatever type TestConfigurations returns)
        var runConfiguration = new MedicationExtractingParserConfiguration
        {
            InputFile = inputFile,
            OutputFile = outputFile,
            CultureName = culture ?? System.Globalization.CultureInfo.CurrentCulture.Name
        };

        var medicationParser = new MedicationExtractingParser()
        {
            Configuration = runConfiguration,
            Logger = log
        };

        // 5. Pass Cocona's built-in cancellation token (triggered by Ctrl+C)
        await medicationParser.ParseData(ct);
        log.Information("ParseMedication completed");
    }
    catch (OperationCanceledException)
    {
        log.Information("ParseMedication has been cancelled by the user.");
    }
    catch (Exception ex)
    {
        log.Error(ex, "An error occurred during parsing.");
    }
});

// 6. Run the application
await app.RunAsync();