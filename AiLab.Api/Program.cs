using AiLab.Api;
using AiLab.Api.ChangeLog;
using AiLab.Api.OpenApi;
using AiLab.Api.Tickets;
using AiLab.Api.Tools;
using AiLab.Core.Providers;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.AI;
using Microsoft.OpenApi;
using Serilog;
using System.Text.Json.Serialization;

// Content root = the output folder, so appsettings.json is found wherever the app is started from (like the console app).
// In Development (launchSettings.json) the host also loads user-secrets, which hold Anthropic:ApiKey.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

ResilienceSettings resilience = builder.Configuration.GetSection("Resilience").Get<ResilienceSettings>() ?? new();

// Ollama's HTTP calls go through Polly's standard pipeline: total timeout → retry → circuit breaker → attempt timeout.
// This is the only retry layer for Ollama (OllamaSharp doesn't retry on its own).
// Margin on the total timeout: room for the backoff between attempts (about 2 s then 4 s, with jitter).
TimeSpan ollamaTotalTimeout = resilience.AllAttempts + TimeSpan.FromSeconds(20);
builder.Services.AddHttpClient("ollama", client => client.Timeout = Timeout.InfiniteTimeSpan) // Polly owns the timers, not HttpClient's 100 s default
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = resilience.AttemptTimeout;
        options.Retry.MaxRetryAttempts = resilience.MaxRetries;
        options.TotalRequestTimeout.Timeout = ollamaTotalTimeout;
        // The handler's validation requires sampling ≥ 2 × attempt timeout
        options.CircuitBreaker.SamplingDuration = resilience.AttemptTimeout * 2;
    });

// The only place that knows which provider is used
builder.Services.AddChatClient(sp =>
{
    IConfiguration config = sp.GetRequiredService<IConfiguration>();
    string provider = config["Chat:Provider"] ?? throw new InvalidOperationException("Chat:Provider is missing in appsettings.json.");
    string model = config["Chat:Model"] ?? throw new InvalidOperationException("Chat:Model is missing in appsettings.json.");

    // Anthropic gets no Polly: its SDK already retries (connection errors, timeouts, 408/409/429/5xx, honouring retry-after),
    // so it gets the same attempt timeout and retry count instead. Polly on top would turn one failure into up to 3 × 3 billed calls.
    return ChatClientFactory.Create(provider, model, config["Anthropic:ApiKey"],
        ollamaHttpClient: provider == "ollama" ? sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama") : null,
        ollamaUrl: config["Chat:OllamaUrl"],
        anthropicAttemptTimeout: resilience.AttemptTimeout,
        anthropicMaxRetries: resilience.MaxRetries);
})
// The first Use... is the outermost layer: function invocation → logging → provider.
// Function invocation runs the tool loop and calls the layer below once per round trip,
// so logging (inside the loop) records every model call, including the tool-call steps.
// The cap counts model calls including the first, so a confused model can't loop (default 40).
.UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = resilience.MaxToolIterations)
.UseLogging();

// The outer budget for the whole request. When it fires, the request's CancellationToken is cancelled
// and the middleware answers 504 with a short body. Skipped while a debugger is attached.
builder.Services.AddRequestTimeouts(options => options.AddPolicy("analyze-ticket", new RequestTimeoutPolicy
{
    Timeout = TimeSpan.FromSeconds(resilience.RequestTimeoutSeconds),
    TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
    WriteTimeoutResponse = context => context.Response.WriteAsJsonAsync(new ErrorResponse("The request timed out.")),
}));

// Logs go to a file only, like the console app. The path is fixed next to the app.
string logPath = Path.Combine(AppContext.BaseDirectory, "logs", "api-.log");
builder.Services.AddSerilog((_, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day));

builder.Services.AddSingleton<IChangeLogRepository, InMemoryChangeLogRepository>();
builder.Services.AddSingleton<TicketTools>();
builder.Services.AddSingleton<TicketAnalyzer>();

// The web defaults also accept numbers sent as strings, which made every number in the OpenAPI schema "integer or string".
// No request field is a number, so strict handling changes nothing for callers and keeps the document plain.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// OpenAPI document at /openapi/v1.json, built from the endpoint metadata and the records' [Description]s
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new OpenApiInfo
    {
        Title = "AiLab ticket analysis API",
        Version = "v1",
        Description = """
            Analyzes support tickets for a multi-tenant SaaS with an LLM (Ollama llama3.2 by default, or Claude Haiku).
            The model calls one tool, GetRecentChanges, to read a fake change log, then returns a summary,
            a probable cause and test cases. A lab project: the response includes diagnostics (tool calls, tokens, cost).
            """,
    };
    return Task.CompletedTask;
}));

// Unhandled exceptions become a short problem+json 500 (or 400 for a bad body), never a stack trace, also in Development
builder.Services.AddProblemDetails();

var app = builder.Build();

// In Development a body that can't be read throws BadHttpRequestException; keep its 400 instead of a 500
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});

// Inside the exception handler, so a timeout is answered here (504) before anything is treated as an error
app.UseRequestTimeouts();

// The API description and its UI are for development only: /openapi/v1.json and /swagger
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "AiLab API v1"));
}

// Resolve the client once at startup, so a missing provider, model or key fails here and not on the first request
app.Services.GetRequiredService<IChatClient>();

// The request timeout should outlast one model call with all its attempts; a warning, not a crash, so it can be set low on purpose
if (TimeSpan.FromSeconds(resilience.RequestTimeoutSeconds) <= resilience.AllAttempts)
    app.Logger.LogWarning("Resilience:RequestTimeoutSeconds ({RequestTimeout} s) is not longer than attempt timeout × attempts ({AllAttempts} s)",
        resilience.RequestTimeoutSeconds, resilience.AllAttempts.TotalSeconds);

// The endpoint only validates and maps outcomes to status codes; the work is in TicketAnalyzer.
// The CancellationToken is the request's (client disconnect, later the request timeout) and goes all the way down.
app.MapPost("/analyze-ticket", async (AnalyzeTicketRequest request, TicketAnalyzer analyzer, CancellationToken cancellationToken) =>
{
    if (request.Validate() is { } errors)
        return Results.ValidationProblem(errors);

    try
    {
        return Results.Ok(await analyzer.AnalyzeAsync(request, cancellationToken));
    }
    catch (ModelCallException ex)
    {
        return Results.Json(new ErrorResponse(ex.Message), statusCode: StatusCodes.Status502BadGateway);
    }
})
// The endpoint returns IResult, so OpenAPI can't see the response types; they are declared here
.WithName("AnalyzeTicket")
.WithTags("Tickets")
.WithSummary("Analyze a support ticket")
.WithDescription("""
    Sends the ticket to the model with one tool, GetRecentChanges(component). The model calls it to read the
    component's recent changes (up to 3 tool round trips, then one final answer), and the reply is parsed into
    a summary, a probable cause and test cases. The probable cause must name a change from the change log, or say
    the cause is unknown. Each call costs real money on Anthropic (about $0.001 on Haiku); Ollama is free.
    """)
.Produces<AnalyzeTicketResponse>(StatusCodes.Status200OK)
.ProducesValidationProblem()
.Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
.Produces<ErrorResponse>(StatusCodes.Status504GatewayTimeout)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.AddOpenApiOperationTransformer((operation, _, cancellationToken) => AnalyzeTicketExamples.Apply(operation, cancellationToken))
// Last: it returns the general IEndpointConventionBuilder, which has no Produces
.WithRequestTimeout("analyze-ticket");

Console.WriteLine($"Logs: {Path.GetDirectoryName(logPath)}");
app.Run();
