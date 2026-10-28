namespace AiLab.Domain.Outputs;

// One source after IVectorSearchService.SyncAsync. Chunks = what the chunker made now (0 for a file that left the corpus);
// Embedded + Unchanged = Chunks; Deleted = stored keys the source no longer has. InputTokens as in IndexResult.
public sealed record SyncResult(string Source, int Chunks, int Embedded, int Unchanged, int Deleted, long? InputTokens, long ElapsedMs);
