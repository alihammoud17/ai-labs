using System.Diagnostics;
using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using AiLab.Evals.Hosting;
using AiLab.Evals.Retrieval;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals;

// Retrieval on the Knowledge Base (week 8 Tuesday, D1): the same answerable rows as RetrievalEvalTests.Retrieval_hit_rate,
// the same top (10), each passage placed in its file by PassageLocator and scored by the same RetrievalMetrics.FirstHitRank.
// No Rag host, no Ollama, no Postgres (KnowledgeBaseFixture). Runs with AILAB_EVAL=1 and KnowledgeBase__Id set.
// Writes eval-runs/retrieval/<date>-kb-emulator.md (or -kb-bedrock.md: the endpoint decides, KnowledgeBaseTarget).
// A passage that can't be located stays at its rank as a non-hit and is listed. An AWS error fails the run: a broken
// setup writes no report, so it can never read as 0 hits.
public class KnowledgeBaseRetrievalEvalTests(KnowledgeBaseFixture fixture) : IClassFixture<KnowledgeBaseFixture>
{
    [Fact]
    public async Task Retrieval_hit_rate_kb()
    {
        Assert.SkipUnless(fixture.Settings.Enabled, EvalSettings.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        int top = RetrievalEvalTests.Top;

        var results = new List<RetrievalResult>();
        var unlocated = new List<UnlocatedPassage>();
        var latencies = new List<double>();
        foreach (var row in AskSetData.Load().Rows.Where(r => r.Answerable && r.Gold.Count > 0))
        {
            var watch = Stopwatch.StartNew();
            var passages = await fixture.Retriever.RetrieveAsync(row.Question, top, ct);
            latencies.Add(watch.Elapsed.TotalMilliseconds);
            Assert.NotEmpty(passages); // a KB with documents always returns something

            var ranked = new List<RetrievedChunk>();
            foreach (var passage in passages)
            {
                var location = fixture.Locator.Locate(passage.Source, passage.Text);
                if (location.Chunk is { } chunk)
                {
                    ranked.Add(chunk);
                }
                else
                {
                    ranked.Add(UnlocatedPassage.NotLocated(passage.Source));
                    unlocated.Add(new UnlocatedPassage(row.Id, passage.Rank, passage.Source, location.Reason!));
                }
            }
            results.Add(new RetrievalResult(row, RetrievalMetrics.FirstHitRank(row, ranked), ranked));
        }

        var target = fixture.Target;
        string source = $"**{target.Label}.** Bedrock `Retrieve` on Knowledge Base `{fixture.KnowledgeBase.Id}`, `numberOfResults` {top}" +
                        (fixture.KnowledgeBase.SearchType is { } type ? $", `overrideSearchType` {type}" : "") +
                        ", for each answerable row of ask-set-v1; each passage placed in its corpus file by `PassageLocator` and scored by the same `RetrievalMetrics` as `/search`. No chat model." +
                        (target == KnowledgeBaseTarget.Emulator
                            ? " aws-emulator: keyword term-frequency search, one cut-off passage per document, so this measures that stand-in, not Bedrock. Chunker: n/a."
                            : " Chunker: n/a (the data source's chunking).");
        string report = RetrievalReport.Build("kb", "n/a", top, DateOnly.FromDateTime(DateTime.Now), results,
            title: target.Label, source: source, unlocated: unlocated, latenciesMs: latencies);

        string path = RepoPaths.Resolve(Path.Combine(RetrievalEvalTests.OutputFolder, $"{DateTime.Now:yyyy-MM-dd}-{target.ExecutionName}.md"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, report, ct);
        TestContext.Current.SendDiagnosticMessage($"Wrote {path}");

        if (unlocated.Count > 0)
            TestContext.Current.AddWarning($"{unlocated.Count} passage(s) not located: listed in the report's Not located section.");
    }
}
