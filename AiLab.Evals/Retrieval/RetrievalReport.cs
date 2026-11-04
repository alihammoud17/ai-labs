using System.Globalization;
using System.Text;
using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using AiLab.Evals.Summary;

namespace AiLab.Evals.Retrieval;

// One question's retrieval: its first-hit rank in the top 10 and the chunks returned, best first.
// Tuesday = the first-hit rank from Tuesday's MEAI baseline (top 5), when its results are on disk.
public sealed record RetrievalResult(AskSetRow Row, int? Rank, IReadOnlyList<RetrievedChunk> Ranked, int? Tuesday = null, bool HasTuesday = false);

// The markdown for eval-runs/retrieval/<date>-<mode>.md: hit@k and MRR overall, per category, per question.
// Non-English rows (H5) are kept out of the totals and reported on their own line, as in the eval plan.
public static class RetrievalReport
{
    public static readonly int[] Ks = [1, 3, 5, 10];
    public const string NonEnglish = "Non-English";

    public static string Build(string mode, string chunker, int top, DateOnly date, IReadOnlyList<RetrievalResult> results)
    {
        var english = results.Where(r => r.Row.Category != NonEnglish).ToList();
        var other = results.Where(r => r.Row.Category == NonEnglish).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# Retrieval: {mode}, {date:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine($"`GET /search?top={top}&chunker={chunker}&mode={mode}` for each answerable row of ask-set-v1. No chat model, $0.");
        sb.AppendLine("A question is found at the rank of its first gold chunk (for `all` rows: the rank where the last needed source first appears).");
        sb.AppendLine();

        sb.AppendLine("## Overall");
        sb.AppendLine();
        sb.AppendLine($"| Rows | {string.Join(" | ", Ks.Select(k => $"hit@{k}"))} | MRR |");
        sb.AppendLine($"|---|{string.Concat(Ks.Select(_ => "---|"))}---|");
        sb.AppendLine(Line($"English answerable ({english.Count})", english));
        foreach (var r in other)
            sb.AppendLine(Line($"{r.Row.Id} ({r.Row.Category}, apart)", [r]));
        sb.AppendLine();

        sb.AppendLine("## Per category (English)");
        sb.AppendLine();
        sb.AppendLine($"| Category | {string.Join(" | ", Ks.Select(k => $"hit@{k}"))} | MRR |");
        sb.AppendLine($"|---|{string.Concat(Ks.Select(_ => "---|"))}---|");
        foreach (var group in english.GroupBy(r => r.Row.Category))
            sb.AppendLine(Line($"{group.Key} ({group.Count()})", [.. group]));
        sb.AppendLine();

        bool tuesday = results.Any(r => r.HasTuesday);
        sb.AppendLine("## Per question");
        sb.AppendLine();
        if (tuesday)
        {
            int same = results.Count(r => r.HasTuesday && SameAsTuesday(r));
            sb.AppendLine($"Tuesday = first-hit rank in Tuesday's MEAI baseline (vector, top 5; `-` = not in the top 5). Tuesday counted the first chunk of any gold source, so on `all` rows it can be lower than First hit here. Same retrieval as Tuesday: {same} of {results.Count(r => r.HasTuesday)}.");
            sb.AppendLine();
        }
        sb.AppendLine(tuesday ? "| ID | Category | Mode | First hit | Tuesday | Top 5 |" : "| ID | Category | Mode | First hit | Top 5 |");
        sb.AppendLine(tuesday ? "|---|---|---|---|---|---|" : "|---|---|---|---|---|");
        foreach (var r in results)
        {
            string top5 = string.Join(", ", r.Ranked.Take(5).Select(c =>
            {
                string text = $"{c.Source}:{c.StartLine}-{c.EndLine}";
                return r.Row.Gold.Any(g => RecallAtKEvaluator.IsHit(g, c)) ? $"**{text}**" : text;
            }));
            string rank = r.Rank?.ToString(CultureInfo.InvariantCulture) ?? "miss";
            sb.AppendLine(tuesday
                ? $"| {r.Row.Id} | {r.Row.Category} | {r.Row.GoldMode} | {rank} | {(r.HasTuesday ? r.Tuesday?.ToString(CultureInfo.InvariantCulture) ?? "-" : "?")} | {top5} |"
                : $"| {r.Row.Id} | {r.Row.Category} | {r.Row.GoldMode} | {rank} | {top5} |");
        }
        sb.AppendLine();
        sb.AppendLine("Gold chunks in **bold**.");
        return sb.ToString();
    }

