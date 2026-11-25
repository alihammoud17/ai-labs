using System.Diagnostics;
using System.Text;
using AiLab.Domain.Interfaces;
using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using AiLab.Evals.Hosting;
using AiLab.Evals.Judging;
using AiLab.Evals.Retrieval;
using AiLab.Evals.Summary;
using AiLab.Infrastructure.Files;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Evals;

// Answers through the same suite (week 8 Block 5): the 30 rows of ask-set-v1 through the Knowledge Base's RetrieveAndGenerate,
// AILAB_EVAL_RUNS times each, citations placed by PassageLocator, scored by the same GoldCheck as /ask, stored under the
// execution kb-emulator (or kb-bedrock: the endpoint decides, never AILAB_EVAL_EXECUTION) so GoldCheckReport and
// CostPerCorrectReport read it like any /ask execution. No judge, no chat client, no cache. Needs KnowledgeBase__ModelArn.
// Writes eval-runs/meai/<execution>-summary.md with the label. An AWS error fails the run (setup, not a score).
public class KnowledgeBaseAskEvalTests(KnowledgeBaseFixture fixture) : IClassFixture<KnowledgeBaseFixture>
{
    public const string EmulatorNote =
        "Emulator answers are quoted passages (no model runs): this measures retrieval, not generation.";

    [Fact]
    public async Task Ask_set_v1_kb()
    {
        Assert.SkipUnless(fixture.Settings.Enabled, EvalSettings.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var ask = fixture.Services.GetService<IKnowledgeBaseAsk>()
            ?? throw new InvalidOperationException("KnowledgeBase:ModelArn is not set: run with KnowledgeBase__ModelArn=$GEN_MODEL_ARN.");
        var target = fixture.Target;
        string modelArn = fixture.KnowledgeBase.ModelArn!;

        // Its own name, from the endpoint (Task 1.3): never fixture.Settings.ExecutionName, which would be an /ask folder
        var reporting = DiskBasedReportingConfiguration.Create(
            storageRootPath: RepoPaths.Resolve(EvalSettings.StorageRoot),
            evaluators: [new KnowledgeBaseOutcomeEvaluator()],
            chatConfiguration: null,
            enableResponseCaching: false,
            executionName: target.ExecutionName,
            tags: ["answers:kb", "judge:none", $"target:{target.Place}", $"model:{modelArn}"]);

        var problems = new List<string>();
        var latencies = new List<double>();
        foreach (var row in AskSetData.Load().Rows)
        {
            for (int i = 1; i <= fixture.Settings.Runs; i++)
            {
                await using var run = await reporting.CreateScenarioRunAsync(
                    EvalReporting.ScenarioName(row), iterationName: i.ToString(), additionalTags: EvalReporting.Tags(row), cancellationToken: ct);

                var watch = Stopwatch.StartNew();
                var answer = await ask.AskAsync(row.Question, ct);
                latencies.Add(watch.Elapsed.TotalMilliseconds);

                // Every cited passage placed in its file, once per distinct passage; unlocated ones listed, never cited
                var citations = new List<RetrievedChunk>();
                var unlocated = new List<UnlocatedPassage>();
                foreach (var (reference, n) in answer.Citations.SelectMany(c => c.References).Distinct().Select((r, n) => (r, n + 1)))
                {
                    var location = fixture.Locator.Locate(reference.Source, reference.Text);
                    if (location.Chunk is { } chunk)
                        citations.Add(chunk);
                    else
                        unlocated.Add(new UnlocatedPassage(row.Id, n, reference.Source, location.Reason!));
                }

                var result = await run.EvaluateAsync(
                    [new ChatMessage(ChatRole.User, row.Question)],
                    new ChatResponse(new ChatMessage(ChatRole.Assistant, answer.Text)),
                    [new KnowledgeBaseAskContext(answer, citations, unlocated, modelArn, watch.Elapsed.TotalMilliseconds), new AskSetRowContext(row)],
                    ct);
                problems.AddRange(result.Metrics.Values
                    .SelectMany(m => m.Diagnostics?.Where(d => d.Severity == EvaluationDiagnosticSeverity.Error) ?? [])
                    .Select(d => $"{row.Id} run {i}: {d.Message}"));
            }
        }

        Assert.True(problems.Count == 0, string.Join(" | ", problems));
        await WriteSummaryAsync(target, latencies, ct);
    }

    // The stored results read back through the same reports as /ask's executions, under the label
    private async Task WriteSummaryAsync(KnowledgeBaseTarget target, List<double> latencies, CancellationToken ct)
    {
        string folder = RepoPaths.Resolve(Path.Combine(EvalSettings.StorageRoot, "results", target.ExecutionName));
        var runs = EvalSummary.Load(folder);
        int expected = AskSetData.Load().Rows.Count * fixture.Settings.Runs;

        var sb = new StringBuilder();
        sb.AppendLine($"# {target.Label}: answers, {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine();
        if (target == KnowledgeBaseTarget.Emulator)
            sb.AppendLine($"**{EmulatorNote}** The KB has no gate and no abstain status, so E5 and the abstain rows are scored as the service answered.");
        sb.AppendLine($"Execution `{target.ExecutionName}`, {fixture.Settings.Runs} run(s) per row ({expected} expected, {runs.Count} stored). " +
                      $"Latency per call (RetrieveAndGenerate, client side): median {MatrixReport.Median(latencies):0} ms, p95 {MatrixReport.Percentile(latencies, 95):0} ms. " +
                      "The reports below assume 3 runs per row (90); with fewer they say incomplete, which is by design here.");
        sb.AppendLine();
        sb.AppendLine(GoldCheckReport.Markdown([new GoldCheckReport.Cell(target.ExecutionName, runs)]).Replace("# Gold check over stored results", "## Gold check"));
        sb.AppendLine(CostPerCorrectReport.Markdown([new CostPerCorrectReport.Cell(target.ExecutionName, runs)], DateOnly.FromDateTime(DateTime.Now), expectedRuns: expected)
            .Replace("# Cost per correct answer", "## Cost per correct answer").Replace("\n## Measured", "\n### Measured"));

        var notLocated = runs.Where(r => r.Outcome.GetValueOrDefault("notLocated", "") is { Length: > 0 }).ToList();
        sb.AppendLine("## Citations not located");
        sb.AppendLine();
        sb.AppendLine(notLocated.Count == 0
            ? "None: every cited passage was placed in its corpus file."
            : string.Join(Environment.NewLine, notLocated.Select(r => $"- {r.Id} run {r.Iteration}: {r.Outcome["notLocated"]}")));

        string path = RepoPaths.Resolve(Path.Combine(EvalSettings.StorageRoot, $"{target.ExecutionName}-summary.md"));
        await File.WriteAllTextAsync(path, sb.ToString(), ct);
        TestContext.Current.SendDiagnosticMessage($"Wrote {path}");
    }
}
