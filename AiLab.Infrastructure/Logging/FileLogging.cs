using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AiLab.Infrastructure.Logging;

// Logs go to a file only, so they never mix with the console output.
// The path is fixed next to the app (like appsettings.json), so it doesn't depend on where the app is started from.
public static class FileLogging
{
    public static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "logs");

    // One file per day: <prefix>-YYYYMMDD.log. Levels come from the Serilog section of appsettings.json.
    public static IServiceCollection AddFileLogging(this IServiceCollection services, IConfiguration configuration, string filePrefix) =>
        services.AddSerilog((_, logger) => logger
            .ReadFrom.Configuration(configuration)
            .WriteTo.File(Path.Combine(Folder, $"{filePrefix}-.log"), rollingInterval: RollingInterval.Day));
}
