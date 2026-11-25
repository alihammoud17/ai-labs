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

    // The header named "Heading chunker" whatever chunker ran (2026-11-05: comparison-fixed.md said heading)
    [Theory]
    [InlineData("heading")]
    [InlineData("fixed")]
    public void The_comparison_header_names_the_chunker_and_top_that_ran(string chunker)
    {
        AskSetRow a3 = AskSetData.Row("A3");
        var ranks = new Dictionary<string, Dictionary<string, int?>> { ["A3"] = new() { ["vector"] = 1 } };

        string report = RetrievalReport.Comparison([a3], ranks, ["vector"], chunker, 10, new DateOnly(2026, 11, 5));

        Assert.Contains($"Chunker `{chunker}`, top 10.", report);
        Assert.DoesNotContain("Heading chunker", report);
    }

    // Week 8: the KB report keeps an unlocated passage at its rank as a non-hit and lists it; a later hit keeps its rank
    [Fact]
    public void An_unlocated_passage_keeps_its_rank_as_a_non_hit_and_is_listed()
    {
        AskSetRow a3 = AskSetData.Row("A3");
        RetrievedChunk[] ranked = [UnlocatedPassage.NotLocated("docs/contracts.md"), Chunk("docs/contracts.md", 1, 63)];
        var result = new RetrievalResult(a3, RetrievalMetrics.FirstHitRank(a3, ranked), ranked);
        Assert.Equal(2, result.Rank);

        string report = RetrievalReport.Build("kb", "n/a", 10, new DateOnly(2026, 11, 25), [result],
            title: "KB on aws-emulator (measured, stand-in)", source: "KB source line.",
            unlocated: [new UnlocatedPassage("A3", 1, "docs/contracts.md", "passage not found in docs/contracts.md")],
            latenciesMs: [10, 20, 30]);

        Assert.StartsWith("# Retrieval: KB on aws-emulator (measured, stand-in), 2026-11-25", report);
        Assert.Contains("KB source line.", report);
        Assert.DoesNotContain("GET /search", report);
        Assert.Contains("| A3 | Single fact | any | 2 | docs/contracts.md (not located), **docs/contracts.md:1-63** |", report);
        Assert.Contains("## Not located", report);
        Assert.Contains("| A3 | 1 | docs/contracts.md | passage not found in docs/contracts.md |", report);
        Assert.Contains("Latency per query (client side, same machine): median 20 ms", report);
    }

    [Fact]
    public void A_kb_report_with_every_passage_located_says_so()
    {
        var a3 = Result("A3", Chunk("docs/contracts.md", 1, 63));

        string report = RetrievalReport.Build("kb", "n/a", 10, new DateOnly(2026, 11, 25), [a3], unlocated: []);

        Assert.Contains("None: every passage was placed in its corpus file", report);
    }

    [Fact]
    public void The_search_report_has_no_kb_parts()
    {
        string report = RetrievalReport.Build("vector", "heading", 10, new DateOnly(2026, 11, 25), [Result("A3", Chunk("docs/contracts.md", 43, 65))]);

        Assert.StartsWith("# Retrieval: vector, 2026-11-25", report);
        Assert.Contains("`GET /search?top=10&chunker=heading&mode=vector`", report);
        Assert.DoesNotContain("Not located", report);
        Assert.DoesNotContain("Latency", report);
    }
}
