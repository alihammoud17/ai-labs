using System.ComponentModel;

namespace AiLab.Domain.Responses;

// POST /dev/ingest (AiLab.Rag)
[Description("What was chunked and stored: one row per file and chunker, then one total per chunker.")]
public sealed record IngestResponse(
    [property: Description("fixed, heading or both.")]
    string Chunker,
    [property: Description("The corpus folder that was read.")]
    string Corpus,
    [property: Description("Which vector store was written: postgres or inmemory.")]
    string Provider,
    [property: Description("Time for the whole ingest (chunking, embedding and storing), in milliseconds.")]
    long ElapsedMs,
    IReadOnlyList<IngestStats> Files,
    IReadOnlyList<IngestStats> Totals);

// Estimated tokens = characters / 4. EmbeddingTokens is what the embedding provider counted, to compare with the estimate.
public sealed record IngestStats(
    string Chunker,
    [property: Description("The file, relative to the corpus folder; null in a total.")]
    string? Source,
    int Files,
    int Chunks,
    [property: Description("Smallest chunk, in estimated tokens (characters / 4).")]
    int MinTokens,
    [property: Description("Average chunk, in estimated tokens, rounded.")]
    int AvgTokens,
    [property: Description("Largest chunk, in estimated tokens.")]
    int MaxTokens,
    [property: Description("Tokens the embedding provider counted for these chunks; null if it reported none.")]
    long? EmbeddingTokens,
    [property: Description("Time for embedding and storing these chunks, in milliseconds.")]
    long ElapsedMs);

// GET /dev/chunks (AiLab.Rag)
[Description("The stored chunks in reading order: by source, chunker, then position.")]
public sealed record ChunksResponse(
    string? Chunker,
    string? Source,
    int Count,
    IReadOnlyList<ChunkResponse> Chunks);

public sealed record ChunkResponse(
    string Key,
    string Chunker,
    string Source,
    int Position,
    string? Heading,
    int StartLine,
    int EndLine,
    [property: Description("Characters / 4.")]
    int EstimatedTokens,
    [property: Description("SHA-256 of the text, 64 hex characters.")]
    string ContentHash,
    string Text);
