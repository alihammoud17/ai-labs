using AiLab.Domain.DTOs;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace AiLab.Infrastructure.VectorStore;

// The collection comes from AddVectorStore, in memory or Postgres; this class doesn't know which.
// Vectors are never logged: 768 floats per line is noise.
public sealed class TextVectorStore(VectorStoreCollection<string, TextChunkRecord> collection, ILogger<TextVectorStore> logger)
    : ITextVectorStore
{
    public Task EnsureCreatedAsync(CancellationToken ct = default) => collection.EnsureCollectionExistsAsync(ct);

    public async Task UpsertAsync(IReadOnlyList<TextChunk> chunks, IReadOnlyList<ReadOnlyMemory<float>> embeddings, CancellationToken ct = default)
    {
        if (chunks.Count != embeddings.Count)
            throw new ArgumentException($"Got {chunks.Count} chunks but {embeddings.Count} embeddings; chunks[i] goes with embeddings[i].", nameof(embeddings));
        for (int i = 0; i < embeddings.Count; i++)
            CheckDimensions(embeddings[i], $"the embedding for chunk '{chunks[i].Key}'", nameof(embeddings));

        TextChunkRecord[] records = chunks.Select((chunk, i) => new TextChunkRecord
        {
            Key = chunk.Key,
            Text = chunk.Text,
            Source = chunk.Source,
            Chunker = chunk.Chunker,
            Position = chunk.Position,
            Heading = chunk.Heading,
            StartLine = chunk.StartLine,
            EndLine = chunk.EndLine,
            ContentHash = chunk.ContentHash,
            Embedding = embeddings[i],
        }).ToArray();

        // One batched call: Postgres sends one batch instead of a round trip per record
        await collection.UpsertAsync(records, ct);
        logger.LogDebug("Upserted {Count} chunk(s) into {Collection}", records.Length, collection.Name);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int top, string? chunker = null, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
        CheckDimensions(queryEmbedding, "the query embedding", nameof(queryEmbedding));

        // Both stores turn the filter into their own query (Postgres: a WHERE on the indexed Chunker column)
        VectorSearchOptions<TextChunkRecord>? options = chunker is null ? null : new() { Filter = r => r.Chunker == chunker };

        var hits = new List<SearchHit>(top);
        try
        {
            await foreach (VectorSearchResult<TextChunkRecord> result in collection.SearchAsync(queryEmbedding, top, options, ct))
            {
                // A missing score is a connector problem; 0 would read as "identical".
                // NaN comes from a zero vector (cosine has no direction to compare) and would break sorting and thresholds.
                double score = result.Score ?? throw new InvalidOperationException($"The vector store returned no score for chunk '{result.Record.Key}'.");
                if (double.IsNaN(score))
                    throw new InvalidOperationException($"The vector store returned NaN for chunk '{result.Record.Key}'; is the query or the stored embedding all zeros?");
                hits.Add(new SearchHit(ToChunk(result.Record), score));
            }
        }
        // Both stores fail on a missing collection, Postgres only with "Call to vector store failed."; the existence check runs only then
        catch (VectorStoreException)
        {
            if (await collection.CollectionExistsAsync(ct))
                throw;
            throw new VectorCollectionMissingException(collection.Name);
        }

        logger.LogDebug("Search in {Collection} (top {Top}, chunker {Chunker}) returned {Count} hit(s)", collection.Name, top, chunker ?? "any", hits.Count);
        return hits;
    }

    // The key and hash are rebuilt from the fields, so they always match what Upsert stored
    private static TextChunk ToChunk(TextChunkRecord record) =>
        new(record.Chunker, record.Source, record.Position, record.Text, record.Heading, record.StartLine, record.EndLine);

    // Checked here because the stores don't: in memory a wrong-size row is accepted and then breaks every search,
    // and Postgres only says "Call to vector store failed."
    private static void CheckDimensions(ReadOnlyMemory<float> embedding, string what, string paramName)
    {
        if (embedding.Length != TextChunkRecord.Dimensions)
            throw new ArgumentException(
                $"{what} has {embedding.Length} dimensions, but the store expects {TextChunkRecord.Dimensions}. " +
                "Check Embedding:Model; another model needs TextChunkRecord.Dimensions changed and the collection recreated.", paramName);
    }
}
