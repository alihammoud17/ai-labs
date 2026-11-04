using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using AiLab.Evals.Retrieval;

namespace AiLab.Evals;

// Offline: the retrieval report's totals and the comparison with Tuesday's ranks.
public class RetrievalReportTests
{
    private static RetrievedChunk Chunk(string file, int start, int end) => new(file, start, end);
    private static readonly RetrievedChunk Other = Chunk("docs/decisions.md", 1, 5);

    private static RetrievalResult Result(string id, params RetrievedChunk[] ranked)
    {
        AskSetRow row = AskSetData.Row(id);
        return new RetrievalResult(row, RetrievalMetrics.FirstHitRank(row, ranked), ranked);
    }

    // B6 needs docs/architecture.md:46 and README.md:38. Tuesday counted the first of either.
    [Fact]
    public void An_all_row_is_compared_with_Tuesdays_any_source_rank()
    {
        var b6 = Result("B6", Chunk("README.md", 34, 42), Other, Other, Chunk("docs/architecture.md", 40, 46));
        Assert.Equal(4, b6.Rank);

        Assert.True(RetrievalReport.SameAsTuesday(b6 with { Tuesday = 1, HasTuesday = true }));
        Assert.False(RetrievalReport.SameAsTuesday(b6 with { Tuesday = 4, HasTuesday = true }));
    }

    [Fact]
    public void A_rank_beyond_5_matches_Tuesdays_not_in_the_top_5()
    {
        var a3 = Result("A3", Other, Other, Other, Other, Other, Other, Chunk("docs/contracts.md", 43, 65));
        Assert.Equal(7, a3.Rank);

        Assert.True(RetrievalReport.SameAsTuesday(a3 with { Tuesday = null, HasTuesday = true }));
        Assert.False(RetrievalReport.SameAsTuesday(a3 with { Tuesday = 1, HasTuesday = true }));
    }

    [Fact]
    public void The_non_English_row_is_kept_out_of_the_totals()
    {
        var a3 = Result("A3", Chunk("docs/contracts.md", 43, 65));
        var h5 = Result("H5", Other);

        string report = RetrievalReport.Build("vector", "heading", 10, new DateOnly(2026, 11, 4), [a3, h5]);

        Assert.Contains("| English answerable (1) | 1.000 | 1.000 | 1.000 | 1.000 | 1.000 |", report);
        Assert.Contains("| H5 (Non-English, apart) | 0.000 | 0.000 | 0.000 | 0.000 | 0.000 |", report);
        Assert.Contains("| A3 | Single fact | any | 1 | **docs/contracts.md:43-65** |", report);
        Assert.Contains("| H5 | Non-English | any | miss |", report);
        Assert.DoesNotContain("Tuesday", report);
    }
}
