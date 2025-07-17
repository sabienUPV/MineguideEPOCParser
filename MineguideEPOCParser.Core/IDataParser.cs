using Serilog;

namespace MineguideEPOCParser.Core
{
    public interface IDataParser
    {
        ILogger? Logger { get; set; }
        int NumberOfOutputAdditionalColumns { get; }
        IProgress<ProgressValue>? Progress { get; set; }

        Task ParseData(CancellationToken cancellationToken = default);
    }

    public interface IDataParser<TConfiguration> : IDataParser where TConfiguration : DataParserConfiguration
    {
        TConfiguration Configuration { get; set; }
    }
}