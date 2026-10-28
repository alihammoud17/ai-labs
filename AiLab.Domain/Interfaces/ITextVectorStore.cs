using AiLab.Domain.DTOs;
using AiLab.Domain.Outputs;

namespace AiLab.Domain.Interfaces;

/// <summary>Stores text chunks with their embeddings and finds the closest ones to a query embedding.</summary>
public interface ITextVectorStore
{
    /// <summary>Creates the collection if it's missing (in Postgres: the table and its HNSW index).</summary>
    Task EnsureCreatedAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces chunks by key. <c>chunks[i]</c> goes with <c>embeddings[i]</c>, so both lists must have the same count.
    /// </summary>
    Task UpsertAsync(IReadOnlyList<TextChunk> chunks, IReadOnlyList<ReadOnlyMemory<float>> embeddings, CancellationToken ct = default);

    /// <summary>
    /// The <paramref name="top"/> chunks closest to <paramref name="queryEmbedding"/>, closest first.
    /// <see cref="SearchHit.Score"/> is cosine distance: 0 = identical, lower = closer.
    /// With <paramref name="chunker"/>, only chunks from that chunker are searched; null searches all of them.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int top, string? chunker = null, CancellationToken ct = default);
}
