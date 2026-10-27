using AiLab.Domain.DTOs;
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

        TextChunkRecord[] records = chunks.Select((chunk, i) => new TextChunkRecord
        {
            Key = chunk.Key,
            Text = chunk.Text,
            Source = chunk.Source,
            Embedding = embeddings[i],
        }).ToArray();

        // One batched call: Postgres sends one batch instead of a round trip per record
        await collection.UpsertAsync(records, ct);
        logger.LogDebug("Upserted {Count} chunk(s) into {Collection}", records.Length, collection.Name);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int top, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        var hits = new List<SearchHit>(top);
        await foreach (VectorSearchResult<TextChunkRecord> result in collection.SearchAsync(queryEmbedding, top, cancellationToken: ct))
        {
            // A missing score is a connector problem; 0 would read as "identical"
            double score = result.Score ?? throw new InvalidOperationException($"The vector store returned no score for chunk '{result.Record.Key}'.");
            hits.Add(new SearchHit(result.Record.Key, result.Record.Text, result.Record.Source, score));
        }

        logger.LogDebug("Search in {Collection} (top {Top}) returned {Count} hit(s)", collection.Name, top, hits.Count);
        return hits;
    }
}
