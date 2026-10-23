using AiLab.Api.ChangeLog;
using AiLab.Api.Grounding;
using AiLab.Api.Tickets;
using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiLab.Api.Evaluation;

// Runs every test ticket N times through the same TicketAnalyzer as POST /analyze-ticket, scores each probable cause,
// and saves every reply under eval-runs/ so it can be re-scored later without new model calls.
public class TicketEval(TicketAnalyzer analyzer, IChangeLogRepository changeLog, IChatClient client, ILogger<TicketEval> logger)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions IndentedJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private record EvalRun(string Ticket, int Run, Verdict Verdict, bool Correct, bool Fenced, CauseStatus? Status, AnalysisDiagnostics? Diagnostics);

    public async Task<string> RunAsync(int runs, decimal? maxCost, CancellationToken cancellationToken)
    {
        IReadOnlyList<(string Letter, string Json)> tickets = TestTicketFiles.ReadAll();
        Dictionary<string, string?> expected = JsonSerializer.Deserialize<Dictionary<string, string?>>(
            File.ReadAllText(Path.Combine(TestTicketFiles.Folder, "expected.json")))!;
        List<string> allVersions = changeLog.GetComponents().SelectMany(changeLog.GetByComponent).Select(c => c.Version).ToList();

        string model = client.GetService<ChatClientMetadata>()?.DefaultModelId ?? "unknown-model";
        // In the repo's eval-runs/, next to the job eval's runs. dotnet run --project starts the API in AiLab.Api/,
        // so the repo root is found from the app's folder rather than the working directory.
        string runFolder = Path.Combine(RepoRoot(), "eval-runs", $"{DateTime.Now:yyyyMMdd-HHmmss}_tickets_{model}");
        Directory.CreateDirectory(runFolder);

        StringBuilder report = new();
        void Line(string text = "")
        {
            report.AppendLine(text);
            logger.LogInformation("{EvalLine}", text);
        }

        Line($"Ticket eval: {tickets.Count} tickets × {runs} run{(runs == 1 ? "" : "s")} on {model}, replies in {runFolder}/");
        Line();

        List<EvalRun> results = [];
        string? stoppedBecause = null;

        foreach ((string letter, string json) in tickets)
        {
            string? expectedVersion = expected.GetValueOrDefault(letter);
            AnalyzeTicketRequest request = JsonSerializer.Deserialize<AnalyzeTicketRequest>(json, WebJson)!;

            for (int run = 1; run <= runs && stoppedBecause is null; run++)
            {
                TicketRun? reply = null;
                string? error = null;
                try
                {
                    reply = await analyzer.RunAsync(request, cancellationToken);
                }
                catch (ModelCallException ex)
                {
                    error = $"{ex.Code}: {ex.Message}";
                }

                Verdict verdict = reply?.Analysis is { } analysis
                    ? TicketScorer.Score(analysis.ProbableCause, expectedVersion, allVersions)
                    : Verdict.Failed;
                bool correct = TicketScorer.IsCorrect(verdict, expectedVersion);
                // From the raw text, so a reply that didn't parse still shows whether it was fenced
                bool fenced = reply?.RawText.TrimStart().StartsWith("```") == true;

                EvalRun result = new(letter, run, verdict, correct, fenced, reply?.Verification?.CauseStatus, reply?.Diagnostics);
                results.Add(result);
                Line(FormatRun(result, reply?.Analysis?.ProbableCause ?? error ?? $"no analysis ({reply?.Outcome})"));

                await File.WriteAllTextAsync(Path.Combine(runFolder, $"ticket-{letter}-run{run}.json"), JsonSerializer.Serialize(new
                {
                    ticket = letter,
                    run,
                    expectedVersion,
                    verdict = verdict.ToString().ToLowerInvariant(),
                    correct,
                    fenced,
                    error,
                    outcome = reply?.Outcome,
                    rawText = reply?.RawText,
                    analysis = reply?.Analysis,
                    verification = reply?.Verification,
                    diagnostics = reply?.Diagnostics,
                }, IndentedJson), cancellationToken);

                decimal spent = results.Sum(r => r.Diagnostics?.Cost ?? 0);
                if (maxCost is { } limit && spent > limit)
                    stoppedBecause = $"cost ${spent:0.000000} passed maxCost ${limit:0.000000}";
            }
        }

        if (stoppedBecause is not null)
        {
            Line();
            Line($"⚠ Stopped early ({stoppedBecause}) after {results.Count} of {tickets.Count * runs} runs.");
        }

        Line();
        Line("Per ticket:");
        foreach (IGrouping<string, EvalRun> ticket in results.GroupBy(r => r.Ticket))
            Line($"  {ticket.Key} (expects {expected.GetValueOrDefault(ticket.Key) ?? "unknown"}): {FormatTotals(ticket.ToList())}");
        Line($"Overall: {FormatTotals(results)}");

        await File.WriteAllTextAsync(Path.Combine(runFolder, "report.txt"), report.ToString(), cancellationToken);
        return report.ToString();
    }

    // The first folder above the app that holds AiLab.slnx; the working directory if there is none
    private static string RepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AiLab.slnx")))
                return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    // A run 1  right     ✓  grounded    GetRecentChanges(billing)  2 trips  1015/114 tok  $0.000000  not fenced  | v4.18.0: Proration now…
    private static string FormatRun(EvalRun r, string cause)
    {
        AnalysisDiagnostics? d = r.Diagnostics;
        string tools = d is null || d.ToolCalls.Count == 0
            ? "no tool call"
            : string.Join(", ", d.ToolCalls.Select(c => $"{c.Name}({string.Join(", ", c.Arguments?.Values ?? [])})"));
        string oneLine = cause.ReplaceLineEndings(" ");
        if (oneLine.Length > 110)
            oneLine = oneLine[..110] + "…";

        return string.Create(CultureInfo.InvariantCulture,
            $"{r.Ticket} run {r.Run}  {r.Verdict.ToString().ToLowerInvariant(),-8}  {(r.Correct ? "✓" : "✗")}  {r.Status?.ToString().ToLowerInvariant() ?? "-",-10}  {tools}  " +
            $"{d?.RoundTrips ?? 0} trips  {d?.InputTokens ?? 0}/{d?.OutputTokens ?? 0} tok  ${d?.Cost ?? 0:0.000000}  " +
            $"{(r.Fenced ? "fenced" : "not fenced")}  | {oneLine}");
    }

    // right 2, unknown 1, invented 0, failed 0 | grounded 2, unknown 1, unverified 0 → correct 2/3, fenced 0/3, tokens 3000/400, cost $0.001500
    private static string FormatTotals(List<EvalRun> runs) => string.Create(CultureInfo.InvariantCulture,
        $"right {runs.Count(r => r.Verdict == Verdict.Right)}, unknown {runs.Count(r => r.Verdict == Verdict.Unknown)}, " +
        $"invented {runs.Count(r => r.Verdict == Verdict.Invented)}, failed {runs.Count(r => r.Verdict == Verdict.Failed)} " +
        $"| grounded {runs.Count(r => r.Status == CauseStatus.Grounded)}, unknown {runs.Count(r => r.Status == CauseStatus.Unknown)}, " +
        $"unverified {runs.Count(r => r.Status == CauseStatus.Unverified)} " +
        $"→ correct {runs.Count(r => r.Correct)}/{runs.Count}, fenced {runs.Count(r => r.Fenced)}/{runs.Count}, " +
        $"tokens {runs.Sum(r => r.Diagnostics?.InputTokens ?? 0)}/{runs.Sum(r => r.Diagnostics?.OutputTokens ?? 0)}, " +
        $"cost ${runs.Sum(r => r.Diagnostics?.Cost ?? 0):0.000000}");
}
