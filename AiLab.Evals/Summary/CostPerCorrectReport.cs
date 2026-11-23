using System.Globalization;
using System.Text;
using AiLab.Evals.Evaluators;

namespace AiLab.Evals.Summary;

// D2, measured: what one correct answer costs. Correct = GoldCheck (no judge) over the gold-checkable runs; cost = /ask's
// own call from the "Ask outcome" metadata, never chatDetails (those turns are the judges': the cost of measuring, not of
// answering). Gated runs made no call, so they add $0. No data never reads as 0: a missing execution is a "missing" row,
// 0 correct is "n/a", an unpriced model is "?".
public static class CostPerCorrectReport
{
    public sealed record Cell(string Execution, IReadOnlyList<RunRecord>? Runs);

    public sealed record Row(
        int Runs, int Checked, int Correct, int Run1Checked, int Run1Correct, int Reached,
        IReadOnlyList<string> Models, decimal? Cost, IReadOnlyList<string> UnpricedModels,
        double? MedianMs, double? P95Ms, int CacheHits)
    {
        public double? Accuracy => Checked == 0 ? null : (double)Correct / Checked;

        // null: unknown (an unpriced model) or nothing to divide by (0 correct)
        public decimal? CostPerCorrect => Cost is null || Correct == 0 ? null : Cost / Correct;

        // Per answer that reached the model, as week 5's "cost per 1,000" (but over the checked runs only)
        public decimal? CostPerThousand => Cost is null || Reached == 0 ? null : Cost / Reached * 1000;

        // Every call priced at $0: a local model, free per call (the hardware isn't counted)
        public bool Local => Cost == 0 && Reached > 0;
    }

    public static Row Summarise(IReadOnlyList<RunRecord> runs)
    {
        var verdicts = runs.Select(GoldCheckReport.Check).Where(v => v.Result.Verdict != GoldVerdict.NotChecked).ToList();
        var checkedRuns = verdicts.Select(v => v.Run).ToList();
        int correct = verdicts.Count(v => v.Result.Verdict == GoldVerdict.Correct);
        var run1 = verdicts.Where(v => v.Run.Iteration == "1").ToList();

        // Only runs that reached the model cost anything; a gated run is $0, not unpriced
        var reached = checkedRuns.Where(r => r.Outcome.GetValueOrDefault("gate") == "passed").ToList();
        var prices = reached.Select(r => EvalSummary.PriceOf(Model(r), MatrixReport.Long(r, "inputTokens") ?? 0,
            MatrixReport.Long(r, "outputTokens") ?? 0)).ToList();
        var unpriced = reached.Where((r, i) => prices[i] is null).Select(Model).Distinct()
            .Select(m => m.Length == 0 ? "(no model)" : m).ToList();
        decimal? cost = unpriced.Count > 0 ? null : prices.Sum(p => p!.Value);

        var latencies = reached.Select(r => MatrixReport.Double(r, "retrievalMs") + MatrixReport.Double(r, "generationMs"))
            .Where(v => v is not null).Select(v => v!.Value).ToList();

        return new Row(
            runs.Count, checkedRuns.Count, correct, run1.Count, run1.Count(v => v.Result.Verdict == GoldVerdict.Correct),
            reached.Count, [.. reached.Select(Model).Where(m => m.Length > 0).Distinct()], cost, unpriced,
            MatrixReport.Median(latencies), MatrixReport.Percentile(latencies, 95),
            runs.Sum(r => r.Turns.Count(t => t.CacheHit)));
    }

    public static string Markdown(IReadOnlyList<Cell> cells, DateOnly date)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Cost per correct answer, {date:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("## Measured");
        sb.AppendLine();
        sb.AppendLine("From stored eval results only. **Correct** = `GoldCheck` (facts from the row's `match` list plus a citation that holds a gold line; " +
                      "abstains by status or phrase; E5 by the gate) over the gold-checkable runs (16 rows × 3 runs = 48 per complete execution); " +
                      "judge-scored rows are left out. **Cost** = `/ask`'s own call (model and tokens from the `Ask outcome` metadata, judges excluded) " +
                      "over those runs; gated runs made no call and add $0. **Cost per correct** = cost ÷ correct. Cost per 1,000 and latency are over " +
                      "the checked runs that reached the model. Latency = retrieval + generation.");
        sb.AppendLine();
        sb.AppendLine("| Execution | Runs | Checked | Correct | Accuracy | Run 1 correct / checked | Reached model | `/ask` model | `/ask` cost (checked runs) | Cost per correct | Cost per 1,000 answers | Latency median | Latency p95 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        var notes = new List<string>();
        foreach (var cell in cells)
        {
            if (cell.Runs is null || cell.Runs.Count == 0)
            {
                sb.AppendLine($"| {cell.Execution} | missing | | | | | | | | | | | |");
                notes.Add($"- **{cell.Execution}: no stored results.** Nothing measured; not 0 correct and not $0.");
                continue;
            }

            var row = Summarise(cell.Runs);
            sb.AppendLine($"| {cell.Execution} | {row.Runs} | {row.Checked} | {row.Correct} | {Percent(row.Accuracy)} | " +
                          $"{row.Run1Correct} / {row.Run1Checked} | {row.Reached} | {Models(row)} | {Money(row, row.Cost, "0.000000")} | " +
                          $"{CostPerCorrect(row)} | {Money(row, row.CostPerThousand, "0.00")} | {Ms(row.MedianMs)} | {Ms(row.P95Ms)} |");

            if (row.UnpricedModels.Count > 0)
                notes.Add($"- **{cell.Execution}: no price for {string.Join(", ", row.UnpricedModels)}** (not in `ModelPrices`), so its cost shows `?`.");
            if (row.Correct == 0)
                notes.Add($"- **{cell.Execution}: 0 correct**, so cost per correct is n/a (there is no correct answer to divide by).");
            if (row.CacheHits > 0)
                notes.Add($"- **{cell.Execution}: {row.CacheHits} cached call(s)** (answers or judges; the results don't say which). Cost uses the stored token counts either way; latency may be understated.");
        }

        if (notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Check before using:");
            sb.AppendLine();
            foreach (string note in notes)
                sb.AppendLine(note);
        }

        return sb.ToString();
    }

    private static string Model(RunRecord r) => r.Outcome.GetValueOrDefault("model", "");

    private static string Models(Row row) => row.Models.Count == 0 ? "-" : string.Join(", ", row.Models.Select(m => $"`{m}`"));

    private static string CostPerCorrect(Row row) =>
        row.Correct == 0 ? "n/a"
        : row.Local ? "$0 (local)"
        : Money(row, row.CostPerCorrect, "0.000000");

    private static string Money(Row row, decimal? v, string format) =>
        v is null ? (row.Cost is null ? "?" : "n/a")
        : row.Local ? "$0 (local)"
        : "$" + v.Value.ToString(format, CultureInfo.InvariantCulture);

    private static string Percent(double? v) => v is null ? "n/a" : v.Value.ToString("0%", CultureInfo.InvariantCulture);

    private static string Ms(double? v) => v is null ? "-" : (v.Value / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " s";
}
