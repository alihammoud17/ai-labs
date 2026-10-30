using AiLab.Application;
using AiLab.Domain.Constants;
using AiLab.Domain.Settings;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Files;
using AiLab.Infrastructure.Logging;
using AiLab.Infrastructure.VectorStore;
using AiLab.Rag.Endpoints;
using AiLab.Rag.Middleware;
using AiLab.Web;
using AiLab.Rag.OpenApi;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.AI;

// Content root = the output folder, so appsettings.json is found wherever the app is started from (like the other hosts).
// In Development (launchSettings.json) the host also loads user-secrets, which hold ConnectionStrings:Vectors.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// The keyed provider client and the keyed embedding generator (Embedding:* in appsettings.json). No resilience settings for now.
builder.Services.AddInfrastructure();
builder.Services.AddFileLogging(builder.Configuration, "rag");
builder.Services.AddApplication();

// AddApplication registers chat-based services (ticket analyzer, job extractor), so Development's ValidateOnBuild
// needs an IChatClient. POST /ask will use it too. Chat:* in appsettings.json; read on first resolve.
builder.Services.AddChatClient(sp => sp.GetRequiredKeyedService<IChatClient>(ChatClientKeys.Provider))
.UseLogging();

// Embeddings, logged to the rag- file like the console logs them to its ai- file
builder.Services.AddEmbeddingGenerator(sp => sp.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(EmbeddingGeneratorKeys.Provider))
.UseLogging();

// ITextVectorStore over the VectorStore:Provider collection (inmemory or postgres). Only this host has a vector store.
builder.Services.AddVectorStore(builder.Configuration);

// Embedding:UseTaskPrefixes (default false): nomic-embed-text's "search_document: " / "search_query: " prefixes.
// Changing it means seeding again, since the stored vectors were made with the other setting.
builder.Services.AddSingleton(new VectorSearchSettings
{
    UseTaskPrefixes = builder.Configuration.GetValue<bool>("Embedding:UseTaskPrefixes"),
});

// Ask:* (POST /ask): top, distance gate, output limit, prompt path. Defaults in AskSettings when the section is missing.
// PromptPath is relative to the repo root, like Seed:Path, so it's made absolute here (AskService reads it on each request).
var ask = builder.Configuration.GetSection("Ask").Get<AskSettings>() ?? new();
builder.Services.AddSingleton(ask with { PromptPath = RepoPaths.Resolve(ask.PromptPath) });

// As in the API: the web defaults also accept numbers sent as strings, which made every number in the OpenAPI schema
// "integer or string". Strict handling keeps the document plain, and makes "top": "5" in a POST /ask body a 400 invalid_request.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// OpenAPI document at /openapi/v1.json
builder.Services.AddRagApiDocument();

// Errors as { error, code } (AiLab.Web): Rag's own exceptions (400, 409, 502, 503) in RagExceptionHandler, unreadable
// requests (400) and everything else (500) in the shared part
builder.Services.AddErrorResponses();
builder.Services.AddExceptionHandler<RagExceptionHandler>();

// POST /ask: every allowed request may cost a model call, so Ask:PermitsPerMinute (10) per client IP, in a fixed window.
// Behind a proxy every caller shares the proxy's IP; that would need the forwarded-headers middleware first.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(AskEndpoint.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = ask.PermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));

    // The usual { error, code } body instead of an empty 429, and Retry-After when the limiter knows it
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        await ErrorResponses.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, ErrorCodes.RateLimited,
            $"Too many questions: at most {ask.PermitsPerMinute} per minute. Try again later.");
    };
});

var app = builder.Build();

// Exceptions become { error, code } (AiLab.Web, shared with the ticket API): RagExceptionHandler first, then a 500
app.UseErrorResponses();

// After routing (WebApplication adds it first), so the /ask policy is found from the endpoint
app.UseRateLimiter();

app.MapGet("/health", () => new HealthResponse("ok"))
    .WithTags("Health")
    .WithSummary("Is the host up? (Doesn't check Ollama or Postgres)");

// GET /search?q=...&top=5
app.MapSearch();

// POST /ask { "question": "...", "top": 5 }
app.MapAsk();

// The API description and its UI are for development only: /openapi/v1.json and /swagger.
// POST /dev/seed fills the store from inputs/similarity-sentences.txt (Seed:Path), POST /dev/ingest from data/corpus
// (Corpus:Path), and GET /dev/chunks lists what was stored.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "AiLab vector search v1"));
    app.MapDevEndpoints();
}

Console.WriteLine($"Logs: {FileLogging.Folder}");
app.Run();

// Public so AiLab.Rag.Tests can use WebApplicationFactory<Program>
public partial class Program;

// A named type, so the OpenAPI document shows HealthResponse instead of an anonymous type
public sealed record HealthResponse(string Status);
