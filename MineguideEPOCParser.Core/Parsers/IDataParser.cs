using MineguideEPOCParser.Core.Parsers.Configurations;
using MineguideEPOCParser.Core.Utils;
using Serilog;

namespace MineguideEPOCParser.Core.Parsers
{
    public interface IDataParser
    {
        ILogger? Logger { get; set; }
        IProgress<ProgressValue>? Progress { get; set; }

        Task ParseData(CancellationToken cancellationToken = default);
    }

    public interface IDataParser<TConfiguration> : IDataParser where TConfiguration : DataParserConfiguration
    {
        TConfiguration Configuration { get; set; }
    }
}