using System.Text;
using AiLab.Domain.Responses;
using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using AiLab.Evals.Hosting;
using AiLab.Evals.Judging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace AiLab.Evals;

// The 30 questions of ask-set-v1 through the real POST /ask, AILAB_EVAL_RUNS times each (3 by default), scored by:
// - Groundedness (judge): is the answer supported by the chunks that were sent?
// - Relevance (judge): does the answer address the question?
// - Retrieval (judge): do the chunks look relevant to the question, best first?
// - Recall@5 (no model): was a gold chunk among the 5 sent?
// - Ask outcome (no model): status, gate, tokens and time, for the summary and layer 2.
// Scores are recorded, not asserted (the plan: pass/fail thresholds come after the baseline). The test fails only
// when the setup is broken: an HTTP error, a missing metric, an evaluator error, a citation /search didn't return.
public class AskSetEvalTests(EvalFixture fixture) : IClassFixture<EvalFixture>
{
    private static readonly string[] JudgedMetrics =
    [
        GroundednessEvaluator.GroundednessMetricName,
        RelevanceEvaluator.RelevanceMetricName,
        RetrievalEvaluator.RetrievalMetricName,
    ];

    private readonly RecallAtKEvaluator _recall = new();

    [Theory]
    [MemberData(nameof(AskSetData.Ids), MemberType = typeof(AskSetData))]
    public async Task Ask_set_v1(string id)
    {
        Assert.SkipUnless(fixture.Settings.Enabled, EvalSettings.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var row = AskSetData.Row(id);

        // Groundedness and Relevance only when there's an answer to judge; Retrieval and the two no-model ones always
        IEvaluator[] evaluators =
        [
            new AnsweredOnlyEvaluator(new GroundednessEvaluator()),
            new AnsweredOnlyEvaluator(new RelevanceEvaluator()),
            new RetrievalEvaluator(),
            _recall,
            new AskOutcomeEvaluator(),
        ];
        var reporting = EvalReporting.Create(fixture.Settings, fixture.Judge, evaluators);

        var problems = new List<string>();
        for (int i = 1; i <= fixture.Settings.Runs; i++)
        {
            // The iteration name is part of the cache key: runs 1, 2 and 3 are three real answers the first time
            await using var run = await reporting.CreateScenarioRunAsync(
                EvalReporting.ScenarioName(row), iterationName: i.ToString(), additionalTags: EvalReporting.Tags(row), cancellationToken: ct);

            // Haiku answers through the run's client, so its answer is cached with the judges' calls. llama3.2 is free.
            fixture.Chat.Target = fixture.Settings.Answers == "haiku" ? run.ChatConfiguration!.ChatClient : fixture.Ollama;
            try
            {
                var search = await fixture.SearchAsync(row.Question, ct);
                var ask = await fixture.AskAsync(row.Question, ct);
                problems.AddRange(CitationsOutsideSearch(i, search, ask));

                var messages = new[] { new ChatMessage(ChatRole.User, row.Question) };
                var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ask.Answer));
                EvaluationContext[] context =
                [
                    new GroundednessEvaluatorContext(SentSources(search)),
                    new RetrievalEvaluatorContext(search.Hits.Select(h => h.Text)),
                    GoldSourcesContext.From(row),
                    new RetrievedChunksContext([.. search.Hits.Select(h => new RetrievedChunk(h.Source, h.StartLine, h.EndLine))]),
                    new AskOutcomeContext(ask),
                ];

                var result = await run.EvaluateAsync(messages, response, context, ct);
                problems.AddRange(SetupProblems(i, result));
            }
            finally
            {
                fixture.Chat.Target = null;
            }
        }

        Assert.True(problems.Count == 0, $"{row.Id}: {string.Join(" | ", problems)}");
    }

    // The chunks as the judge sees them: numbered like the <source n> blocks /ask sends, with file and lines
    internal static string SentSources(SearchResponse search)
    {
        var sb = new StringBuilder();
        foreach (var hit in search.Hits)
        {
            sb.AppendLine($"[{hit.Rank}] {hit.Source}, lines {hit.StartLine}-{hit.EndLine}, section: {hit.Heading ?? "-"}");
            sb.AppendLine(hit.Text.Trim());
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    // The judges get /search's chunks as "what was sent". If /ask cited something else, that assumption is wrong.
    private static IEnumerable<string> CitationsOutsideSearch(int iteration, SearchResponse search, AskResponse ask) =>
        ask.Citations
            .Where(c => !search.Hits.Any(h => h.Source == c.Source && h.StartLine == c.StartLine && h.EndLine == c.EndLine))
            .Select(c => $"run {iteration}: cited [{c.N}] {c.Source}:{c.StartLine}-{c.EndLine} isn't in /search's top {search.Hits.Count}");

    // Every metric present, and no evaluator error (a missing context, an unreadable or cut-off judge reply)
    private IEnumerable<string> SetupProblems(int iteration, EvaluationResult result)
    {
        string[] expected = [.. JudgedMetrics, _recall.RecallMetricName, RecallAtKEvaluator.FirstHitRankMetricName, AskOutcomeEvaluator.OutcomeMetricName];
        foreach (string name in expected)
        {
            if (!result.Metrics.TryGetValue(name, out var metric))
            {
                yield return $"run {iteration}: no {name} metric";
                continue;
            }

            foreach (var error in metric.Diagnostics?.Where(d => d.Severity == EvaluationDiagnosticSeverity.Error) ?? [])
                yield return $"run {iteration}: {name}: {error.Message}";
        }
    }
}
