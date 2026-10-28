using System.ComponentModel;
using System.Diagnostics;
using AiLab.Application.Chunking;
using AiLab.Domain.Constants;
using AiLab.Domain.DTOs;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Domain.Responses;
using AiLab.Infrastructure.Files;
using AiLab.Rag.OpenApi;
using AiLab.Web;

namespace AiLab.Rag.Endpoints;

// Endpoints for Development only; Program.cs maps them inside its IsDevelopment() check
public static class DevEndpoints
{
    private const string DefaultSeedPath = "inputs/similarity-sentences.txt";
    private const string DefaultCorpusPath = "data/corpus";
    private const string Both = "both";

    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapSeed();
        app.MapIngest();
        app.MapChunks();
        return app;
    }

    // Seeds the store with one chunk per non-blank line of Seed:Path. Chunker "lines", Position = the line number, so keys are
    // "lines:<file name>:<line number>" and seeding again replaces the rows. A relative path is taken from the repo root, not the working directory: dotnet run starts a web
    // project in its own folder (AiLab.Rag/), wherever it was typed.
    private static IEndpointConventionBuilder MapSeed(this IEndpointRouteBuilder app) =>
        app.MapPost("/dev/seed", async (IVectorSearchService search, IConfiguration config, ILoggerFactory loggers, CancellationToken cancellationToken) =>
        {
            ILogger logger = loggers.CreateLogger(typeof(DevEndpoints).FullName!);
            string path = RepoPaths.Resolve(config["Seed:Path"] is { Length: > 0 } configured ? configured : DefaultSeedPath);
            logger.LogInformation("Seeding from {Path}", path);

            if (!File.Exists(path))
                return ErrorResponses.Result(StatusCodes.Status400BadRequest, ErrorCodes.SeedFileNotFound,
                    $"Seed file not found: {path}. Check Seed:Path (relative to the repo root).");

            string source = Path.GetFileName(path);
            string[] lines = await File.ReadAllLinesAsync(path, cancellationToken);
            // Numbered before blanks are skipped, so a key always points at the same line of the file
            TextChunk[] chunks = lines
                .Select((line, i) => (Text: line.Trim(), LineNumber: i + 1))
                .Where(l => l.Text.Length > 0)
                .Select(l => new TextChunk(ChunkerNames.Lines, source, l.LineNumber, l.Text, Heading: null, l.LineNumber, l.LineNumber))
                .ToArray();

            var stopwatch = Stopwatch.StartNew();
            IndexResult indexed = await search.IndexAsync(chunks, cancellationToken);
            return Results.Ok(new SeedResponse(indexed.Indexed, source, config["VectorStore:Provider"] ?? "", stopwatch.ElapsedMilliseconds));
        })
        .WithName("Seed")
        .WithTags("Dev")
        .WithSummary("Store the example sentences (Development only)")
        .WithDescription("""
            Embeds every non-blank line of `inputs/similarity-sentences.txt` (setting `Seed:Path`, relative to the repo root)
            and stores it as one chunk, with chunker `lines`. Keys are `lines:<file name>:<line number>`, so seeding again
            replaces the rows instead of adding to them. Free on Ollama.
            """)
        .Produces<SeedResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
        .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
        .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
        .AddOpenApiOperationTransformer(RagExamples.ApplySeed);

    // Chunks every .md under Corpus:Path (relative to the repo root, like Seed:Path) with one or both chunkers and stores them.
    // Source = the path relative to the corpus folder with / separators, so keys look like "heading:docs/contracts.md:3".
    // One SyncAsync per chunker: only chunks whose text changed are embedded (each file still gets its own token count),
    // positions a shorter file no longer has are deleted, and so are the chunks of a file that left the corpus.
    // force=true embeds everything again, for a new embedding model or task-prefix setting (the hash covers the text only).
    private static IEndpointConventionBuilder MapIngest(this IEndpointRouteBuilder app) =>
        app.MapPost("/dev/ingest", async (IVectorSearchService search, IEnumerable<IChunker> chunkers, IConfiguration config,
            ILoggerFactory loggers, CancellationToken cancellationToken,
            [Description("fixed, heading or both (the default).")] string? chunker = null,
            [Description("Embed every chunk again, even unchanged ones. Needed after changing Embedding:Model or Embedding:UseTaskPrefixes.")] bool force = false) =>
        {
            ILogger logger = loggers.CreateLogger(typeof(DevEndpoints).FullName!);
            string requested = string.IsNullOrEmpty(chunker) ? Both : chunker;
            IChunker[] selected = chunkers.Where(c => requested == Both || c.Name == requested).OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
            if (selected.Length == 0)
                return ErrorResponses.Result(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest,
                    $"chunker must be one of: {string.Join(", ", chunkers.Select(c => c.Name))}, {Both}.");

            string root = RepoPaths.Resolve(config["Corpus:Path"] is { Length: > 0 } configured ? configured : DefaultCorpusPath);
            if (!Directory.Exists(root))
                return ErrorResponses.Result(StatusCodes.Status400BadRequest, ErrorCodes.CorpusNotFound,
                    $"Corpus folder not found: {root}. Check Corpus:Path (relative to the repo root).");
            logger.LogInformation("Ingesting {Root} with {Chunkers}", root, string.Join(", ", selected.Select(c => c.Name)));

            var stopwatch = Stopwatch.StartNew();
            var files = new List<(string Source, string Markdown)>();
            foreach (string path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                files.Add((Path.GetRelativePath(root, path).Replace('\\', '/'), await File.ReadAllTextAsync(path, cancellationToken)));

            var perFile = new List<IngestStats>();
            var totals = new List<IngestStats>();
            foreach (IChunker selectedChunker in selected)
            {
                Dictionary<string, IReadOnlyList<TextChunk>> chunksBySource =
                    files.ToDictionary(f => f.Source, f => selectedChunker.Chunk(f.Source, f.Markdown));
                IReadOnlyList<SyncResult> synced = await search.SyncAsync(selectedChunker.Name, chunksBySource, force, cancellationToken);

                List<IngestStats> rows = synced
                    .Select(r => Stats(selectedChunker.Name, r.Source, chunksBySource.ContainsKey(r.Source) ? 1 : 0,
                        chunksBySource.GetValueOrDefault(r.Source) ?? [], r))
                    .ToList();
                perFile.AddRange(rows);

                // Null as soon as one file had no count: a partial sum would look like a real total
                long? tokens = rows.All(r => r.EmbeddingTokens is not null) ? rows.Sum(r => r.EmbeddingTokens) : null;
                var sum = new SyncResult("", synced.Sum(r => r.Chunks), synced.Sum(r => r.Embedded), synced.Sum(r => r.Unchanged),
                    synced.Sum(r => r.Deleted), tokens, synced.Sum(r => r.ElapsedMs));
                totals.Add(Stats(selectedChunker.Name, null, files.Count, chunksBySource.Values.SelectMany(c => c).ToList(), sum));
            }

            return Results.Ok(new IngestResponse(requested, root, config["VectorStore:Provider"] ?? "", stopwatch.ElapsedMilliseconds, perFile, totals));
        })
        .WithName("Ingest")
        .WithTags("Dev")
        .WithSummary("Chunk and store the corpus (Development only)")
        .WithDescription("""
            Reads every `.md` file under `data/corpus` (setting `Corpus:Path`, relative to the repo root), cuts it with the
            `fixed` chunker (windows of about 500 tokens, 50 overlapping), the `heading` chunker (one chunk per section,
            the heading path in front) or `both`, and embeds and stores the chunks in one collection, filtered by `chunker`.

            Keys are `<chunker>:<source>:<position>`. Ingesting again embeds only chunks whose text changed (each chunk's
            SHA-256 is stored), deletes positions a file no longer has, and deletes the chunks of a file that left the folder.
            A second run with no edits reports 0 embedded. `force=true` embeds everything again: needed after changing the
            embedding model or `Embedding:UseTaskPrefixes`, which the hash can't see.

            Returns one row per file and chunker and a total per chunker: chunks made, embedded, unchanged and deleted,
            smallest / average / largest chunk in estimated tokens (characters / 4), the tokens the embedding provider
            actually counted for what it embedded, and the time taken. Free on Ollama.
            """)
        .Produces<IngestResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
        .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
        .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
        .AddOpenApiOperationTransformer(RagExamples.ApplyIngest);

    // The stored chunks in reading order, to look at what a chunker did. No embedding: it reads the store only.
    private static IEndpointConventionBuilder MapChunks(this IEndpointRouteBuilder app) =>
        app.MapGet("/dev/chunks", async (ITextVectorStore store, CancellationToken cancellationToken,
            [Description("Only this chunker's chunks: fixed, heading or lines. Empty or missing: all of them.")] string? chunker = null,
            [Description("Only this file, as stored, for example docs/contracts.md. Empty or missing: all of them.")] string? source = null) =>
        {
            string? chunkerFilter = string.IsNullOrEmpty(chunker) ? null : chunker;
            string? sourceFilter = string.IsNullOrEmpty(source) ? null : source;
            if (chunkerFilter is not null && !ChunkerNames.All.Contains(chunkerFilter))
                return ErrorResponses.Result(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest,
                    $"chunker must be one of: {string.Join(", ", ChunkerNames.All)}.");

            IReadOnlyList<TextChunk> chunks = await store.GetChunksAsync(chunkerFilter, sourceFilter, cancellationToken);
            return Results.Ok(new ChunksResponse(chunkerFilter, sourceFilter, chunks.Count, chunks
                .Select(c => new ChunkResponse(c.Key, c.Chunker, c.Source, c.Position, c.Heading, c.StartLine, c.EndLine,
                    IChunker.EstimateTokens(c.Text), c.ContentHash, c.Text))
                .ToList()));
        })
        .WithName("Chunks")
        .WithTags("Dev")
        .WithSummary("List the stored chunks (Development only)")
        .WithDescription("""
            The stored chunks ordered by source, chunker and position, with their heading path, line range, estimated tokens
            and content hash, for reading what a chunker did: `?chunker=heading&source=docs/contracts.md`.
            """)
        .Produces<ChunksResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
        .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
        .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
        .AddOpenApiOperationTransformer(RagExamples.ApplyChunks);

    // Estimated tokens = characters / 4, the same estimate the chunkers size their windows with
    private static IngestStats Stats(string chunker, string? source, int files, IReadOnlyList<TextChunk> chunks, SyncResult sync)
    {
        int[] tokens = chunks.Select(c => IChunker.EstimateTokens(c.Text)).ToArray();
        return new IngestStats(chunker, source, files, tokens.Length, sync.Embedded, sync.Unchanged, sync.Deleted,
            tokens.Length == 0 ? 0 : tokens.Min(), tokens.Length == 0 ? 0 : (int)Math.Round(tokens.Average()), tokens.Length == 0 ? 0 : tokens.Max(),
            sync.InputTokens, sync.ElapsedMs);
    }
}
