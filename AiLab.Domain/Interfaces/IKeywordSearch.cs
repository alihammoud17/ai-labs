using AiLab.Domain.Outputs;

namespace AiLab.Domain.Interfaces;

/// <summary>
/// Finds chunks that share words with the query (full-text search), next to <see cref="ITextVectorStore"/>'s vector search.
/// Registered by the same <c>AddVectorStore</c>, so both search the same collection.
/// </summary>
public interface IKeywordSearch
{
    /// <summary>Creates what keyword search needs on an existing collection (in Postgres: a GIN index on the text). Safe to call again.</summary>
    Task EnsureIndexAsync(CancellationToken ct = default);

    /// <summary>
    /// The <paramref name="top"/> chunks that best match the query's words, best first. Any word counts (OR), not all of them:
    /// one word the docs don't use would otherwise lose the right chunk. Chunks with no query word aren't returned, so there
    /// can be fewer than <paramref name="top"/>, or none.
    /// <see cref="SearchHit.Score"/> is the cosine distance to <paramref name="queryEmbedding"/>, as in vector search, so a gate
    /// on distance works on keyword hits too. The order comes from the word match, not from that distance.
    /// With <paramref name="chunker"/>, only that chunker's chunks are searched; null searches all of them.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, ReadOnlyMemory<float> queryEmbedding, int top, string? chunker = null, CancellationToken ct = default);
}
