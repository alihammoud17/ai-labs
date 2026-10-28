using AiLab.Domain.DTOs;

namespace AiLab.Application.Chunking;

public interface IChunker
{
    string Name { get; }                       // "fixed" | "heading"
    IReadOnlyList<TextChunk> Chunk(string source, string markdown);
    // About 4 characters per token for English; compare with the real counts VectorSearchService logs
    static int EstimateTokens(string s) => (s.Length + 3) / 4;
}
