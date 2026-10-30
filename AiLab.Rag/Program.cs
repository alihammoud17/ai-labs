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
using System.Text.Json.Serialization;
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
// "integer or string". Rag takes no JSON bodies, so strict handling changes nothing for callers and keeps the document plain.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// OpenAPI document at /openapi/v1.json
builder.Services.AddRagApiDocument();

// Errors as { error, code } (AiLab.Web): Rag's own exceptions (400, 409, 502, 503) in RagExceptionHandler, unreadable
// requests (400) and everything else (500) in the shared part
builder.Services.AddErrorResponses();
builder.Services.AddExceptionHandler<RagExceptionHandler>();

var app = builder.Build();

// Exceptions become { error, code } (AiLab.Web, shared with the ticket API): RagExceptionHandler first, then a 500
app.UseErrorResponses();

app.MapGet("/health", () => new HealthResponse("ok"))
    .WithTags("Health")
    .WithSummary("Is the host up? (Doesn't check Ollama or Postgres)");

// GET /search?q=...&top=5
app.MapSearch();

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
