using AiLab.Domain.DTOs;
using AiLab.Domain.Outputs;

namespace AiLab.Domain.Interfaces;

public interface IVectorSearchService
{
    /// <summary>
    /// Embeds the chunks and stores them, creating the collection if needed. Returns the number stored and the provider's
    /// token count. Empty texts and duplicate keys are refused.
    /// </summary>
    Task<IndexResult> IndexAsync(IReadOnlyList<TextChunk> chunks, CancellationToken ct = default);

    /// <summary>
    /// Brings one <paramref name="chunker"/>'s stored chunks in line with <paramref name="chunksBySource"/>, which holds every
    /// source of the corpus (a source with no chunks too). Embeds only chunks whose key is new or whose ContentHash changed,
    /// deletes keys a source no longer has, and deletes every chunk of a stored source that isn't in the dictionary.
    /// <paramref name="force"/> embeds everything again: the hash covers the text only, so a new embedding model or
    /// task-prefix setting needs it. One result per source, removed sources included, ordered by source.
    /// </summary>
    Task<IReadOnlyList<SyncResult>> SyncAsync(string chunker, IReadOnlyDictionary<string, IReadOnlyList<TextChunk>> chunksBySource,
        bool force = false, CancellationToken ct = default);

    /// <summary>
    /// The <paramref name="top"/> (1–20) chunks closest to the query, closest first, optionally only from one
    /// <paramref name="chunker"/> (one of <c>ChunkerNames.All</c>). Fails if nothing was indexed yet.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int top = 5, string? chunker = null, CancellationToken ct = default);
}
