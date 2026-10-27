namespace AiLab.Domain.Responses;

// GET /search (AiLab.Rag). Provider says which store answered (inmemory or postgres).
public sealed record SearchResponse(string Query, int Top, string Provider, long ElapsedMs, IReadOnlyList<SearchResponseHit> Hits);

// Distance is cosine distance (lower = closer). Similarity = 1 - distance, rounded to 4 decimals,
// only to compare with the /similarity matrix.
public sealed record SearchResponseHit(int Rank, string Key, string Text, string Source, double Distance, double Similarity);

// POST /dev/seed (AiLab.Rag)
public sealed record SeedResponse(int Indexed, string Source, string Provider, long ElapsedMs);
