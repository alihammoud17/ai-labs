using AiLab.Api.OpenApi;
using AiLab.Application;
using AiLab.Domain.Constants;
using AiLab.Domain.DTOs;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Responses;
using AiLab.Domain.Settings;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Logging;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.AI;
using Microsoft.OpenApi;
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

var app = builder.Build();

// Unhandled exceptions become the same short { error, code } body as every other error: a 500, never a stack trace,
// also in Development. A body that can't be read throws BadHttpRequestException; it keeps its 400.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    ExceptionHandler = context =>
    {
        Exception? ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        (int status, ErrorResponse body) = ex is BadHttpRequestException bad
            ? (bad.StatusCode, new ErrorResponse("The request body could not be read.", ErrorCodes.InvalidRequest))
            : (StatusCodes.Status500InternalServerError, new ErrorResponse("Something went wrong.", ErrorCodes.InternalError));
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(body);
    },
});

// Inside the exception handler, so a timeout is answered here (504) before anything is treated as an error
app.UseRequestTimeouts();

// The API description and its UI are for development only: /openapi/v1.json and /swagger
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "AiLab API v1"));

    // The ticket eval: every TestTickets/ticket-*.json, N runs each, scored against expected.json.
    // No request timeout: 12 Haiku requests take minutes. maxCost stops it once the summed cost passes the limit.
    app.MapPost("/dev/eval-tickets", async (ITicketEval eval, CancellationToken cancellationToken, int runs = 3, decimal? maxCost = null) =>
        runs is < 1 or > 10
            ? Results.BadRequest(new ErrorResponse("runs must be between 1 and 10.", ErrorCodes.InvalidRequest))
            : Results.Text(await eval.RunAsync(runs, maxCost, cancellationToken)))
    .WithTags("Dev")
    .WithSummary("Run and score every test ticket (Development only)")
    .WithDescription("Each run is a real model call: free on Ollama, about $0.0006 per ticket on Haiku. Replies are saved under eval-runs/.");
}

// Resolve the client once at startup, so a missing provider, model or key fails here and not on the first request
app.Services.GetRequiredService<IChatClient>();

// The request timeout should outlast one model call with all its attempts; a warning, not a crash, so it can be set low on purpose
if (TimeSpan.FromSeconds(resilience.RequestTimeoutSeconds) <= resilience.AllAttempts)
    app.Logger.LogWarning("Resilience:RequestTimeoutSeconds ({RequestTimeout} s) is not longer than attempt timeout × attempts ({AllAttempts} s)",
        resilience.RequestTimeoutSeconds, resilience.AllAttempts.TotalSeconds);

// The endpoint only validates and maps outcomes to status codes; the work is in TicketAnalyzer.
// The CancellationToken is the request's (client disconnect, later the request timeout) and goes all the way down.
app.MapPost("/analyze-ticket", async (AnalyzeTicketRequest request, ITicketAnalyzer analyzer, CancellationToken cancellationToken) =>
{
    if (request.Validate() is { } errors)
        return Results.ValidationProblem(errors);

    try
    {
        return Results.Ok(await analyzer.AnalyzeAsync(request, cancellationToken));
    }
    catch (ModelCallException ex)
    {
        return Results.Json(new ErrorResponse(ex.Message, ex.Code), statusCode: StatusCodes.Status502BadGateway);
    }
    catch (TicketRejectedException ex)
    {
        return Results.Json(new ErrorResponse(ex.Message, ex.Code), statusCode: StatusCodes.Status422UnprocessableEntity);
    }
})
// The endpoint returns IResult, so OpenAPI can't see the response types; they are declared here
.WithName("AnalyzeTicket")
.WithTags("Tickets")
.WithSummary("Analyze a support ticket")
.WithDescription("""
    An AI model reads the ticket and the component's recent changes (the tool GetRecentChanges, up to 3 tool round
    trips, then one final answer).

    **It can:** restate the problem, suggest a probable cause from the component's recent deployed changes,
    and draft test cases.

    **It can't:** see code, logs, tenant data or anything outside the change log. It can't confirm that a change
    really explains the problem, and it often says the cause is unknown. Answers vary between calls.

    **Reading the response:** `aiAnalysis` is the model's guess. `verification` is checked by the server against
    what the tool returned in this request:
    - `grounded`: the cause cites only versions the tool returned. Real evidence, not proof: the change may still not explain the problem.
    - `unknown`: the model says the cause is unknown.
    - `unverified`: the cause cites a version the tool didn't return, or names a cause without a version. Treat it as a guess.

    `evidence` lists the change-log entries a grounded cause relies on, copied by the server.
    Each call costs real money on Anthropic (about $0.001 on Haiku); Ollama is free.
    """)
.Produces<AnalyzeTicketResponse>(StatusCodes.Status200OK)
.ProducesValidationProblem()
.Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
.Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
.Produces<ErrorResponse>(StatusCodes.Status504GatewayTimeout)
.Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
.AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
    AnalyzeTicketExamples.Apply(operation, context.ApplicationServices.GetRequiredService<ITestTicketStore>(), cancellationToken))
// Last: it returns the general IEndpointConventionBuilder, which has no Produces
.WithRequestTimeout("analyze-ticket");

Console.WriteLine($"Logs: {FileLogging.Folder}");
app.Run();

// Public so the integration tests can use WebApplicationFactory<Program> and the provider key
public partial class Program
{
    public const string ProviderClientKey = ChatClientKeys.Provider;
}
