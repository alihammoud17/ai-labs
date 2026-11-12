using AiLab.Application;
using AiLab.Console.Chat;
using AiLab.Domain.Constants;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Logging;
using AiLab.Infrastructure.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
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

// The SaaS API's MCP tools for /saas (connects only on /saas on, so the console runs without the SaaS API).
builder.Services.AddSaasMcp(builder.Configuration);

builder.Services.AddFileLogging(builder.Configuration, "ai");

// The chat history and session cost, /extract and /eval
builder.Services.AddApplication();

builder.Services.AddChatClient(sp => sp.GetRequiredKeyedService<IChatClient>(ChatClientKeys.Provider))
// The first Use... is the outermost layer: function invocation → logging → provider (same order as the API).
// Function invocation runs the tool loop and calls the layer below once per round trip, so logging (inside the loop)
// records every model call, including the tool-call steps. With /saas off no tools are sent, so it just passes through.
// The cap counts model calls including the first, so a confused model can't loop. No detailed errors: a failing tool
// sends the model only "Error: Function failed.", never the exception's text.
.UseFunctionInvocation(configure: f =>
{
    f.MaximumIterationsPerRequest = builder.Configuration.GetValue<int>("SaasMcp:MaxToolIterations", 3);
    f.IncludeDetailedErrors = false;
})
// Logs every call: Debug = call info, Verbose (Trace) = full messages and options. Levels are set in appsettings.json.
.UseLogging();

// Embeddings (Embedding:* in appsettings.json), logged to the same file as chat calls
builder.Services.AddEmbeddingGenerator(sp => sp.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(EmbeddingGeneratorKeys.Provider))
.UseLogging();

builder.Services.AddSingleton<TurnCancellation>();
builder.Services.AddSingleton<JobCommands>();
builder.Services.AddSingleton<EmbeddingCommands>();
builder.Services.AddSingleton<ChatSession>();

// The host is only built, not run: its console lifetime would take over Ctrl+C, which cancels a streaming reply
var host = builder.Build();

Console.WriteLine($"Logs: {FileLogging.Folder}");
await host.Services.GetRequiredService<ChatSession>().RunAsync();
