using AiLab.Api.ChangeLog;
using AiLab.Core.Providers;
using Microsoft.Extensions.AI;
using Serilog;

// Content root = the output folder, so appsettings.json is found wherever the app is started from (like the console app).
// In Development (launchSettings.json) the host also loads user-secrets, which hold Anthropic:ApiKey.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// The only place that knows which provider is used
builder.Services.AddChatClient(sp =>
{
    IConfiguration config = sp.GetRequiredService<IConfiguration>();
    string provider = config["Chat:Provider"] ?? throw new InvalidOperationException("Chat:Provider is missing in appsettings.json.");
    string model = config["Chat:Model"] ?? throw new InvalidOperationException("Chat:Model is missing in appsettings.json.");

    return ChatClientFactory.Create(provider, model, config["Anthropic:ApiKey"]);
})
// The first Use... is the outermost layer: function invocation → logging → provider.
// Function invocation runs the tool loop and calls the layer below once per round trip,
// so logging (inside the loop) records every model call, including the tool-call steps.
.UseFunctionInvocation()
.UseLogging();

// Logs go to a file only, like the console app. The path is fixed next to the app.
string logPath = Path.Combine(AppContext.BaseDirectory, "logs", "api-.log");
builder.Services.AddSerilog((_, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day));

builder.Services.AddSingleton<IChangeLogRepository, InMemoryChangeLogRepository>();

var app = builder.Build();

// Resolve the client once at startup, so a missing provider, model or key fails here and not on the first request
app.Services.GetRequiredService<IChatClient>();

Console.WriteLine($"Logs: {Path.GetDirectoryName(logPath)}");
app.Run();
