using System.Diagnostics;
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

    public async Task<int> IndexAsync(IReadOnlyList<TextChunk> chunks, CancellationToken ct = default)
    {
        if (chunks.Count == 0)
            return 0;

        // Ollama embeds "" as a normal-looking vector (from 2 special tokens), so an empty chunk would come back as a contentless hit
        TextChunk? empty = chunks.FirstOrDefault(c => string.IsNullOrWhiteSpace(c.Text));
        if (empty is not null)
            throw new ArgumentException($"Chunk '{empty.Key}' has no text.", nameof(chunks));

        // The store would let the last one silently replace the others, and the count returned would be wrong
        string[] duplicates = chunks.GroupBy(c => c.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
            throw new ArgumentException($"Duplicate chunk keys: {string.Join(", ", duplicates)}.", nameof(chunks));

        // Indexing is a write, so it may create the collection; search never does
        await store.EnsureCreatedAsync(ct);

        var stopwatch = Stopwatch.StartNew();
        var vectors = new List<ReadOnlyMemory<float>>(chunks.Count);
        long totalTokens = 0;
        foreach (TextChunk[] batch in chunks.Chunk(EmbeddingBatchSize))
        {
            var generated = await embeddings.GenerateAsync(batch.Select(c => useTaskPrefixes ? DocumentPrefix + c.Text : c.Text), cancellationToken: ct);
            if (generated.Count != batch.Length)
                throw new InvalidOperationException($"Expected {batch.Length} embeddings, got {generated.Count}.");

            vectors.AddRange(generated.Select(e => e.Vector));
            totalTokens += generated.Usage?.InputTokenCount ?? 0;
            // Tokens per chunk: Ollama silently cuts inputs longer than the model's context, so watch for chunks near the limit
            logger.LogDebug("Embedded {Count} chunks ({Tokens} tokens)", batch.Length, generated.Usage?.InputTokenCount);
        }
        long embedMs = stopwatch.ElapsedMilliseconds;

        await store.UpsertAsync(chunks, vectors, ct);

        logger.LogInformation("Indexed {Count} chunks ({Tokens} tokens): embedding {EmbedMs} ms, upsert {UpsertMs} ms",
            chunks.Count, totalTokens, embedMs, stopwatch.ElapsedMilliseconds - embedMs);
        return chunks.Count;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int top = 5, string? chunker = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidRequestException("Query is required.");
        if (top is < 1 or > MaxTop)
            throw new InvalidRequestException($"top must be between 1 and {MaxTop}.");

        var stopwatch = Stopwatch.StartNew();
        ReadOnlyMemory<float> vector = await embeddings.GenerateVectorAsync(useTaskPrefixes ? QueryPrefix + query : query, cancellationToken: ct);
        long embedMs = stopwatch.ElapsedMilliseconds;

        // No EnsureCreated here: a wrong VectorStore:Collection should fail, not return 0 hits from a new empty table
        IReadOnlyList<SearchHit> hits = await store.SearchAsync(vector, top, chunker, ct);

        logger.LogInformation("Search returned {Hits} hits: embedding {EmbedMs} ms, search {SearchMs} ms",
            hits.Count, embedMs, stopwatch.ElapsedMilliseconds - embedMs);
        return hits;
    }
}
