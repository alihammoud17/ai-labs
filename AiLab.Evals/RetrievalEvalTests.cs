using AiLab.Application.Search;
using AiLab.Domain.Constants;
using AiLab.Domain.DTOs;
using AiLab.Domain.Outputs;
using AiLab.Domain.Responses;
using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using AiLab.Evals.Hosting;
using AiLab.Evals.Retrieval;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals;

// Retrieval on its own: GET /search for every answerable row of ask-set-v1, first-hit ranks, hit rate@k and MRR.
// No chat model and no judge: needs only Ollama (embeddings) and Postgres, costs $0. Runs with AILAB_EVAL=1.
// Writes eval-runs/retrieval/<date>-<mode>.md (<date>-<mode>-<chunker>.md with AILAB_EVAL_CHUNKER set). Fails only on a
// broken setup (an HTTP error, no hits at all) and, for vector on heading chunks, when the ranks differ from Tuesday's
// MEAI baseline (results in eval-runs/meai, when present; Tuesday used heading, so other chunkers aren't compared).
//   AILAB_EVAL=1 dotnet test AiLab.Evals --filter RetrievalEvalTests
public class RetrievalEvalTests(EvalFixture fixture) : IClassFixture<EvalFixture>
{
    public const int Top = 10;
    public const string OutputFolder = "eval-runs/retrieval";
    public const string TuesdayExecution = "baseline-2026-11-03-haiku";

    [Theory]
    [InlineData("vector")]
    [InlineData("keyword")]
    [InlineData("hybrid")]
    public async Task Retrieval_hit_rate(string mode)
    {
        Assert.SkipUnless(fixture.Settings.Enabled, EvalSettings.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        // Tuesday's baseline was heading chunks: another chunker's ranks are expected to differ
        bool compareWithTuesday = mode == "vector" && fixture.Ask.Chunker == ChunkerNames.Heading;
        var tuesday = compareWithTuesday
            ? RetrievalReport.TuesdayRanks(RepoPaths.Resolve(Path.Combine(EvalSettings.StorageRoot, "results", TuesdayExecution)))
            : new Dictionary<string, int?>();

        var results = new List<RetrievalResult>();
        foreach (var row in AskSetData.Load().Rows.Where(r => r.Answerable && r.Gold.Count > 0))
        {
            var search = await fixture.SearchAsync(row.Question, ct, Top, mode);
            // Keyword search may find nothing (no shared word); vector and hybrid always return hits
            if (mode != "keyword")
                Assert.NotEmpty(search.Hits);

            List<RetrievedChunk> ranked = [.. search.Hits.Select(h => new RetrievedChunk(h.Source, h.StartLine, h.EndLine))];
            bool hasTuesday = tuesday.TryGetValue(row.Id, out int? tuesdayRank);
            results.Add(new RetrievalResult(row, RetrievalMetrics.FirstHitRank(row, ranked), ranked, tuesdayRank, hasTuesday));
        }

        string report = RetrievalReport.Build(mode, fixture.Ask.Chunker, Top, DateOnly.FromDateTime(DateTime.Now), results);
        string path = RepoPaths.Resolve(Path.Combine(OutputFolder, $"{DateTime.Now:yyyy-MM-dd}-{mode}{ChunkerSuffix}.md"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, report, ct);
        TestContext.Current.SendDiagnosticMessage($"Wrote {path}");

        if (tuesday.Count == 0 && compareWithTuesday)
            TestContext.Current.AddWarning($"No Tuesday results in {EvalSettings.StorageRoot}/results/{TuesdayExecution}: ranks not compared.");

        var changed = results.Where(r => r.HasTuesday && !RetrievalReport.SameAsTuesday(r))
            .Select(r => $"{r.Row.Id}: {r.Rank?.ToString() ?? "miss"} (Tuesday {r.Tuesday?.ToString() ?? "-"})").ToList();
        Assert.True(changed.Count == 0, $"Vector ranks differ from Tuesday's: {string.Join("; ", changed)}. Was the corpus re-ingested or the embedding model changed?");
    }

    // Every mode side by side, plus hybrid with k = 10 and 100 (merged here from the same two top-20 lists), and the
    // questions whose first hit moved against vector. Writes eval-runs/retrieval/<date>-comparison.md (or -comparison-<chunker>.md).
    [Fact]
    public async Task Compare_modes()
    {
        Assert.SkipUnless(fixture.Settings.Enabled, EvalSettings.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        string[] columns = ["vector", "keyword", "hybrid", "hybrid k=10", "hybrid k=100"];

        var rows = AskSetData.Load().Rows.Where(r => r.Answerable && r.Gold.Count > 0).ToList();
        var ranks = new Dictionary<string, Dictionary<string, int?>>();
        foreach (var row in rows)
        {
            var vector = Chunks(await fixture.SearchAsync(row.Question, ct, MergeWindow, "vector"));
            var keyword = Chunks(await fixture.SearchAsync(row.Question, ct, MergeWindow, "keyword"));
            var hybrid = Chunks(await fixture.SearchAsync(row.Question, ct, Top, "hybrid"));

            ranks[row.Id] = new()
            {
                ["vector"] = RetrievalMetrics.FirstHitRank(row, [.. vector.Take(Top).Select(c => c.Chunk)]),
                ["keyword"] = RetrievalMetrics.FirstHitRank(row, [.. keyword.Take(Top).Select(c => c.Chunk)]),
                ["hybrid"] = RetrievalMetrics.FirstHitRank(row, [.. hybrid.Select(c => c.Chunk)]),
                ["hybrid k=10"] = RetrievalMetrics.FirstHitRank(row, Merged(vector, keyword, 10)),
                ["hybrid k=100"] = RetrievalMetrics.FirstHitRank(row, Merged(vector, keyword, 100)),
            };
        }

        string path = RepoPaths.Resolve(Path.Combine(OutputFolder, $"{DateTime.Now:yyyy-MM-dd}-comparison{ChunkerSuffix}.md"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, RetrievalReport.Comparison(rows, ranks, columns, fixture.Ask.Chunker, Top, DateOnly.FromDateTime(DateTime.Now)), ct);
        TestContext.Current.SendDiagnosticMessage($"Wrote {path}");
    }

    // Only when AILAB_EVAL_CHUNKER is set, so the heading and fixed reports of the same day don't overwrite each other
    // (and an unset run keeps Wednesday's file names)
    private string ChunkerSuffix => fixture.Settings.Chunker is { } c ? $"-{c}" : "";

    // Each list's window before the merge, as in VectorSearchService.HybridWindow
    private const int MergeWindow = 20;

    private static List<(RetrievedChunk Chunk, SearchHit Hit)> Chunks(SearchResponse response) =>
        [.. response.Hits.Select(h => (new RetrievedChunk(h.Source, h.StartLine, h.EndLine),
            new SearchHit(new TextChunk(h.Chunker, h.Source, h.Position, h.Text, h.Heading, h.StartLine, h.EndLine), h.Distance)))];

    private static List<RetrievedChunk> Merged(List<(RetrievedChunk Chunk, SearchHit Hit)> vector, List<(RetrievedChunk Chunk, SearchHit Hit)> keyword, int k) =>
        [.. RankFusion.Merge([.. vector.Select(v => v.Hit)], [.. keyword.Select(v => v.Hit)], Top, k)
            .Select(h => new RetrievedChunk(h.Chunk.Source, h.Chunk.StartLine, h.Chunk.EndLine))];
}
