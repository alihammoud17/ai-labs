using AiLab.Evals.Hosting;
using AiLab.Evals.Summary;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals;

// Offline: the experiment matrix's serving numbers (cost per answer, latency) and what it flags.
public class MatrixReportTests
{
    private static readonly IReadOnlyDictionary<string, double?> NoScores =
        EvalSummary.Judged.ToDictionary(n => n, _ => (double?)null);
    private static readonly IReadOnlyDictionary<string, bool?> NoFailures =
        EvalSummary.Judged.ToDictionary(n => n, _ => (bool?)null);

    private static RunRecord Run(string id, string gate, string model = "claude-haiku-5-5", long input = 0, long output = 0,
        double retrievalMs = 0, double generationMs = 0, double? groundedness = null, params Turn[] turns)
    {
        var outcome = new Dictionary<string, string> { ["gate"] = gate, ["model"] = model };
        if (gate == "passed")
        {
            outcome["inputTokens"] = input.ToString();
            outcome["outputTokens"] = output.ToString();
            outcome["retrievalMs"] = retrievalMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
            outcome["generationMs"] = generationMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var scores = new Dictionary<string, double?>(NoScores) { ["Groundedness"] = groundedness };
        return new RunRecord(id, "Category", "answer", "1", "grounded", outcome, 1, false, 1, scores, NoFailures, turns, 0);
    }

    private static Turn Judge(long input, long output, bool cacheHit = false) => new("claude-haiku-5-5", input, output, cacheHit, 1);

    [Fact]
    public void Cost_per_answer_counts_only_the_ask_call_and_skips_gated_runs()
    {
        // 1M in at $0.10 and 1M out at $0.50: $0.10 and $0.50, average $0.30. The judge turns (2M tokens) don't count,
        // and E5's gated run made no call, so it isn't an answer that cost $0.
        RunRecord[] runs =
        [
            Run("A3", "passed", input: 1_000_000, turns: Judge(1_000_000, 0)),
            Run("N1", "passed", output: 1_000_000, turns: Judge(0, 1_000_000)),
            Run("E5", "too_far"),
        ];

        var row = MatrixReport.Summarise(runs);

        Assert.Equal(0.30m, row.CostPerAnswer);
        Assert.Equal((2, 1), (row.Reached, row.Gated));
        Assert.Equal((1, 1), (row.E5Runs, row.E5Gated));
        Assert.Equal(500_000, row.AvgInput);
    }

    [Fact]
    public void Llama_answers_cost_zero_but_an_unknown_model_is_unknown_not_zero()
    {
        var llama = MatrixReport.Summarise([Run("A3", "passed", model: "llama3.2", input: 3000, output: 50)]);
        Assert.Equal(0m, llama.CostPerAnswer);
        Assert.Empty(llama.UnpricedModels);

        var unknown = MatrixReport.Summarise(
        [
            Run("A3", "passed", input: 3000, output: 50),
            Run("N1", "passed", model: "mystery-model", input: 3000, output: 50),
        ]);
        Assert.Null(unknown.CostPerAnswer);
        Assert.Equal(["mystery-model"], unknown.UnpricedModels);

        string md = MatrixReport.Markdown([new("matrix-heading-x", [Run("N1", "passed", model: "mystery-model")])], new DateOnly(2026, 11, 6));
        Assert.Contains("| ? | ? |", md);  // cost per answer and per 1,000
        Assert.Contains("no price for mystery-model", md);
    }

    [Fact]
    public void Latency_is_retrieval_plus_generation_over_the_runs_that_reached_the_model()
    {
        // Totals 1000, 2000, ..., 20000 ms; the gated run (retrieval only) is left out
        var runs = Enumerable.Range(1, 20).Select(i => Run($"Q{i}", "passed", retrievalMs: 100, generationMs: i * 1000 - 100)).ToList();
        runs.Add(Run("E5", "too_far"));

        var row = MatrixReport.Summarise(runs);

        Assert.Equal(10_500, row.MedianMs);   // (10000 + 11000) / 2
        Assert.Equal(19_000, row.P95Ms);      // nearest rank: ceil(0.95 × 20) = 19th value
    }

    [Theory]
    [InlineData(new double[] { 5 }, 5, 5)]
    [InlineData(new double[] { 3, 1, 2 }, 2, 3)]
    [InlineData(new double[] { 4, 1, 3, 2 }, 2.5, 4)]
    public void Median_and_p95_on_known_lists(double[] values, double median, double p95)
    {
        Assert.Equal(median, MatrixReport.Median(values));
        Assert.Equal(p95, MatrixReport.Percentile(values, 95));
    }

    [Fact]
    public void Empty_lists_give_no_value_rather_than_zero()
    {
        Assert.Null(MatrixReport.Median([]));
        Assert.Null(MatrixReport.Percentile([], 95));
        Assert.Null(MatrixReport.Summarise([Run("E5", "too_far")]).CostPerAnswer);
    }

    [Fact]
    public void A_missing_execution_is_reported_not_skipped()
    {
        string md = MatrixReport.Markdown(
        [
            new("matrix-heading-haiku", [Run("A3", "passed", input: 3000, output: 200)]),
            new("matrix-fixed-haiku", null),
        ], new DateOnly(2026, 11, 6));

        Assert.Contains("| matrix-fixed-haiku | missing |", md);
        Assert.Contains("matrix-fixed-haiku: no stored results", md);
    }

    [Fact]
    public void Cache_hits_are_counted_and_flagged()
    {
        string md = MatrixReport.Markdown(
            [new("matrix-heading-haiku", [Run("A3", "passed", turns: [Judge(10, 1, cacheHit: true), Judge(10, 1)])])],
            new DateOnly(2026, 11, 6));

        Assert.Contains("| 1 |\n", md.ReplaceLineEndings("\n"));  // the last column: cache hits
        Assert.Contains("1 cached call(s)", md);
    }

    [Fact]
    public void Hand_scored_columns_are_left_for_the_human()
    {
        string md = MatrixReport.Markdown([new("matrix-heading-haiku", [Run("A3", "passed", groundedness: 5)])], new DateOnly(2026, 11, 6));

        Assert.Contains("| matrix-heading-haiku | 1 | 1 | 0 | ? | ? | 0/0 | 5 |", md);
    }

    // AILAB_EVAL_MATRIX=<execution>,<execution>,...: writes eval-runs/meai/matrix-<date>.md from the stored results
    [Fact]
    public void Write_matrix()
    {
        string? executions = Environment.GetEnvironmentVariable("AILAB_EVAL_MATRIX");
        Assert.SkipWhen(executions is null,
            "Set AILAB_EVAL_MATRIX=matrix-heading-haiku,matrix-heading-llama,matrix-fixed-haiku,matrix-fixed-llama to write the matrix.");

        string root = RepoPaths.Resolve(EvalSettings.StorageRoot);
        var cells = executions!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name =>
            {
                string folder = Path.Combine(root, "results", name);
                return new MatrixReport.Cell(name, Directory.Exists(folder) ? EvalSummary.Load(folder) : null);
            })
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.Now);
        string path = Path.Combine(root, $"matrix-{today:yyyy-MM-dd}.md");
        File.WriteAllText(path, MatrixReport.Markdown(cells, today));
        TestContext.Current.SendDiagnosticMessage($"Wrote {path}");
    }
}
