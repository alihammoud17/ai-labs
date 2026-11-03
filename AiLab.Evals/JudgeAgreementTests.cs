using System.Globalization;
using System.Text;
using System.Text.Json;
using AiLab.Evals.Hosting;
using AiLab.Evals.Judging;
using AiLab.Infrastructure.Files;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace AiLab.Evals;

// Can the judge be trusted? GroundednessEvaluator on the 44 hand-labelled week 4 runs
// (eval/results/2026-10-31-labelled-runs.json), compared with the labels:
// - positive = labelled M1 or M4 (a claim in no sent chunk, or general knowledge instead of abstaining)
// - the judge says positive when the score is below 4 (MEAI's "failed")
// The chunks are fetched again with /search: same corpus, same embeddings, same top as the run, so the same chunks.
// Not judged: the two 502 runs (no answer) and the gated E5 runs (no model call).
// M2 (right fact, wrong chunk cited) is listed but not counted: Groundedness sees all the chunks as one context.
// Writes eval-runs/meai/judge-check-2026-10-31.md. Records only; never fails on the agreement itself.
public class JudgeAgreementTests(EvalFixture fixture) : IClassFixture<EvalFixture>
{
    private const string Execution = "judge-check-2026-10-31";

    private sealed record LabelledRun(string Date, string Model, string Id, int Top, string Gate, double BestDistance,
        string Status, IReadOnlyList<string> Codes, string Why, string Answer);

    private sealed record LabelledFile(IReadOnlyList<LabelledRun> Runs);

    [Fact]
    public async Task Groundedness_against_the_week_4_hand_labels()
    {
        Assert.SkipUnless(fixture.Settings.Enabled, EvalSettings.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        var file = JsonSerializer.Deserialize<LabelledFile>(
            File.ReadAllText(RepoPaths.Resolve("eval/results/2026-10-31-labelled-runs.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var questions = AskSet.AskSetData.Load().Rows.ToDictionary(r => r.Id);
        var reporting = EvalReporting.Create(fixture.Settings with { ExecutionName = Execution }, fixture.Judge, [new GroundednessEvaluator()]);

        var verdicts = new List<(LabelledRun Run, double? Score, bool? Failed, string? Reason)>();
        var problems = new List<string>();
        foreach (var run in file.Runs.Where(r => r.Gate == "passed" && !r.Status.StartsWith("HTTP")))
        {
            string question = questions[run.Id].Question;
            string model = run.Model.StartsWith("claude") ? "haiku" : "llama";
            await using var scenario = await reporting.CreateScenarioRunAsync(
                $"AiLab.Evals.JudgeAgreementTests.{model}.{run.Id}-top{run.Top}-{run.Date}", cancellationToken: ct,
                additionalTags: [$"labels:{string.Join(' ', run.Codes)}"]);

            var search = await fixture.SearchAsync(question, ct, run.Top);
            var result = await scenario.EvaluateAsync(
                [new ChatMessage(ChatRole.User, question)],
                new ChatResponse(new ChatMessage(ChatRole.Assistant, run.Answer)),
                [new GroundednessEvaluatorContext(AskSetEvalTests.SentSources(search))], ct);

            var metric = result.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);
            foreach (var error in metric.Diagnostics?.Where(d => d.Severity == EvaluationDiagnosticSeverity.Error) ?? [])
                problems.Add($"{model} {run.Id} top {run.Top}: {error.Message}");
            verdicts.Add((run, metric.Value, metric.Interpretation?.Failed, metric.Reason));
        }

        File.WriteAllText(RepoPaths.Resolve($"{EvalSettings.StorageRoot}/{Execution}.md"), Report(verdicts));
        Assert.True(problems.Count == 0, string.Join(" | ", problems));
    }

    private static bool Positive(LabelledRun run) => run.Codes.Any(c => c is "M1" or "M4");

    private static string Report(List<(LabelledRun Run, double? Score, bool? Failed, string? Reason)> verdicts)
    {
        var inv = CultureInfo.InvariantCulture;
        var scored = verdicts.Where(v => v.Score is not null).ToList();
        int tp = scored.Count(v => Positive(v.Run) && v.Failed == true);
        int fn = scored.Count(v => Positive(v.Run) && v.Failed != true);
        int fp = scored.Count(v => !Positive(v.Run) && v.Failed == true);
        int tn = scored.Count(v => !Positive(v.Run) && v.Failed != true);
        string Ratio(int a, int b) => b == 0 ? "-" : ((double)a / b).ToString("0.00", inv);

        var sb = new StringBuilder();
        sb.AppendLine("# Judge check: Groundedness vs the week 4 hand labels");
        sb.AppendLine();
        sb.AppendLine($"Judged runs: {verdicts.Count}, with a score: {scored.Count}. Positive = labelled M1 or M4. Judge positive = score below 4.");
        sb.AppendLine();
        sb.AppendLine("| | Judge: not grounded | Judge: grounded |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine($"| Labelled M1/M4 | {tp} | {fn} |");
        sb.AppendLine($"| Not labelled M1/M4 | {fp} | {tn} |");
        sb.AppendLine();
        sb.AppendLine($"- Precision of \"not grounded\": {Ratio(tp, tp + fp)} ({tp} of {tp + fp})");
        sb.AppendLine($"- Recall of \"not grounded\": {Ratio(tp, tp + fn)} ({tp} of {tp + fn})");
        sb.AppendLine();
        sb.AppendLine("| Model | ID | Top | Labels | Score | Failed | Judge's reason |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var (run, score, failed, reason) in verdicts)
            sb.AppendLine($"| {run.Model} | {run.Id} | {run.Top} | {string.Join(", ", run.Codes)} | {score?.ToString("0", inv) ?? "-"} | {(failed == true ? "yes" : "no")} | {(reason ?? "").Replace("|", "/").Replace("\n", " ")} |");
        return sb.ToString();
    }
}