    // Tuesday's "First hit rank" (RecallAtKEvaluator) is the first chunk hitting ANY gold source, even on `all` rows,
    // and stops at 5 (Recall@5). So compare like with like: the any-source rank, capped at 5.
    public static bool SameAsTuesday(RetrievalResult r)
    {
        int? anyRank = RetrievalMetrics.FirstHitRank(r.Row with { GoldMode = "any" }, r.Ranked);
        return r.Tuesday == (anyRank is <= 5 ? anyRank : null);
    }

    // First-hit ranks per question id from a stored MEAI execution (iteration 1; retrieval is the same on every run)
    public static IReadOnlyDictionary<string, int?> TuesdayRanks(string executionFolder) =>
        Directory.Exists(executionFolder)
            ? EvalSummary.Load(executionFolder)
                .GroupBy(r => r.Id)
                .ToDictionary(g => g.Key, g => g.First().FirstHit is double d ? (int?)(int)d : null)
            : new Dictionary<string, int?>();

    // Modes side by side: totals over the English rows, H5 apart, then every question whose first hit differs between modes
    public static string Comparison(IReadOnlyList<AskSetRow> rows, IReadOnlyDictionary<string, Dictionary<string, int?>> ranks,
        IReadOnlyList<string> columns, DateOnly date)
    {
        var english = rows.Where(r => r.Category != NonEnglish).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"# Retrieval: modes compared, {date:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("Heading chunker, top 10. Hybrid = the top 20 of vector and keyword merged by RRF (k = 60 unless named).");
        sb.AppendLine();
        sb.AppendLine($"| Mode | {string.Join(" | ", Ks.Select(k => $"hit@{k}"))} | MRR | Misses at 5 |");
        sb.AppendLine($"|---|{string.Concat(Ks.Select(_ => "---|"))}---|---|");
        foreach (string column in columns)
        {
            var r = english.Select(row => ranks[row.Id][column]).ToList();
            sb.AppendLine($"| {column} | {string.Join(" | ", Ks.Select(k => F(RetrievalMetrics.HitRate(r, k))))} | {F(RetrievalMetrics.Mrr(r))} | {r.Count(x => x is null or > 5)} |");
        }
        sb.AppendLine();
        sb.AppendLine($"English rows: {english.Count}. Non-English (apart): " +
            string.Join(", ", rows.Where(r => r.Category == NonEnglish).Select(r => $"{r.Id} " + string.Join(", ", columns.Select(c => $"{c} {R(ranks[r.Id][c])}")))));
        sb.AppendLine();

        sb.AppendLine("## First hit per question (rows where the modes differ)");
        sb.AppendLine();
        sb.AppendLine($"| ID | Category | {string.Join(" | ", columns)} |");
        sb.AppendLine($"|---|---|{string.Concat(columns.Select(_ => "---|"))}");
        foreach (var row in rows.Where(r => columns.Select(c => ranks[r.Id][c]).Distinct().Count() > 1))
            sb.AppendLine($"| {row.Id} | {row.Category} | {string.Join(" | ", columns.Select(c => R(ranks[row.Id][c])))} |");
        return sb.ToString();
    }

    private static string R(int? rank) => rank?.ToString(CultureInfo.InvariantCulture) ?? "miss";

    private static string Line(string label, IReadOnlyList<RetrievalResult> rows)
    {
        var ranks = rows.Select(r => r.Rank).ToList();
        return $"| {label} | {string.Join(" | ", Ks.Select(k => F(RetrievalMetrics.HitRate(ranks, k))))} | {F(RetrievalMetrics.Mrr(ranks))} |";
    }

    private static string F(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
}
