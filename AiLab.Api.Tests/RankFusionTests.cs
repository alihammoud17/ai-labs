using AiLab.Application.Search;
using AiLab.Domain.DTOs;
using AiLab.Domain.Outputs;

namespace AiLab.Api.Tests;

// Offline: reciprocal rank fusion of a vector and a keyword ranking. Merge returns hits, not RRF scores, so the tests
// check the order (and that each hit keeps its cosine distance).
public class RankFusionTests
{
    private static SearchHit Hit(string source, int position, double distance, int start = 1, int end = 5) =>
        new(new TextChunk("heading", source, position, $"{source} {position}", null, start, end), distance);

    private static string[] Keys(IReadOnlyList<SearchHit> hits) => [.. hits.Select(h => h.Chunk.Key)];

    // The JWT question from step 4.3, with the real distances
    private static readonly SearchHit Readme = Hit("README.md", 6, 0.3844, 65, 76);
    private static readonly SearchHit Decisions29 = Hit("docs/decisions.md", 7, 0.3663, 29, 31);
    private static readonly SearchHit Decisions49 = Hit("docs/decisions.md", 12, 0.5369, 49, 62);

    [Fact]
    public void Worked_example_puts_README_before_decisions()
    {
        // Vector: decisions.md:29-31 first, README second. Keyword: README first, decisions.md:29-31 third.
        // README 1/62 + 1/61 = 0.03252, decisions.md:29-31 1/61 + 1/63 = 0.03227, decisions.md:49-62 1/62 = 0.01613
        IReadOnlyList<SearchHit> merged = RankFusion.Merge([Decisions29, Readme], [Readme, Decisions49, Decisions29], top: 5);

        Assert.Equal(Keys([Readme, Decisions29, Decisions49]), Keys(merged));
    }

    [Fact]
    public void A_chunk_in_both_lists_beats_one_in_a_single_list()
    {
        SearchHit both = Hit("both.md", 1, 0.40);
        SearchHit vectorOnly = Hit("vector.md", 1, 0.10);

        // vectorOnly is 1st in vector (1/61 = 0.0164); both is 2nd in each (2/62 = 0.0323)
        IReadOnlyList<SearchHit> merged = RankFusion.Merge([vectorOnly, both], [Hit("other.md", 1, 0.9), both], top: 5);

        Assert.Equal(both.Chunk.Key, merged[0].Chunk.Key);
    }

    // With k = 0 the scores are plain 1/rank, so exact values are easy to set against each other
    [Fact]
    public void A_chunk_in_only_one_list_keeps_its_1_over_k_plus_rank()
    {
        SearchHit single = Hit("single.md", 1, 0.30);  // 1st in vector only: 1/1 = 1.0
        SearchHit both23 = Hit("both23.md", 1, 0.10);  // 2nd and 3rd: 1/2 + 1/3 = 0.833
        SearchHit both22 = Hit("both22.md", 1, 0.20);  // 2nd and 2nd: 1/2 + 1/2 = 1.0, a tie with single

        IReadOnlyList<SearchHit> lower = RankFusion.Merge([single, both23], [Hit("x.md", 1, 0.9), Hit("y.md", 1, 0.9), both23], top: 5, k: 0);
        Assert.Equal(single.Chunk.Key, lower[0].Chunk.Key);

        // Equal at 1.0: the tie goes to the smaller distance (0.20 < 0.30), so the single list's score was exactly 1/(0+1)
        IReadOnlyList<SearchHit> equal = RankFusion.Merge([single, both22], [Hit("x.md", 1, 0.9), both22], top: 5, k: 0);
        Assert.Equal(Keys([both22, single]), Keys(equal).Take(2));
    }

    [Fact]
    public void Ties_go_to_the_smaller_distance_then_the_key()
    {
        // 3rd in vector only and 3rd in keyword only: both exactly 1/63
        SearchHit vectorThird = Hit("a-vector.md", 1, 0.45);
        SearchHit keywordThird = Hit("z-keyword.md", 1, 0.35);
        SearchHit[] vector = [Hit("v1.md", 1, 0.1), Hit("v2.md", 1, 0.2), vectorThird];
        SearchHit[] keyword = [Hit("k1.md", 1, 0.1), Hit("k2.md", 1, 0.2), keywordThird];

        IReadOnlyList<SearchHit> merged = RankFusion.Merge(vector, keyword, top: 6);

        // The closer chunk first, though its key sorts later
        Assert.Equal(Keys([keywordThird, vectorThird]), Keys(merged).Skip(4));

        // Same distance too: the key decides
        SearchHit[] keywordSameDistance = [Hit("k1.md", 1, 0.1), Hit("k2.md", 1, 0.2), keywordThird with { Score = 0.45 }];
        Assert.Equal(Keys([vectorThird, keywordThird]), Keys(RankFusion.Merge(vector, keywordSameDistance, top: 6)).Skip(4));
    }

    [Fact]
    public void Hits_keep_their_cosine_distance()
    {
        IReadOnlyList<SearchHit> merged = RankFusion.Merge([Decisions29, Readme], [Readme, Decisions49, Decisions29], top: 5);

        Assert.Equal([0.3844, 0.3663, 0.5369], merged.Select(h => h.Score));
    }

    [Fact]
    public void Top_is_respected()
    {
        IReadOnlyList<SearchHit> merged = RankFusion.Merge([Decisions29, Readme], [Readme, Decisions49, Decisions29], top: 2);

        Assert.Equal(Keys([Readme, Decisions29]), Keys(merged));
    }

    [Fact]
    public void Empty_lists()
    {
        Assert.Empty(RankFusion.Merge([], [], top: 5));

        // One empty list: the other comes back in its own order
        Assert.Equal(Keys([Decisions29, Readme]), Keys(RankFusion.Merge([Decisions29, Readme], [], top: 5)));
        Assert.Equal(Keys([Readme, Decisions49]), Keys(RankFusion.Merge([], [Readme, Decisions49], top: 5)));
    }

    [Fact]
    public void Bad_top_or_k_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RankFusion.Merge([Readme], [], top: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RankFusion.Merge([Readme], [], top: 5, k: -1));
    }
}
