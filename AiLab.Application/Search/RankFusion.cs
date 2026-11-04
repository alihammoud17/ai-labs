using AiLab.Domain.Outputs;

namespace AiLab.Application.Search;

// Reciprocal rank fusion (RRF): merges two rankings by position, not by score, because the two scores don't compare
// (cosine distance: lower is better, 0..2; ts_rank_cd: higher is better, no fixed scale).
// Each chunk gets 1 / (k + rank) from every list it's in, 1-based; the sums decide the order. A chunk both searches found
// beats one only a single search found, unless the single one ranked much higher.
// The returned hits keep their own Score, the cosine distance (keyword hits carry it too), so /ask's distance gate
// still works. The RRF score only decides the order and isn't returned.
public static class RankFusion
{
    public const int DefaultK = 60;

    public static IReadOnlyList<SearchHit> Merge(IReadOnlyList<SearchHit> vector, IReadOnlyList<SearchHit> keyword, int top, int k = DefaultK)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(k);

        var scores = new Dictionary<string, (double Score, SearchHit Hit)>();

        void AddRanks(IReadOnlyList<SearchHit> hits)
        {
            for (var i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                var key = hit.Chunk.Key;
                var rankScore = 1.0 / (k + i + 1);

                // In both lists: one hit kept, the scores added (the distance is the same in both)
                if (scores.TryGetValue(key, out var current))
                    scores[key] = (current.Score + rankScore, current.Hit);
                else
                    scores.Add(key, (rankScore, hit));
            }
        }

        AddRanks(vector);
        AddRanks(keyword);

        // Ties are real: 3rd in vector only and 3rd in keyword only both score exactly 1/(k+3).
        // Then the closer chunk wins, then the key, so the order is the same on every run.
        return scores
            .OrderByDescending(entry => entry.Value.Score)
            .ThenBy(entry => entry.Value.Hit.Score)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(top)
            .Select(entry => entry.Value.Hit)
            .ToList();
    }
}
