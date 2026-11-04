using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using AiLab.Evals.Hosting;
using AiLab.Evals.Retrieval;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals;

// Retrieval on its own: GET /search for every answerable row of ask-set-v1, first-hit ranks, hit rate@k and MRR.
// No chat model and no judge: needs only Ollama (embeddings) and Postgres, costs $0. Runs with AILAB_EVAL=1.
// Writes eval-runs/retrieval/<date>-<mode>.md. Fails only on a broken setup (an HTTP error, no hits at all) and, for
// vector, when the ranks differ from Tuesday's MEAI baseline (results in eval-runs/meai, when present).
//   AILAB_EVAL=1 dotnet test AiLab.Evals --filter RetrievalEvalTests
public class RetrievalEvalTests(EvalFixture fixture) : IClassFixture<EvalFixture>
{
    public const int Top = 10;
    public const string OutputFolder = "eval-runs/retrieval";
    public const string TuesdayExecution = "baseline-2026-11-03-haiku";

    [Theory]
    [InlineData("vector")]
    public async Task Retrieval_hit_rate(string mode)
    {
        Assert.SkipUnless(fixture.Settings.Enabled, EvalSettings.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        var tuesday = mode == "vector"
            ? RetrievalReport.TuesdayRanks(RepoPaths.Resolve(Path.Combine(EvalSettings.StorageRoot, "results", TuesdayExecution)))
            : new Dictionary<string, int?>();

        var results = new List<RetrievalResult>();
        foreach (var row in AskSetData.Load().Rows.Where(r => r.Answerable && r.Gold.Count > 0))
        {
            var search = await fixture.SearchAsync(row.Question, ct, Top, mode);
            Assert.NotEmpty(search.Hits);

            List<RetrievedChunk> ranked = [.. search.Hits.Select(h => new RetrievedChunk(h.Source, h.StartLine, h.EndLine))];
            bool hasTuesday = tuesday.TryGetValue(row.Id, out int? tuesdayRank);
            results.Add(new RetrievalResult(row, RetrievalMetrics.FirstHitRank(row, ranked), ranked, tuesdayRank, hasTuesday));
        }

        string report = RetrievalReport.Build(mode, fixture.Ask.Chunker, Top, DateOnly.FromDateTime(DateTime.Now), results);
        string path = RepoPaths.Resolve(Path.Combine(OutputFolder, $"{DateTime.Now:yyyy-MM-dd}-{mode}.md"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, report, ct);
        TestContext.Current.SendDiagnosticMessage($"Wrote {path}");

        if (tuesday.Count == 0 && mode == "vector")
            TestContext.Current.AddWarning($"No Tuesday results in {EvalSettings.StorageRoot}/results/{TuesdayExecution}: ranks not compared.");

        var changed = results.Where(r => r.HasTuesday && !RetrievalReport.SameAsTuesday(r))
            .Select(r => $"{r.Row.Id}: {r.Rank?.ToString() ?? "miss"} (Tuesday {r.Tuesday?.ToString() ?? "-"})").ToList();
        Assert.True(changed.Count == 0, $"Vector ranks differ from Tuesday's: {string.Join("; ", changed)}. Was the corpus re-ingested or the embedding model changed?");
    }
}
