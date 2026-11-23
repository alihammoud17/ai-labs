using AiLab.Evals.Evaluators;
using AiLab.Evals.Hosting;
using AiLab.Evals.Summary;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals;

// Offline: cost per correct answer from hand-made runs. Correct comes from GoldCheck on the run's answer and citations;
// cost only from /ask's own tokens in the Ask outcome metadata.
public class CostPerCorrectReportTests
{
    private static readonly IReadOnlyDictionary<string, double?> NoScores =
        EvalSummary.Judged.ToDictionary(n => n, _ => (double?)null);
    private static readonly IReadOnlyDictionary<string, bool?> NoFailures =
        EvalSummary.Judged.ToDictionary(n => n, _ => (bool?)null);

    private static readonly DateOnly Date = new(2026, 11, 19);
    private static readonly RetrievedChunk A3Chunk = new("docs/contracts.md", 43, 65);

    // A run that reached the model, with /ask's tokens and times; judge turns can be added to prove they're ignored
    private static RunRecord Asked(string id, string iteration, string answer, string model = "claude-haiku-5-5",
        long input = 10_000, long output = 1_000, double ms = 1000, Turn[]? turns = null, params RetrievedChunk[] citations) =>
        new(id, "Category", "answer", iteration, "grounded",
            new Dictionary<string, string>
            {
                ["gate"] = "passed", ["model"] = model, ["inputTokens"] = input.ToString(), ["outputTokens"] = output.ToString(),
                ["retrievalMs"] = "0", ["generationMs"] = ms.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            null, null, null, NoScores, NoFailures, turns ?? [], 0, answer, citations);

    private static RunRecord Right(string iteration, string model = "claude-haiku-5-5", Turn[]? turns = null) =>
        Asked("A3", iteration, "`2026-03-18` [1]", model, turns: turns, citations: A3Chunk);

    private static RunRecord WrongValue(string iteration, string model = "claude-haiku-5-5") =>
        Asked("A3", iteration, "`2026-03-17` [1]", model, citations: A3Chunk);

    // E5, stopped by the gate: no model, no tokens
    private static RunRecord Gated(string iteration) =>
        new("E5", "Out of domain", "gate", iteration, "abstained",
            new Dictionary<string, string> { ["gate"] = "too_far", ["model"] = "", ["inputTokens"] = "", ["outputTokens"] = "" },
            null, null, null, NoScores, NoFailures, [], 0, "", []);

    // 1
    [Fact]
    public void Cost_per_correct_is_the_ask_cost_of_the_checked_runs_divided_by_the_correct_ones()
    {
        // Each run: 10,000 in × $0.10/1M = $0.001, plus 1,000 out × $0.50/1M = $0.0005 → $0.0015.
        // 4 checked runs → $0.0060; 3 correct → $0.0060 / 3 = $0.002 per correct.
        // The judge turn (1M tokens) on run 1 isn't /ask's call and adds nothing.
        RunRecord[] runs = [Right("1", turns: [new Turn("claude-haiku-5-5", 1_000_000, 1_000_000, false, 1)]), Right("2"), Right("3"), WrongValue("4")];

        var row = CostPerCorrectReport.Summarise(runs);

        Assert.Equal((4, 3), (row.Checked, row.Correct));
        Assert.Equal(0.0060m, row.Cost);
        Assert.Equal(0.002m, row.CostPerCorrect);
        Assert.Equal(1.50m, row.CostPerThousand);   // $0.0015 per answer × 1,000
        Assert.Equal(0.75, row.Accuracy);
        Assert.Contains("| x | 4 | 4 | 3 | 75% | 1 / 1 | 4 | `claude-haiku-5-5` | $0.006000 | $0.002000 | $1.50 |",
            CostPerCorrectReport.Markdown([new("x", runs)], Date, expectedRuns: 4));
    }

    // 2
    [Fact]
    public void Zero_correct_is_n_a_not_zero_dollars()
    {
        string md = CostPerCorrectReport.Markdown([new("x", [WrongValue("1"), WrongValue("2")])], Date, expectedRuns: 2);

        Assert.Contains("| x | 2 | 2 | 0 | 0% | 0 / 1 | 2 | `claude-haiku-5-5` | $0.003000 | n/a |", md);
        Assert.Contains("**x: 0 correct**", md);
    }

    // 3
    [Fact]
    public void An_unpriced_model_makes_the_cost_unknown_not_lower()
    {
        var row = CostPerCorrectReport.Summarise([Right("1"), Right("2", model: "some-new-model")]);
        string md = CostPerCorrectReport.Markdown([new("x", [Right("1"), Right("2", model: "some-new-model")])], Date, expectedRuns: 2);

        Assert.Null(row.Cost);
        Assert.Null(row.CostPerCorrect);
        Assert.Contains("| `claude-haiku-5-5`, `some-new-model` | ? | ? | ? |", md);
        Assert.Contains("no price for some-new-model", md);
    }

    // 4: the exact-ID lookup in PriceOf (the prefix rule alone returned null for Bedrock IDs)
    [Theory]
    [InlineData("us.amazon.nova-lite-v1:0", "0.00084")]       // 10,000 × $0.06/1M + 1,000 × $0.24/1M = $0.0006 + $0.00024
    [InlineData("us.anthropic.claude-haiku-5-5", "0.00165")]  // 10,000 × $0.11/1M + 1,000 × $0.55/1M = $0.0011 + $0.00055
    public void Bedrock_model_ids_are_priced_by_exact_id(string model, string expected)
    {
        var row = CostPerCorrectReport.Summarise([Right("1", model)]);

        Assert.Empty(row.UnpricedModels);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), row.Cost);
    }

    // 5
    [Fact]
    public void A_gated_run_is_checked_adds_nothing_and_does_not_make_the_cell_unpriced()
    {
        var row = CostPerCorrectReport.Summarise([Right("1"), Gated("1")]);

        Assert.Equal((2, 2, 1), (row.Checked, row.Correct, row.Reached));
        Assert.Equal(0.0015m, row.Cost);              // only A3's call
        Assert.Equal(0.00075m, row.CostPerCorrect);   // $0.0015 / 2 correct
        Assert.Empty(row.UnpricedModels);
    }

    // 6
    [Fact]
    public void An_execution_without_results_is_a_missing_row()
    {
        string md = CostPerCorrectReport.Markdown([new("bedrock-nova-lite", null), new("empty", [])], Date);

        Assert.Contains("| bedrock-nova-lite | missing |", md);
        Assert.Contains("| empty | missing |", md);
        Assert.Contains("not 0 correct and not $0", md);
    }

    // 7: A1 is a judge row
    [Fact]
    public void Not_checked_runs_are_in_neither_checked_nor_cost()
    {
        var row = CostPerCorrectReport.Summarise([Right("1"), Asked("A1", "1", "Registration returns 202.", input: 1_000_000)]);

        Assert.Equal((2, 1, 1), (row.Runs, row.Checked, row.Correct));
        Assert.Equal(0.0015m, row.Cost);
    }

    [Fact]
    public void A_local_model_reads_zero_dollars_local()
    {
        string md = CostPerCorrectReport.Markdown([new("x", [Right("1", "llama3.2"), WrongValue("2", "llama3.2")])], Date, expectedRuns: 2);

        Assert.Contains("| `llama3.2` | $0 (local) | $0 (local) | $0 (local) |", md);
    }

    // Found in Block 8: a Bedrock run on the emulator stored only E5's 3 gated runs (every other row failed with a 502), and
    // the report read "3 of 3 correct, 100%, $0.000000". Fewer runs than a complete cell → incomplete, no accuracy or cost.
    [Fact]
    public void An_incomplete_execution_shows_no_accuracy_or_cost()
    {
        string md = CostPerCorrectReport.Markdown([new("bedrock-nova-lite", [Gated("1"), Gated("2"), Gated("3")])], Date);

        Assert.Contains("| bedrock-nova-lite | incomplete: 3 of 90 | 3 | 3 | n/a | 1 / 1 | 0 | - | n/a | n/a | n/a | - | - |", md);
        Assert.Contains("**bedrock-nova-lite: incomplete**, 3 of 90 runs stored", md);
        Assert.DoesNotContain("100%", md);
        Assert.DoesNotContain("$0.000000", md);
    }

    // 4.3: projected rows sit in their own table, never in the measured one, and read "pending"
    [Fact]
    public void Projected_rows_have_their_own_table_and_pending_correctness()
    {
        // Source runs: 10,000 in / 1,000 out each. us.anthropic.claude-haiku-5-5: $0.0011 + $0.00055 = $0.00165 per answer,
        // exactly 1.1 × claude-haiku-5-5's $0.0015
        CostPerCorrectReport.Cell source = new("matrix-heading-haiku", [Right("1"), Right("2"), Gated("1")]);
        string md = CostPerCorrectReport.Markdown([source, new("bedrock-haiku-5-5", null)], Date,
            ["us.anthropic.claude-haiku-5-5", "us.amazon.nova-lite-v1:0", "unknown-model"], "matrix-heading-haiku");

        string[] parts = md.Split("## Projected (not measured)");
        Assert.Equal(2, parts.Length);
        Assert.DoesNotContain("us.anthropic", parts[0]);   // nothing projected above the projected heading
        Assert.DoesNotContain("pending", parts[0]);
        Assert.Contains("| bedrock-haiku-5-5 | missing |", parts[0]);
        Assert.Contains("| `us.anthropic.claude-haiku-5-5` | $0.11 / $0.55 | 10,000 / 1,000 | matrix-heading-haiku | $0.001650 | $1.65 | pending |", parts[1]);
        Assert.Contains("| `us.amazon.nova-lite-v1:0` | $0.06 / $0.24 | 10,000 / 1,000 | matrix-heading-haiku | $0.000840 | $0.84 | pending |", parts[1]);
        Assert.Contains("| `unknown-model` | ? | 10,000 / 1,000 | matrix-heading-haiku | ? | ? | pending |", parts[1]);
        Assert.Contains("another tokenizer (Nova Lite)", parts[1]);
    }

    [Fact]
    public void A_projection_without_source_data_says_so()
    {
        string md = CostPerCorrectReport.Markdown([new("matrix-heading-haiku", null)], Date, ["us.amazon.nova-lite-v1:0"], "matrix-heading-haiku");

        Assert.Contains("| `us.amazon.nova-lite-v1:0` | $0.06 / $0.24 | no source data | matrix-heading-haiku | n/a | n/a | pending |", md);
    }

    [Fact]
    public void Without_projected_models_there_is_no_projected_section()
    {
        Assert.DoesNotContain("Projected", CostPerCorrectReport.Markdown([new("x", [Right("1")])], Date));
    }

    // AILAB_EVAL_COST=<execution>,<execution>,...: writes docs/evals/cost-per-correct-2026-11-19.md (D2) from the stored
    // results. Optional: AILAB_EVAL_COST_PROJECTED=<model id>,... and AILAB_EVAL_COST_SOURCE=<execution> add the
    // projected table (Block 7). No model call.
    [Fact]
    public void Write_cost_per_correct()
    {
        string? executions = Environment.GetEnvironmentVariable("AILAB_EVAL_COST");
        Assert.SkipWhen(executions is null,
            "Set AILAB_EVAL_COST=matrix-heading-haiku,matrix-fixed-haiku,matrix-heading-llama,matrix-fixed-llama to write the cost-per-correct report.");

        static string[] List(string? value) => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        string root = RepoPaths.Resolve(EvalSettings.StorageRoot);
        var cells = List(executions)
            .Select(name =>
            {
                string folder = Path.Combine(root, "results", name);
                return new CostPerCorrectReport.Cell(name, Directory.Exists(folder) ? EvalSummary.Load(folder) : null);
            })
            .ToList();

        string path = RepoPaths.Resolve("docs/evals/cost-per-correct-2026-11-19.md");
        File.WriteAllText(path, CostPerCorrectReport.Markdown(cells, Date,
            List(Environment.GetEnvironmentVariable("AILAB_EVAL_COST_PROJECTED")), Environment.GetEnvironmentVariable("AILAB_EVAL_COST_SOURCE")));
        TestContext.Current.SendDiagnosticMessage($"Wrote {path}");
    }
}
