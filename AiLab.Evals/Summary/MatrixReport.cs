using System.Globalization;
using System.Text;

namespace AiLab.Evals.Summary;

// One table for the experiment matrix (week 5 Thursday): one row per execution (cell), from the same stored results as
// EvalSummary. Serving numbers only come from /ask's own call (the "Ask outcome" metadata), never from the judges:
// the judges are the cost of measuring, not of answering.
// - Cost per answer and latency are over the runs that reached the model (gate passed); gated runs make no call.
// - Latency = retrievalMs + generationMs. A cache hit answers in ~0 ms, so cache hits are counted and flagged.
// - Hand pass and abstain recall are scored by hand (step 5): left as "?".
public static class MatrixReport
{
    public const string OutOfDomainId = "E5";

    public sealed record Cell(string Execution, IReadOnlyList<RunRecord>? Runs);

    public static string Markdown(IReadOnlyList<Cell> cells, DateOnly date)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Experiment matrix, {date:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("Serving numbers come from `/ask`'s own call only (judges excluded), over the runs that reached the model (gate passed). " +
                      "Latency = retrieval + generation ms. Hand pass and abstain recall: scored by hand, `?` until then.");
        sb.AppendLine();
        sb.AppendLine("| Cell | Runs | Reached model | Gated | Hand pass /12 | Abstain recall /12 | E5 gated /3 | Groundedness | Recall@5 | `/ask` tokens in / out (avg) | Cost per answer | Cost per 1,000 | Latency median | Latency p95 | Cache hits |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        var notes = new List<string>();
        foreach (var cell in cells)
        {
            if (cell.Runs is null || cell.Runs.Count == 0)
            {
                sb.AppendLine($"| {cell.Execution} | missing | | | | | | | | | | | | | |");
                notes.Add($"- **{cell.Execution}: no stored results.** Run it (or check the execution name) before reading this table.");
                continue;
            }

            var row = Summarise(cell.Runs);
            sb.AppendLine($"| {cell.Execution} | {cell.Runs.Count} | {row.Reached} | {row.Gated} | ? | ? | {row.E5Gated}/{row.E5Runs} | " +
                          $"{F(EvalSummary.Avg(cell.Runs, "Groundedness"))} | {F(EvalSummary.AvgRecall(cell.Runs))} | " +
                          $"{F(row.AvgInput, "N0")} / {F(row.AvgOutput, "N0")} | {Money(row.CostPerAnswer, "0.000000")} | {Money(row.CostPerAnswer * 1000, "0.00")} | " +
                          $"{Ms(row.MedianMs)} | {Ms(row.P95Ms)} | {row.CacheHits} |");

            if (row.CacheHits > 0)
                notes.Add($"- **{cell.Execution}: {row.CacheHits} cached call(s)** (answers or judges; the results don't say which). A cached Haiku answer comes back in ~0 ms, so its latency may be understated. For real latency, re-run with a new `AILAB_EVAL_CACHE_KEY`.");
            if (row.UnpricedModels.Count > 0)
                notes.Add($"- **{cell.Execution}: no price for {string.Join(", ", row.UnpricedModels)}** (not in `ModelPrices`), so its cost shows `?`.");
        }

        if (notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Check before using");
            sb.AppendLine();
            foreach (string note in notes)
                sb.AppendLine(note);
        }

        return sb.ToString();
    }

    public sealed record Row(
        int Reached, int Gated, int E5Runs, int E5Gated, double? AvgInput, double? AvgOutput,
        decimal? CostPerAnswer, double? MedianMs, double? P95Ms, int CacheHits, IReadOnlyList<string> UnpricedModels);

    public static Row Summarise(IReadOnlyList<RunRecord> runs)
    {
        var reached = runs.Where(r => r.Outcome.GetValueOrDefault("gate") == "passed").ToList();
        var e5 = runs.Where(r => r.Id == OutOfDomainId).ToList();

        var input = reached.Select(r => Long(r, "inputTokens")).Where(v => v is not null).Select(v => (double)v!.Value).ToList();
        var output = reached.Select(r => Long(r, "outputTokens")).Where(v => v is not null).Select(v => (double)v!.Value).ToList();

        // Any run without a price makes the cell's cost unknown, not lower
        var prices = reached.Select(r => EvalSummary.PriceOf(r.Outcome.GetValueOrDefault("model", ""),
            Long(r, "inputTokens") ?? 0, Long(r, "outputTokens") ?? 0)).ToList();
        var unpriced = reached.Where((r, i) => prices[i] is null).Select(r => r.Outcome.GetValueOrDefault("model", "")).Distinct()
            .Select(m => m.Length == 0 ? "(no model)" : m).ToList();
        decimal? cost = reached.Count == 0 || unpriced.Count > 0 ? null : prices.Sum(p => p!.Value) / reached.Count;

        var latencies = reached.Select(r => Double(r, "retrievalMs") + Double(r, "generationMs"))
            .Where(v => v is not null).Select(v => v!.Value).ToList();

        return new Row(
            reached.Count, runs.Count - reached.Count, e5.Count, e5.Count(r => r.Outcome.GetValueOrDefault("gate") == "too_far"),
            input.Count == 0 ? null : input.Average(), output.Count == 0 ? null : output.Average(),
            cost, Median(latencies), Percentile(latencies, 95),
            runs.Sum(r => r.Turns.Count(t => t.CacheHit)), unpriced);
    }

    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return null;
        var sorted = values.Order().ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    // Nearest rank: the smallest value with at least p% of the values at or below it (always a measured value)
    public static double? Percentile(IReadOnlyList<double> values, int p)
    {
        if (values.Count == 0)
            return null;
        var sorted = values.Order().ToList();
        int rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }

    private static long? Long(RunRecord r, string key) =>
        long.TryParse(r.Outcome.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;

    private static double? Double(RunRecord r, string key) =>
        double.TryParse(r.Outcome.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    private static string F(double? v, string format = "0.##") => v?.ToString(format, CultureInfo.InvariantCulture) ?? "-";
    private static string Money(decimal? v, string format) => v is null ? "?" : "$" + v.Value.ToString(format, CultureInfo.InvariantCulture);
    private static string Ms(double? v) => v is null ? "-" : (v.Value / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " s";
}
