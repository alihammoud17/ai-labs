using AiLab.Application;
using AiLab.Console.Chat;
using AiLab.Domain.Constants;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Logging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Provider and model come from appsettings.json; the API key only from user-secrets
// The host loads appsettings.json and, in Development (see launchSettings.json), user-secrets.
// Content root = the output folder, so appsettings.json is found wherever the app is started from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// The keyed provider client (the only place that knows which provider is used). No resilience settings:
// a plain OllamaApiClient and the Anthropic SDK defaults, as the console has always run.
builder.Services.AddInfrastructure();
builder.Services.AddFileLogging(builder.Configuration, "ai");

// The chat history and session cost, /extract and /eval
builder.Services.AddApplication();

builder.Services.AddChatClient(sp => sp.GetRequiredKeyedService<IChatClient>(ChatClientKeys.Provider))
// Logs every call: Debug = call info, Verbose (Trace) = full messages and options. Levels are set in appsettings.json.
.UseLogging();

builder.Services.AddSingleton<TurnCancellation>();
builder.Services.AddSingleton<JobCommands>();
builder.Services.AddSingleton<ChatSession>();

// The host is only built, not run: its console lifetime would take over Ctrl+C, which cancels a streaming reply
var host = builder.Build();

Console.WriteLine($"Logs: {FileLogging.Folder}");
await host.Services.GetRequiredService<ChatSession>().RunAsync();
