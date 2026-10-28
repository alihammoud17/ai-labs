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

// Estimated tokens = characters / 4, over every chunk made. EmbeddingTokens is what the embedding provider counted for the
// chunks it embedded, so on a run where nothing changed it's 0.
public sealed record IngestStats(
    string Chunker,
    [property: Description("The file, relative to the corpus folder; null in a total.")]
    string? Source,
    [property: Description("Files read; 0 in a row for a file that left the corpus (its chunks were deleted).")]
    int Files,
    [property: Description("Chunks the chunker made now.")]
    int Chunks,
    [property: Description("Chunks embedded and stored: new keys or changed text (all of them with force=true).")]
    int Embedded,
    [property: Description("Chunks whose stored text was the same, so not embedded again.")]
    int Unchanged,
    [property: Description("Stored chunks removed: positions a file no longer has, or a file that left the corpus.")]
    int Deleted,
    [property: Description("Smallest chunk, in estimated tokens (characters / 4).")]
    int MinTokens,
    [property: Description("Average chunk, in estimated tokens, rounded.")]
    int AvgTokens,
    [property: Description("Largest chunk, in estimated tokens.")]
    int MaxTokens,
    [property: Description("Tokens the embedding provider counted for the embedded chunks; null if it reported none.")]
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
