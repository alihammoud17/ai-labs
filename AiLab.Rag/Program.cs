using AiLab.Application;
using AiLab.Domain.Constants;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Logging;
using AiLab.Infrastructure.VectorStore;
using AiLab.Rag.Endpoints;
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

var app = builder.Build();

// TODO: the API's UseErrorResponses lives in AiLab.Api/Middleware and can't be referenced from here.
// Move it to a shared place before POST /ask. Until then the endpoints map their own errors (Endpoints/RagErrors.cs).

app.MapGet("/health", () => new { status = "ok" });

// GET /search?q=...&top=5
app.MapSearch();

// POST /dev/seed: fills the store from inputs/similarity-sentences.txt (Seed:Path)
if (app.Environment.IsDevelopment())
    app.MapDevEndpoints();

Console.WriteLine($"Logs: {FileLogging.Folder}");
app.Run();

// Public so AiLab.Rag.Tests can use WebApplicationFactory<Program>
public partial class Program;
