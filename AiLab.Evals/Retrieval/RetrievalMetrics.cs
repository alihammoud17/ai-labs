using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;

namespace AiLab.Evals.Retrieval;

public static class RetrievalMetrics
{
    public static int? FirstHitRank(AskSetRow row, IReadOnlyList<RetrievedChunk> ranked)
    {
        var gold = row.Gold;
        if (gold.Count == 0)
        {
            return null;
        }

        var hitGold = new bool[gold.Count];
        int hitCount = 0;
        for (int i = 0; i < ranked.Count; i++)
        {
            for (int j = 0; j < gold.Count; j++)
            {
                if (!hitGold[j] && RecallAtKEvaluator.IsHit(gold[j], ranked[i]))
                {
                    hitGold[j] = true;
                    hitCount++;
                }
            }

            if (row.GoldMode == "all" ? hitCount == gold.Count : hitCount > 0)
                return i + 1;
        }

        return null;
    }

    // Every rank passed in belongs to an answerable question (the caller leaves out rows without gold),
    // so a null rank is a miss: it counts in the total and adds nothing.
    public static double HitRate(IEnumerable<int?> firstHitRanks, int k)
    {
        int hitCount = 0;
        int totalCount = 0;

        foreach (var rank in firstHitRanks)
        {
            totalCount++;
            if (rank.HasValue && rank.Value <= k)
            {
                hitCount++;
            }
        }

        return totalCount > 0 ? (double)hitCount / totalCount : 0.0;
    }

    public static double Mrr(IEnumerable<int?> firstHitRanks)
    {
        double reciprocalRankSum = 0.0;
        int totalCount = 0;

        foreach (var rank in firstHitRanks)
        {
            totalCount++;
            if (rank.HasValue)
            {
                reciprocalRankSum += 1.0 / rank.Value;
            }
        }

        return totalCount > 0 ? reciprocalRankSum / totalCount : 0.0;
    }
}
