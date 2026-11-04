namespace AiLab.Domain.Constants;

// How GET /search and POST /ask (Ask:SearchMode) find chunks
public static class SearchModes
{
    // Closest embeddings (pgvector, cosine distance): the behaviour before hybrid search
    public const string Vector = "vector";

    // Postgres full-text search: chunks sharing the most query words (OR), ranked by ts_rank_cd
    public const string Keyword = "keyword";

    // Both, a wider window each, merged by reciprocal rank fusion (RankFusion)
    public const string Hybrid = "hybrid";

    public static readonly IReadOnlyList<string> All = [Vector, Keyword, Hybrid];
}
