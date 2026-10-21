using AiLab.Console.Chat;
using AiLab.Console.Providers;
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

// The only place that knows which provider is used
builder.Services.AddChatClient(sp =>
{
    IConfiguration config = sp.GetRequiredService<IConfiguration>();
    string provider = config["Chat:Provider"] ?? throw new InvalidOperationException("Chat:Provider is missing in appsettings.json.");
    string model = config["Chat:Model"] ?? throw new InvalidOperationException("Chat:Model is missing in appsettings.json.");

    return ChatClientFactory.Create(provider, model, config["Anthropic:ApiKey"]);
});

builder.Services.AddSingleton<ChatSession>();

// The host is only built, not run: its console lifetime would take over Ctrl+C, which cancels a streaming reply
var host = builder.Build();

await host.Services.GetRequiredService<ChatSession>().RunAsync();
