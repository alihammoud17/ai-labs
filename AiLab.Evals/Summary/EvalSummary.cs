using System.Globalization;
using System.Text;
using System.Text.Json;
using AiLab.Application.Services;

namespace AiLab.Evals.Summary;

// Reads the stored results of one execution (eval-runs/meai/results/<execution>/<scenario>/<iteration>.json) and
// writes a markdown summary: one line per run, one per question, averages per category, totals. Saves copying
// 90 rows by hand from the HTML report.
public static class EvalSummary
{
    public static readonly string[] Judged = ["Groundedness", "Relevance", "Retrieval"];

    public static IReadOnlyList<RunRecord> Load(string executionFolder) =>
        [.. Directory.EnumerateFiles(executionFolder, "*.json", SearchOption.AllDirectories)
            .Select(path => Parse(File.ReadAllText(path)))
            .OrderBy(r => r.Order).ThenBy(r => r.Iteration)];

    public static RunRecord Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string scenario = root.GetProperty("scenarioName").GetString()!;
        string[] parts = scenario.Split('.');
        string id = parts[^1];
        string category = parts[^2];
        var tags = root.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : [];
        string expected = tags.FirstOrDefault(x => x.StartsWith("expected:"))?["expected:".Length..] ?? "";

        var metrics = root.GetProperty("evaluationResult").GetProperty("metrics");
        double? Num(string name) =>
            metrics.TryGetProperty(name, out var m) && m.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDouble() : null;
        bool? Failed(string name) =>
            metrics.TryGetProperty(name, out var m) && m.TryGetProperty("interpretation", out var i) && i.ValueKind == JsonValueKind.Object
            && i.TryGetProperty("failed", out var f) && f.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? f.GetBoolean() : null;

        var outcome = new Dictionary<string, string>();
        string status = "";
        if (metrics.TryGetProperty("Ask outcome", out var o))
        {
            status = o.TryGetProperty("value", out var sv) ? sv.GetString() ?? "" : "";
            if (o.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object)
                foreach (var p in md.EnumerateObject())
                    outcome[p.Name] = p.Value.GetString() ?? "";
        }

        var turns = new List<Turn>();
        if (root.TryGetProperty("chatDetails", out var cd) && cd.ValueKind == JsonValueKind.Object
            && cd.TryGetProperty("turnDetails", out var td))
            foreach (var turn in td.EnumerateArray())
            {
                var usage = turn.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
                long Tokens(string name) => usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var n)
                    && n.ValueKind == JsonValueKind.Number ? n.GetInt64() : 0;
                turns.Add(new Turn(
                    turn.TryGetProperty("model", out var mm) ? mm.GetString() ?? "" : "",
                    Tokens("inputTokenCount"), Tokens("outputTokenCount"),
                    turn.TryGetProperty("cacheHit", out var ch) && ch.ValueKind == JsonValueKind.True,
                    turn.TryGetProperty("latency", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetDouble() : 0));
            }

