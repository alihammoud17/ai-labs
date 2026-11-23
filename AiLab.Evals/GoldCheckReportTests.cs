using AiLab.Evals.Evaluators;
using AiLab.Evals.Hosting;
using AiLab.Evals.Summary;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals;

// Offline: GoldCheck over stored runs (RunRecord in, verdicts and counts out), and the writer for real results.
public class GoldCheckReportTests
{
    private static readonly IReadOnlyDictionary<string, double?> NoScores =
        EvalSummary.Judged.ToDictionary(n => n, _ => (double?)null);
    private static readonly IReadOnlyDictionary<string, bool?> NoFailures =
        EvalSummary.Judged.ToDictionary(n => n, _ => (bool?)null);

    private static RunRecord Run(string id, string iteration, string answer, string status = "grounded", string gate = "passed",
        params RetrievedChunk[] citations) =>
        new(id, "Category", "answer", iteration, status, new Dictionary<string, string> { ["gate"] = gate },
            null, null, null, NoScores, NoFailures, [], 0, answer, citations);

    [Fact]
    public void Checks_a_stored_run_with_its_answer_status_gate_and_citations()
    {
        var right = GoldCheckReport.Check(Run("A3", "1", "`2026-03-18` [1]", citations: new RetrievedChunk("docs/contracts.md", 43, 65)));
        var gated = GoldCheckReport.Check(Run("E5", "1", "", status: "abstained", gate: "too_far"));
        var judged = GoldCheckReport.Check(Run("A1", "1", "anything"));

        Assert.Equal(GoldVerdict.Correct, right.Result.Verdict);
        Assert.Equal(GoldVerdict.Correct, gated.Result.Verdict);
        Assert.Equal(GoldVerdict.NotChecked, judged.Result.Verdict);
    }

    [Fact]
    public void Counts_split_checked_runs_into_correct_and_wrong_and_keep_not_checked_apart()
    {
        var counts = GoldCheckReport.Count(
        [
            GoldCheckReport.Check(Run("A3", "1", "`2026-03-18` [1]", citations: new RetrievedChunk("docs/contracts.md", 43, 65))),
            GoldCheckReport.Check(Run("A3", "2", "`2026-03-17` [1]", citations: new RetrievedChunk("docs/contracts.md", 43, 65))),
            GoldCheckReport.Check(Run("A1", "1", "anything")),
        ]);

        Assert.Equal(new GoldCheckReport.Counts(Runs: 3, Checked: 2, Correct: 1, Wrong: 1, NotChecked: 1), counts);
    }

    [Fact]
    public void A_cell_without_results_says_no_data_not_zero_correct()
    {
        string md = GoldCheckReport.Markdown([new("matrix-heading-haiku", null)]);

        Assert.Contains("| matrix-heading-haiku | no data |", md);
        Assert.Contains("this is not 0 correct", md);
    }

    [Fact]
    public void Wrong_runs_are_listed_with_their_reason_and_an_incomplete_cell_is_flagged()
    {
        string md = GoldCheckReport.Markdown([new("x", [Run("A3", "1", "`2026-03-17`")])]);

        Assert.Contains("| A3 | exact | **wrong** | no data | no data |", md);
        Assert.Contains("- A3 run 1: missing: 2026-03-18", md);
        Assert.Contains("**x: incomplete**", md);
    }

    // AILAB_EVAL_GOLDCHECK=<execution>,<execution>,...: writes eval-runs/meai/goldcheck-week5.md from the stored results
    [Fact]
    public void Write_goldcheck()
    {
        string? executions = Environment.GetEnvironmentVariable("AILAB_EVAL_GOLDCHECK");
        Assert.SkipWhen(executions is null,
            "Set AILAB_EVAL_GOLDCHECK=matrix-heading-haiku,matrix-fixed-haiku,matrix-heading-llama,matrix-fixed-llama to write the gold check.");

        string root = RepoPaths.Resolve(EvalSettings.StorageRoot);
        var cells = executions!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name =>
            {
                string folder = Path.Combine(root, "results", name);
                return new GoldCheckReport.Cell(name, Directory.Exists(folder) ? EvalSummary.Load(folder) : null);
            })
            .ToList();

        string path = Path.Combine(root, "goldcheck-week5.md");
        File.WriteAllText(path, GoldCheckReport.Markdown(cells));
        TestContext.Current.SendDiagnosticMessage($"Wrote {path}");
    }
}
