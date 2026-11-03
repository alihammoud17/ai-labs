using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace AiLab.Evals;

// Offline: Recall@5 and the first-hit rank from gold lines and retrieved line ranges.
public class RecallAtKEvaluatorTests
{
    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "q")];
    private static readonly ChatResponse Answer = new(new ChatMessage(ChatRole.Assistant, "a"));

    private static async Task<(NumericMetric Recall, NumericMetric Rank)> Evaluate(
        IReadOnlyList<GoldSource> gold, string mode, params RetrievedChunk[] chunks)
    {
        var evaluator = new RecallAtKEvaluator();
        var result = await evaluator.EvaluateAsync(Messages, Answer,
            additionalContext: [new GoldSourcesContext(gold, mode), new RetrievedChunksContext(chunks)],
            cancellationToken: TestContext.Current.CancellationToken);
        return (result.Get<NumericMetric>(evaluator.RecallMetricName), result.Get<NumericMetric>(RecallAtKEvaluator.FirstHitRankMetricName));
    }

    private static GoldSource Gold(string file, params int[] lines) => new(file, lines);
    private static RetrievedChunk Chunk(string file, int start, int end) => new(file, start, end);

    [Fact]
    public async Task Hit_when_a_chunk_from_the_gold_file_contains_a_gold_line()
    {
        var (recall, rank) = await Evaluate([Gold("docs/contracts.md", 45)], "any",
            Chunk("docs/decisions.md", 1, 50), Chunk("docs/contracts.md", 43, 65));

        Assert.Equal(1, recall.Value);
        Assert.False(recall.Interpretation!.Failed);
        Assert.Equal(2, rank.Value);
    }

    [Theory]
    [InlineData(43, 45)] // the range's last line
    [InlineData(45, 65)] // the range's first line
    public async Task The_range_ends_count(int start, int end)
    {
        var (recall, _) = await Evaluate([Gold("docs/contracts.md", 45)], "any", Chunk("docs/contracts.md", start, end));

        Assert.Equal(1, recall.Value);
    }

    [Fact]
    public async Task Miss_when_the_file_matches_but_the_range_does_not()
    {
        var (recall, rank) = await Evaluate([Gold("docs/contracts.md", 45)], "any",
            Chunk("docs/contracts.md", 9, 15), Chunk("docs/contracts.md", 67, 78));

        Assert.Equal(0, recall.Value);
        Assert.True(recall.Interpretation!.Failed);
        Assert.Null(rank.Value);
        Assert.False(rank.Interpretation!.Failed);
    }

    [Fact]
    public async Task Miss_when_the_lines_match_but_the_file_does_not()
    {
        var (recall, _) = await Evaluate([Gold("README.md", 71)], "any", Chunk("docs/architecture.md", 65, 76));

        Assert.Equal(0, recall.Value);
    }

    [Fact]
    public async Task Any_mode_needs_one_gold_source()
    {
        var (recall, _) = await Evaluate([Gold("docs/contracts.md", 12), Gold("README.md", 81)], "any", Chunk("README.md", 78, 87));

        Assert.Equal(1, recall.Value);
    }

    [Fact]
    public async Task All_mode_with_one_of_two_sources_is_half_and_failed()
    {
        var (recall, rank) = await Evaluate([Gold("docs/architecture.md", 46), Gold("README.md", 38)], "all",
            Chunk("docs/architecture.md", 40, 46), Chunk("docs/contracts.md", 1, 10));

        Assert.Equal(0.5, recall.Value);
        Assert.True(recall.Interpretation!.Failed);
        Assert.Equal(1, rank.Value);
    }

    [Fact]
    public async Task All_mode_with_both_sources_passes()
    {
        var (recall, _) = await Evaluate([Gold("docs/architecture.md", 46), Gold("README.md", 38)], "all",
            Chunk("README.md", 34, 42), Chunk("docs/architecture.md", 40, 46));

        Assert.Equal(1, recall.Value);
    }

    [Fact]
    public async Task Only_the_top_5_count()
    {
        var (recall, rank) = await Evaluate([Gold("docs/contracts.md", 45)], "any",
            Chunk("a.md", 1, 2), Chunk("b.md", 1, 2), Chunk("c.md", 1, 2), Chunk("d.md", 1, 2), Chunk("e.md", 1, 2),
            Chunk("docs/contracts.md", 43, 65));

        Assert.Equal(0, recall.Value);
        Assert.Null(rank.Value);
    }

    [Fact]
    public async Task A_range_of_gold_lines_hits_on_any_of_them()
    {
        var (recall, _) = await Evaluate([Gold("docs/contracts.md", 69, 70, 71, 72, 73, 74, 75, 76)], "any", Chunk("docs/contracts.md", 74, 80));

        Assert.Equal(1, recall.Value);
    }

    [Fact]
    public async Task A_row_without_gold_has_no_value_and_does_not_fail()
    {
        var (recall, rank) = await Evaluate([], "any", Chunk("docs/architecture.md", 32, 36));

        Assert.Null(recall.Value);
        Assert.False(recall.Interpretation!.Failed);
        Assert.Null(rank.Value);
        Assert.False(rank.Interpretation!.Failed);
    }

    [Fact]
    public async Task Missing_context_is_a_diagnostic_not_a_score()
    {
        var evaluator = new RecallAtKEvaluator();
        var result = await evaluator.EvaluateAsync(Messages, Answer, cancellationToken: TestContext.Current.CancellationToken);

        var recall = result.Get<NumericMetric>(evaluator.RecallMetricName);
        Assert.Null(recall.Value);
        Assert.Contains(recall.Diagnostics!, d => d.Severity == EvaluationDiagnosticSeverity.Error);
    }

    // On the real set: the week 4 retrieval of A15 (gold chunk at rank 4) and B6 (both sources sent)
    [Fact]
    public async Task Week_4_retrievals_score_as_hand_labelled()
    {
        var a15 = AskSetData.Row("A15");
        var (a15Recall, a15Rank) = await Evaluate(a15.Gold, a15.GoldMode,
            Chunk("docs/contracts.md", 80, 85), Chunk("docs/decisions.md", 33, 35), Chunk("docs/decisions.md", 15, 17),
            Chunk("docs/contracts.md", 67, 78), Chunk("docs/contracts.md", 17, 41));
        Assert.Equal(1, a15Recall.Value);
        Assert.Equal(4, a15Rank.Value);

        var b6 = AskSetData.Row("B6");
        var (b6Recall, _) = await Evaluate(b6.Gold, b6.GoldMode,
            Chunk("README.md", 34, 42), Chunk("docs/architecture.md", 79, 84), Chunk("docs/contracts.md", 1, 5),
            Chunk("docs/architecture.md", 40, 46), Chunk("docs/decisions.md", 1, 5));
        Assert.Equal(1, b6Recall.Value);
    }
}
