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
    /// The <paramref name="top"/> (1–20) chunks closest to the query, closest first, optionally only from one
    /// <paramref name="chunker"/> (one of <c>ChunkerNames.All</c>). Fails if nothing was indexed yet.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int top = 5, string? chunker = null, CancellationToken ct = default);
}
