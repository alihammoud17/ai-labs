using AiLab.Domain.DTOs;
using AiLab.Domain.Outputs;

namespace AiLab.Domain.Interfaces;

public interface IVectorSearchService
{
    /// <summary>
    /// Embeds the chunks and stores them, creating the collection if needed. Returns the number stored.
    /// Empty texts and duplicate keys are refused.
    /// </summary>
    Task<int> IndexAsync(IReadOnlyList<TextChunk> chunks, CancellationToken ct = default);

    /// <summary>The <paramref name="top"/> (1–20) chunks closest to the query, closest first. Fails if nothing was indexed yet.</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int top = 5, CancellationToken ct = default);
}