        return new RunRecord(id, category, expected, root.GetProperty("iterationName").GetString() ?? "",
            status, outcome,
            Num("Recall@5"), Failed("Recall@5"), Num("First hit rank"),
            Judged.ToDictionary(n => n, Num), Judged.ToDictionary(n => n, Failed), turns, Order(id));
    }

    public static string Markdown(string execution, IReadOnlyList<RunRecord> runs)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        string F(double? v, string format = "0.##") => v?.ToString(format, inv) ?? "-";
        string Money(decimal v) => "$" + v.ToString("0.0000", inv);

        int turns = runs.Sum(r => r.Turns.Count), hits = runs.Sum(r => r.Turns.Count(t => t.CacheHit));
        decimal billed = runs.Sum(r => r.Turns.Where(t => !t.CacheHit).Sum(t => Cost(t)));
        decimal full = runs.Sum(r => r.Turns.Sum(t => Cost(t)));

        sb.AppendLine($"# Eval summary: {execution}");
        sb.AppendLine();
        sb.AppendLine($"- Runs: {runs.Count} ({runs.Select(r => r.Id).Distinct().Count()} questions)");
        sb.AppendLine($"- Model calls through the cached client: {turns}, cache hits: {hits}");
        sb.AppendLine($"- Tokens through the cached client: {runs.Sum(r => r.Turns.Sum(t => t.InputTokens)):N0} in / {runs.Sum(r => r.Turns.Sum(t => t.OutputTokens)):N0} out");
        sb.AppendLine($"- Cost of the calls made in this run (cache hits are free): {Money(billed)}; the same calls without the cache: {Money(full)}");
        sb.AppendLine($"- Sum of call latencies: {runs.Sum(r => r.Turns.Sum(t => t.Latency)):0} s (judges run in parallel, so wall time is shorter)");
        sb.AppendLine();

        sb.AppendLine("## Averages per category (answered runs for the judges; answerable rows for recall)");
        sb.AppendLine();
        sb.AppendLine("| Category | Runs | Groundedness | Relevance | Retrieval | Recall@5 | Judge failed (< 4) |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var group in runs.GroupBy(r => r.Category).OrderBy(g => g.Min(r => r.Order)))
            sb.AppendLine($"| {group.Key} | {group.Count()} | {F(Avg(group, "Groundedness"))} | {F(Avg(group, "Relevance"))} | {F(Avg(group, "Retrieval"))} | {F(AvgRecall(group))} | {FailedCount(group)} |");
        sb.AppendLine($"| **All** | {runs.Count} | {F(Avg(runs, "Groundedness"))} | {F(Avg(runs, "Relevance"))} | {F(Avg(runs, "Retrieval"))} | {F(AvgRecall(runs))} | {FailedCount(runs)} |");
        sb.AppendLine();

        sb.AppendLine("## Per question (mean over the runs; statuses in run order)");
        sb.AppendLine();
        sb.AppendLine("| ID | Expected | Statuses | Groundedness | Relevance | Retrieval | Recall@5 | First hit | Best distance |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var q in runs.GroupBy(r => r.Id).OrderBy(g => g.First().Order))
        {
            var first = q.First();
            sb.AppendLine($"| {q.Key} | {first.Expected} | {string.Join(", ", q.Select(r => r.Status))} | {F(Avg(q, "Groundedness"))} | {F(Avg(q, "Relevance"))} | {F(Avg(q, "Retrieval"))} | {F(first.Recall)} | {F(first.FirstHit, "0")} | {first.Outcome.GetValueOrDefault("bestDistance", "-")} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Every run");
        sb.AppendLine();
        sb.AppendLine("| ID | Run | Status | Gate | G | R | Ret | Recall@5 | /ask model | /ask tokens in/out | Gen ms | Calls (hits) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in runs)
            sb.AppendLine($"| {r.Id} | {r.Iteration} | {r.Status} | {r.Outcome.GetValueOrDefault("gate", "-")} | {F(r.Scores["Groundedness"])} | {F(r.Scores["Relevance"])} | {F(r.Scores["Retrieval"])} | {F(r.Recall)} | {r.Outcome.GetValueOrDefault("model", "")} | {r.Outcome.GetValueOrDefault("inputTokens", "")}/{r.Outcome.GetValueOrDefault("outputTokens", "")} | {r.Outcome.GetValueOrDefault("generationMs", "")} | {r.Turns.Count} ({r.Turns.Count(t => t.CacheHit)}) |");

        return sb.ToString();
    }

    public static double? Avg(IEnumerable<RunRecord> runs, string metric)
    {
        var values = runs.Select(r => r.Scores[metric]).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Average();
    }

    // Recall is per question, the same on every run (retrieval doesn't depend on the model): one value per question
    public static double? AvgRecall(IEnumerable<RunRecord> runs)
    {
        var values = runs.GroupBy(r => r.Id).Select(g => g.First().Recall).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Average();
    }

    private static int FailedCount(IEnumerable<RunRecord> runs) =>
        runs.Sum(r => Judged.Count(n => r.JudgeFailed[n] == true && r.Scores[n] is not null));

    // Haiku may report a dated model id: match the table's names as prefixes
    public static decimal Cost(Turn turn)
    {
        string model = turn.Model;
        foreach (string known in new[] { "claude-haiku-5-5", "claude-sonnet-5-5", "llama3.2" })
            if (model.StartsWith(known, StringComparison.Ordinal))
                return ModelPrices.Cost(known, turn.InputTokens, turn.OutputTokens) ?? 0;
        return 0;
    }

    // The row order of ask-set-v1.json, so tables follow the categories
    private static int Order(string id)
    {
        var rows = AskSet.AskSetData.Load().Rows;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Id == id)
                return i;
        return int.MaxValue;
    }
}

public sealed record Turn(string Model, long InputTokens, long OutputTokens, bool CacheHit, double Latency);

public sealed record RunRecord(
    string Id, string Category, string Expected, string Iteration, string Status, IReadOnlyDictionary<string, string> Outcome,
    double? Recall, bool? RecallFailed, double? FirstHit,
    IReadOnlyDictionary<string, double?> Scores, IReadOnlyDictionary<string, bool?> JudgeFailed,
    IReadOnlyList<Turn> Turns, int Order);
