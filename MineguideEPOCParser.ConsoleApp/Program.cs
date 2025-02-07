// See https://aka.ms/new-console-template for more information

using MineguideEPOCParser.Core;
using Serilog;

using CancellationTokenSource cts = new();

await using var log = new LoggerConfiguration()
	.MinimumLevel.Verbose() // Log everything by default
	.WriteTo.Console()
	.CreateLogger();

// TEST JUAN
//var testConfiguration = TestConfigurations.JuanConfig();

// TEST ALEJANDRO
var testConfiguration = TestConfigurations.AlejandroConfig();

log.Information("Application started.");
Console.WriteLine("Press the ENTER key to cancel...\n");

Task cancelTask = Task.Run(() =>
{
	while (Console.ReadKey().Key != ConsoleKey.Enter)
	{
		Console.WriteLine("Press ENTER key to cancel the operation...");
	}

	log.Information("Cancelling operation...");
	cts.Cancel();
});

try
{
	var medicationParser = new MedicationExtractingParser()
	{
		Configuration = testConfiguration,
		Logger = log
	};

	await medicationParser.ParseData(cts.Token);
	log.Information("ParseMedication completed");
}
catch (OperationCanceledException)
{
    log.Information("ParseMedication has been cancelled.");
}
