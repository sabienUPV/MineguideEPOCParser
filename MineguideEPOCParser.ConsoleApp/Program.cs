// See https://aka.ms/new-console-template for more information

using MineguideEPOCParser.ConsoleApp;
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

// Add logger to configuration
testConfiguration.Logger = log;

Console.WriteLine("Application started.");
Console.WriteLine("Press the ENTER key to cancel...\n");

Task cancelTask = Task.Run(() =>
{
	while (Console.ReadKey().Key != ConsoleKey.Enter)
	{
		Console.WriteLine("Press ENTER key to cancel the operation...");
	}

	Console.WriteLine("Cancelling operation...");
	cts.Cancel();
});

try
{
	await MedicationParser.ParseMedication(testConfiguration, cts.Token);
	Console.WriteLine("ParseMedication completed");
}
catch (OperationCanceledException)
{
	Console.WriteLine("ParseMedication has been cancelled.");
}
