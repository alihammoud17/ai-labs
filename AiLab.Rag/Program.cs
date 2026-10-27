using AiLab.Application;
using AiLab.Domain.Constants;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Logging;
using AiLab.Infrastructure.VectorStore;
using AiLab.Rag.Endpoints;
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

// As in the API: the web defaults also accept numbers sent as strings, which made every number in the OpenAPI schema
// "integer or string". Rag takes no JSON bodies, so strict handling changes nothing for callers and keeps the document plain.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// OpenAPI document at /openapi/v1.json
builder.Services.AddRagApiDocument();

var app = builder.Build();

// TODO: the API's UseErrorResponses lives in AiLab.Api/Middleware and can't be referenced from here.
// Move it to a shared place before POST /ask. Until then the endpoints map their own errors (Endpoints/RagErrors.cs).

app.MapGet("/health", () => new HealthResponse("ok"))
    .WithTags("Health")
    .WithSummary("Is the host up? (Doesn't check Ollama or Postgres)");

// GET /search?q=...&top=5
app.MapSearch();

// The API description and its UI are for development only: /openapi/v1.json and /swagger.
// POST /dev/seed fills the store from inputs/similarity-sentences.txt (Seed:Path).
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
