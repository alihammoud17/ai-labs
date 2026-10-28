using System.ComponentModel;

namespace AiLab.Domain.Responses;

// GET /search (AiLab.Rag)
[Description("The chunks closest to the query, closest first.")]
public sealed record SearchResponse(
    string Query,
    int Top,
    [property: Description("Which vector store answered: postgres or inmemory.")]
    string Provider,
    [property: Description("Time for the whole search (embedding the query plus the store's search), in milliseconds.")]
    long ElapsedMs,
    IReadOnlyList<SearchResponseHit> Hits);

// Distance is cosine distance (lower = closer). Similarity = 1 - distance, rounded to 4 decimals,
// only to compare with the /similarity matrix.
public sealed record SearchResponseHit(
    [property: Description("1 for the closest chunk.")]
    int Rank,
    [property: Description("The chunk's stable id, <chunker>:<source>:<position>, for example lines:similarity-sentences.txt:4.")]
    string Key,
    string Text,
    [property: Description("The file the chunk came from.")]
    string Source,
    [property: Description("Cosine distance to the query: lower = closer, 0 = the same text.")]
    double Distance,
    [property: Description("1 − distance, rounded to 4 decimals. Only for comparing with the /similarity matrix.")]
    double Similarity);

// POST /dev/seed (AiLab.Rag)
public sealed record SeedResponse(
    [property: Description("How many chunks were stored (one per non-blank line).")]
    int Indexed,
    [property: Description("The seed file's name.")]
    string Source,
    [property: Description("Which vector store was written: postgres or inmemory.")]
    string Provider,
    [property: Description("Time for embedding and storing, in milliseconds.")]
    long ElapsedMs);
