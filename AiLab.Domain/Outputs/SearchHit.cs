using AiLab.Domain.DTOs;

namespace AiLab.Domain.Outputs;

// Score is cosine distance: 0 = identical, lower = closer
public sealed record SearchHit(TextChunk Chunk, double Score);
