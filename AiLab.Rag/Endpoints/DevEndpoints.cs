using System.Diagnostics;
using AiLab.Domain.Constants;
using AiLab.Domain.DTOs;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Responses;
using AiLab.Infrastructure.Files;
using AiLab.Rag.OpenApi;

namespace AiLab.Rag.Endpoints;

// Endpoints for Development only; Program.cs maps them inside its IsDevelopment() check
public static class DevEndpoints
{
    private const string DefaultSeedPath = "inputs/similarity-sentences.txt";

    // Seeds the store with one chunk per non-blank line of Seed:Path. Keys are "<file name>:<line number>", so seeding again
    // replaces the rows. A relative path is taken from the repo root, not the working directory: dotnet run starts a web
    // project in its own folder (AiLab.Rag/), wherever it was typed.
    public static IEndpointConventionBuilder MapDevEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/dev/seed", async (IVectorSearchService search, IConfiguration config, ILoggerFactory loggers, CancellationToken cancellationToken) =>
        {
            ILogger logger = loggers.CreateLogger(typeof(DevEndpoints).FullName!);
            string path = RepoPaths.Resolve(config["Seed:Path"] is { Length: > 0 } configured ? configured : DefaultSeedPath);
            logger.LogInformation("Seeding from {Path}", path);

            if (!File.Exists(path))
                return RagErrors.Error(StatusCodes.Status400BadRequest, ErrorCodes.SeedFileNotFound,
                    $"Seed file not found: {path}. Check Seed:Path (relative to the repo root).");

            string source = Path.GetFileName(path);
            string prefix = Path.GetFileNameWithoutExtension(path);
            string[] lines = await File.ReadAllLinesAsync(path, cancellationToken);
            // Numbered before blanks are skipped, so a key always points at the same line of the file
            TextChunk[] chunks = lines
                .Select((line, i) => (Text: line.Trim(), LineNumber: i + 1))
                .Where(l => l.Text.Length > 0)
                .Select(l => new TextChunk($"{prefix}:{l.LineNumber}", l.Text, source))
                .ToArray();

            var stopwatch = Stopwatch.StartNew();
            try
            {
                int indexed = await search.IndexAsync(chunks, cancellationToken);
                return Results.Ok(new SeedResponse(indexed, source, config["VectorStore:Provider"] ?? "", stopwatch.ElapsedMilliseconds));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return RagErrors.ToResult(ex, config, logger);
            }
        })
        .WithName("Seed")
        .WithTags("Dev")
        .WithSummary("Store the example sentences (Development only)")
        .WithDescription("""
            Embeds every non-blank line of `inputs/similarity-sentences.txt` (setting `Seed:Path`, relative to the repo root)
            and stores it as one chunk. Keys are `<file name>:<line number>`, so seeding again replaces the rows instead of
            adding to them. Free on Ollama.
            """)
        .Produces<SeedResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
        .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
        .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
        .AddOpenApiOperationTransformer(RagExamples.ApplySeed);
}
