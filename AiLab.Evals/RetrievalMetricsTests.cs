using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using AiLab.Evals.Retrieval;

namespace AiLab.Evals;

// Offline: first-hit rank, hit rate@k and MRR from gold lines and ranked chunks.
public class RetrievalMetricsTests
{
    private static RetrievedChunk Chunk(string file, int start, int end) => new(file, start, end);

    private static readonly RetrievedChunk Other = Chunk("docs/decisions.md", 1, 5);

    // A3: one gold source, docs/contracts.md line 45 (goldMode any)
    private static readonly AskSetRow A3 = AskSetData.Row("A3");
    private static readonly RetrievedChunk A3Gold = Chunk("docs/contracts.md", 43, 65);

    // B6: needs both docs/architecture.md line 46 and README.md line 38 (goldMode all)
    private static readonly AskSetRow B6 = AskSetData.Row("B6");
    private static readonly RetrievedChunk B6Architecture = Chunk("docs/architecture.md", 40, 46);
    private static readonly RetrievedChunk B6Readme = Chunk("README.md", 34, 42);

    [Fact]
    public void First_hit_at_rank_1() =>
        Assert.Equal(1, RetrievalMetrics.FirstHitRank(A3, [A3Gold, Other, Other]));

    [Fact]
    public void First_hit_at_rank_3() =>
        Assert.Equal(3, RetrievalMetrics.FirstHitRank(A3, [Other, Other, A3Gold, Other]));

    [Fact]
    public void No_hit_is_null() =>
        Assert.Null(RetrievalMetrics.FirstHitRank(A3, [Other, Chunk("docs/contracts.md", 9, 15)]));

    [Fact]
    public void A_row_without_gold_is_null()
    {
        AskSetRow noGold = AskSetData.Row("D2");
        Assert.Empty(noGold.Gold);

        Assert.Null(RetrievalMetrics.FirstHitRank(noGold, [A3Gold, B6Readme]));
    }

    [Fact]
    public void Any_mode_stops_at_the_first_of_several_sources()
    {
        AskSetRow twoSourcesAny = B6 with { GoldMode = "any" };

        Assert.Equal(2, RetrievalMetrics.FirstHitRank(twoSourcesAny, [Other, B6Readme, B6Architecture]));
    }

    [Fact]
    public void All_mode_is_the_rank_where_the_last_needed_source_first_appears() =>
        Assert.Equal(4, RetrievalMetrics.FirstHitRank(B6, [B6Architecture, Other, B6Architecture, B6Readme, Other]));

    [Fact]
    public void All_mode_with_one_source_never_found_is_null() =>
        Assert.Null(RetrievalMetrics.FirstHitRank(B6, [B6Architecture, Other, Other, Other, Other]));

    [Fact]
    public void All_mode_counts_one_chunk_that_covers_both_sources()
    {
        AskSetRow sameFile = B6 with
        {
            Gold = [new GoldSource("README.md", [38]), new GoldSource("README.md", [40])],
        };

        Assert.Equal(2, RetrievalMetrics.FirstHitRank(sameFile, [Other, B6Readme]));
    }

    [Theory]
    [InlineData(1, 0.25)]
    [InlineData(3, 0.50)]
    [InlineData(5, 0.75)]
    public void Hit_rate_counts_nulls_as_misses(int k, double expected) =>
        Assert.Equal(expected, RetrievalMetrics.HitRate([1, 3, null, 5], k), 6);

    [Fact]
    public void Mrr_counts_a_miss_as_zero() =>
        Assert.Equal((1 + 0.5) / 4, RetrievalMetrics.Mrr([1, 2, null, null]), 6);

    [Fact]
    public void Empty_input_is_zero()
    {
        Assert.Equal(0, RetrievalMetrics.HitRate([], 5));
        Assert.Equal(0, RetrievalMetrics.Mrr([]));
    }

    // Tuesday's vector baseline over the 24 English answerable rows: 15 at rank 1, 4 at 2, 3 at 3, 2 at 4
    private static readonly int?[] TuesdayRanks =
        [.. Enumerable.Repeat<int?>(1, 15), .. Enumerable.Repeat<int?>(2, 4), .. Enumerable.Repeat<int?>(3, 3), .. Enumerable.Repeat<int?>(4, 2)];

    [Fact]
    public void Tuesdays_ranks_give_the_hand_checked_numbers()
    {
        Assert.Equal(0.625, RetrievalMetrics.HitRate(TuesdayRanks, 1), 3);
        Assert.Equal(22.0 / 24, RetrievalMetrics.HitRate(TuesdayRanks, 3), 6);
        Assert.Equal(1.0, RetrievalMetrics.HitRate(TuesdayRanks, 5), 6);
        Assert.Equal(0.771, RetrievalMetrics.Mrr(TuesdayRanks), 3);
    }

    // With H5 (French, missed) added as a 25th row, hit@5 matches Tuesday's Recall@5 of 0.96
    [Fact]
    public void Adding_the_French_miss_drops_hit_at_5_to_0_96() =>
        Assert.Equal(0.96, RetrievalMetrics.HitRate([.. TuesdayRanks, null], 5), 6);
}
