using AiLab.Evals.Hosting;
using AiLab.Evals.Summary;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals;

public class EvalSummaryTests
{
    // The shape MEAI 10.11 writes (trimmed from a real dry-run result)
    private const string A3Run = """
    {
      "scenarioName": "AiLab.Evals.AskSetEvalTests.Single fact.A3",
      "iterationName": "2",
      "executionName": "x",
      "tags": ["expected:answer", "check:exact", "category:Single fact"],
      "evaluationResult": { "metrics": {
        "Recall@5": { "value": 1, "interpretation": { "rating": "exceptional", "failed": false } },
        "First hit rank": { "value": 1 },
        "Ask outcome": { "value": "grounded", "metadata": { "gate": "passed", "bestDistance": "0.2558", "model": "claude-haiku-5-5", "inputTokens": "2685", "outputTokens": "48" } },
        "Groundedness": { "value": 5, "interpretation": { "rating": "exceptional", "failed": false } },
        "Relevance": { "value": 3, "interpretation": { "rating": "average", "failed": true } },
        "Retrieval": { "value": null, "interpretation": { "rating": "inconclusive", "failed": true } }
      } },
      "chatDetails": { "turnDetails": [
        { "latency": 1.5, "model": "claude-haiku-5-5", "usage": { "inputTokenCount": 1000000, "outputTokenCount": 0 }, "cacheHit": false },
        { "latency": 0.01, "model": "claude-haiku-5-5-20261001", "usage": { "inputTokenCount": 0, "outputTokenCount": 1000000 }, "cacheHit": true }
      ] }
    }
    """;

    [Fact]
    public void Parses_a_stored_result()
    {
        var run = EvalSummary.Parse(A3Run);

        Assert.Equal(("A3", "Single fact", "answer", "2"), (run.Id, run.Category, run.Expected, run.Iteration));
        Assert.Equal("grounded", run.Status);
        Assert.Equal("passed", run.Outcome["gate"]);
        Assert.Equal(1, run.Recall);
        Assert.Equal(5, run.Scores["Groundedness"]);
        Assert.True(run.JudgeFailed["Relevance"]);
        Assert.Null(run.Scores["Retrieval"]);
        Assert.Equal(2, run.Turns.Count);
        Assert.True(run.Turns[1].CacheHit);
    }

    [Fact]
    public void Prices_turns_by_model_prefix_and_bills_only_cache_misses()
    {
        var run = EvalSummary.Parse(A3Run);

        Assert.Equal(0.10m, EvalSummary.Cost(run.Turns[0]));  // 1M input at $0.10
        Assert.Equal(0.50m, EvalSummary.Cost(run.Turns[1]));  // 1M output at $0.50, dated model id

        string md = EvalSummary.Markdown("x", [run]);
        Assert.Contains("cache hits: 1", md);
        Assert.Contains("$0.1000; the same calls without the cache: $0.6000", md);
    }

    [Fact]
    public void Failed_counts_only_scored_metrics()
    {
        // Relevance 3 is a scored failure; Retrieval failed without a score is an error, not a low score
        string md = EvalSummary.Markdown("x", [EvalSummary.Parse(A3Run)]);

        Assert.Contains("| **All** | 1 | 5 | 3 | - | 1 | 1 |", md);
    }

    // AILAB_EVAL_SUMMARY=<execution>: writes eval-runs/meai/<execution>-summary.md from the stored results
    [Fact]
    public void Write_summary()
    {
        string? execution = Environment.GetEnvironmentVariable("AILAB_EVAL_SUMMARY");
        Assert.SkipWhen(execution is null, "Set AILAB_EVAL_SUMMARY=<execution name> to write that execution's summary.");

        string root = RepoPaths.Resolve(EvalSettings.StorageRoot);
        var runs = EvalSummary.Load(Path.Combine(root, "results", execution!));
        Assert.NotEmpty(runs);
        File.WriteAllText(Path.Combine(root, $"{execution}-summary.md"), EvalSummary.Markdown(execution!, runs));
    }
}
