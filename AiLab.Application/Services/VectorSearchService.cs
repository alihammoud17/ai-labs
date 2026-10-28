using System.Diagnostics;
using AiLab.Domain.Constants;
using AiLab.Domain.DTOs;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiLab.Application.Services;

public sealed class VectorSearchService(
    ITextVectorStore store,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILogger<VectorSearchService> logger,
    bool useTaskPrefixes = false) : IVectorSearchService
{
    private const int MaxTop = 20;

    // nomic-embed-text's task prefixes (Embedding:UseTaskPrefixes). Only the embedding input gets them:
    // the stored Text and the returned hits stay as they were.
    public const string DocumentPrefix = "search_document: ";
    public const string QueryPrefix = "search_query: ";

    // Chunks per embedding call: one call for a whole corpus can outlast OllamaSharp's 100 s HttpClient timeout
    public const int EmbeddingBatchSize = 32;

    public async Task<IndexResult> IndexAsync(IReadOnlyList<TextChunk> chunks, CancellationToken ct = default)
    {
        if (chunks.Count == 0)
            return new IndexResult(0, 0);
        Validate(chunks);

        // Indexing is a write, so it may create the collection; search never does
        await store.EnsureCreatedAsync(ct);

        var stopwatch = Stopwatch.StartNew();
        var vectors = new List<ReadOnlyMemory<float>>(chunks.Count);
        long? totalTokens = null;
        foreach (TextChunk[] batch in chunks.Chunk(EmbeddingBatchSize))
        {
            var generated = await embeddings.GenerateAsync(batch.Select(c => useTaskPrefixes ? DocumentPrefix + c.Text : c.Text), cancellationToken: ct);
            if (generated.Count != batch.Length)
                throw new InvalidOperationException($"Expected {batch.Length} embeddings, got {generated.Count}.");

            vectors.AddRange(generated.Select(e => e.Vector));
            if (generated.Usage?.InputTokenCount is { } tokens)
                totalTokens = (totalTokens ?? 0) + tokens;
            // Tokens per chunk: Ollama silently cuts inputs longer than the model's context, so watch for chunks near the limit
            logger.LogDebug("Embedded {Count} chunks ({Tokens} tokens)", batch.Length, generated.Usage?.InputTokenCount);
        }
        long embedMs = stopwatch.ElapsedMilliseconds;

        await store.UpsertAsync(chunks, vectors, ct);

        logger.LogInformation("Indexed {Count} chunks ({Tokens} tokens): embedding {EmbedMs} ms, upsert {UpsertMs} ms",
            chunks.Count, totalTokens, embedMs, stopwatch.ElapsedMilliseconds - embedMs);
        return new IndexResult(chunks.Count, totalTokens);
    }

    public async Task<IReadOnlyList<SyncResult>> SyncAsync(string chunker, IReadOnlyDictionary<string, IReadOnlyList<TextChunk>> chunksBySource,
        bool force = false, CancellationToken ct = default)
    {
        // Every file is checked before anything is written, so one bad file doesn't leave the others half synced
        foreach ((string source, IReadOnlyList<TextChunk> chunks) in chunksBySource)
        {
            TextChunk? stray = chunks.FirstOrDefault(c => c.Chunker != chunker || c.Source != source);
            if (stray is not null)
                throw new ArgumentException($"Chunk '{stray.Key}' doesn't belong to chunker '{chunker}' and source '{source}'.", nameof(chunksBySource));
            Validate(chunks);
        }

        await store.EnsureCreatedAsync(ct);

        // What's stored now for this chunker: key -> hash, by source. The other chunkers' rows are never read or touched.
        Dictionary<string, Dictionary<string, string>> stored = (await store.GetChunksAsync(chunker, ct: ct))
            .GroupBy(c => c.Source)
            .ToDictionary(g => g.Key, g => g.ToDictionary(c => c.Key, c => c.ContentHash));

        var results = new List<SyncResult>();
        foreach ((string source, IReadOnlyList<TextChunk> chunks) in chunksBySource.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var stopwatch = Stopwatch.StartNew();
            Dictionary<string, string> before = stored.GetValueOrDefault(source) ?? [];

            // New key or different text. Same text at a new position is a new key, so inserting a section near the top
            // of a file re-embeds everything after it in the fixed chunker.
            TextChunk[] changed = force
                ? chunks.ToArray()
                : chunks.Where(c => !before.TryGetValue(c.Key, out string? hash) || hash != c.ContentHash).ToArray();
            IndexResult indexed = await IndexAsync(changed, ct);

            // A shorter file: its old last positions would otherwise stay and keep coming back in searches
            string[] gone = before.Keys.Except(chunks.Select(c => c.Key)).ToArray();
            await store.DeleteAsync(gone, ct);

            results.Add(new SyncResult(source, chunks.Count, indexed.Indexed, chunks.Count - changed.Length, gone.Length,
                indexed.InputTokens, stopwatch.ElapsedMilliseconds));
        }

        // A file that left the corpus: all its chunks go
        foreach ((string source, Dictionary<string, string> keys) in stored.Where(s => !chunksBySource.ContainsKey(s.Key)))
        {
            await store.DeleteAsync(keys.Keys.ToArray(), ct);
            results.Add(new SyncResult(source, 0, 0, 0, keys.Count, 0, 0));
        }

        logger.LogInformation("Synced {Chunker}: {Embedded} embedded, {Unchanged} unchanged, {Deleted} deleted",
            chunker, results.Sum(r => r.Embedded), results.Sum(r => r.Unchanged), results.Sum(r => r.Deleted));
        return results.OrderBy(r => r.Source, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int top = 5, string? chunker = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidRequestException("Query is required.");
        if (top is < 1 or > MaxTop)
            throw new InvalidRequestException($"top must be between 1 and {MaxTop}.");
        // An unknown name would just find nothing; a typo should say so
        if (chunker is not null && !ChunkerNames.All.Contains(chunker))
            throw new InvalidRequestException($"chunker must be one of: {string.Join(", ", ChunkerNames.All)}.");

        var stopwatch = Stopwatch.StartNew();
        ReadOnlyMemory<float> vector = await embeddings.GenerateVectorAsync(useTaskPrefixes ? QueryPrefix + query : query, cancellationToken: ct);
        long embedMs = stopwatch.ElapsedMilliseconds;

        // No EnsureCreated here: a wrong VectorStore:Collection should fail, not return 0 hits from a new empty table
        IReadOnlyList<SearchHit> hits = await store.SearchAsync(vector, top, chunker, ct);

        logger.LogInformation("Search returned {Hits} hits: embedding {EmbedMs} ms, search {SearchMs} ms",
            hits.Count, embedMs, stopwatch.ElapsedMilliseconds - embedMs);
        return hits;
    }

    private static void Validate(IReadOnlyList<TextChunk> chunks)
    {
        // Ollama embeds "" as a normal-looking vector (from 2 special tokens), so an empty chunk would come back as a contentless hit
        TextChunk? empty = chunks.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.Text));
        if (empty is not null)
            throw new ArgumentException($"Chunk '{empty.Key}' has no text.", nameof(chunks));

        // The store would let the last one silently replace the others, and the count returned would be wrong
        string[] duplicates = chunks.GroupBy(c => c.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
            throw new ArgumentException($"Duplicate chunk keys: {string.Join(", ", duplicates)}.", nameof(chunks));
    }
}
