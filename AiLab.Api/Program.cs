using AiLab.Api.Endpoints;
using AiLab.Api.Middleware;
using AiLab.Api.OpenApi;
using AiLab.Application;
using AiLab.Domain.Constants;
using AiLab.Domain.Responses;
using AiLab.Domain.Settings;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Logging;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

// Content root = the output folder, so appsettings.json is found wherever the app is started from (like the console app).
// In Development (launchSettings.json) the host also loads user-secrets, which hold Anthropic:ApiKey.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

ResilienceSettings resilience = builder.Configuration.GetSection("Resilience").Get<ResilienceSettings>() ?? new();

// The keyed provider client (the only place that knows which provider is used), Polly on Ollama, the SDK's
// retries for Anthropic, the change log, the file stores and the clock. Keyed, so the integration tests can
// replace just the provider with a scripted fake and still run the real middleware built below.
builder.Services.AddInfrastructure(resilience);
builder.Services.AddFileLogging(builder.Configuration, "api");

// The ticket analyzer, its tool and the ticket eval
builder.Services.AddApplication();

builder.Services.AddChatClient(sp => sp.GetRequiredKeyedService<IChatClient>(ChatClientKeys.Provider))
// The first Use... is the outermost layer: function invocation → logging → provider.
// Function invocation runs the tool loop and calls the layer below once per round trip,
// so logging (inside the loop) records every model call, including the tool-call steps.
// The cap counts model calls including the first, so a confused model can't loop (default 40).
// No detailed errors: a failing tool sends the model only "Error: Function failed.", never the exception's text.
// That's the default; it's set here so the guarantee is visible (a test checks it too).
.UseFunctionInvocation(configure: f =>
{
    f.MaximumIterationsPerRequest = resilience.MaxToolIterations;
    f.IncludeDetailedErrors = false;
})
.UseLogging();

// The outer budget for the whole request. When it fires, the request's CancellationToken is cancelled
// and the middleware answers 504 with a short body. Skipped while a debugger is attached.
builder.Services.AddRequestTimeouts(options => options.AddPolicy("analyze-ticket", new RequestTimeoutPolicy
{
    Timeout = TimeSpan.FromSeconds(resilience.RequestTimeoutSeconds),
    TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
    WriteTimeoutResponse = context => context.Response.WriteAsJsonAsync(new ErrorResponse("The request timed out.", ErrorCodes.Timeout)),
}));

// The web defaults also accept numbers sent as strings, which made every number in the OpenAPI schema "integer or string".
// No request field is a number, so strict handling changes nothing for callers and keeps the document plain.
// Enums as camelCase strings, so causeStatus reads "grounded", not 0.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// OpenAPI document at /openapi/v1.json
builder.Services.AddTicketApiDocument();

var app = builder.Build();

// Unhandled exceptions become the same short { error, code } body as every other error
app.UseErrorResponses();

// Inside the exception handler, so a timeout is answered here (504) before anything is treated as an error
app.UseRequestTimeouts();

// The API description and its UI are for development only: /openapi/v1.json and /swagger
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "AiLab API v1"));

    // The ticket eval (/dev/eval-tickets)
    app.MapDevEndpoints();
}

// Resolve the client once at startup, so a missing provider, model or key fails here and not on the first request
app.Services.GetRequiredService<IChatClient>();

// The request timeout should outlast one model call with all its attempts; a warning, not a crash, so it can be set low on purpose
if (TimeSpan.FromSeconds(resilience.RequestTimeoutSeconds) <= resilience.AllAttempts)
    app.Logger.LogWarning("Resilience:RequestTimeoutSeconds ({RequestTimeout} s) is not longer than attempt timeout × attempts ({AllAttempts} s)",
        resilience.RequestTimeoutSeconds, resilience.AllAttempts.TotalSeconds);

// POST /analyze-ticket
app.MapAnalyzeTicket();

Console.WriteLine($"Logs: {FileLogging.Folder}");
app.Run();

// Public so the integration tests can use WebApplicationFactory<Program> and the provider key
public partial class Program
{
    public const string ProviderClientKey = ChatClientKeys.Provider;
}
