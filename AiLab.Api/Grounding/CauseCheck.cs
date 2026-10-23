using AiLab.Api.ChangeLog;
using AiLab.Api.Tickets;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace AiLab.Api.Grounding;

public enum CauseStatus { Grounded, Unknown, Unverified }

[Description("What the server checked about the probable cause. Written by the server, never by the model.")]
public record CauseVerification(
    [property: Description("grounded: cites only versions the tool returned in this request (real evidence, not proof it explains the problem). unknown: the model says the cause is unknown. unverified: cites a version the tool didn't return, or names a cause without a version.")]
    CauseStatus CauseStatus,

    [property: Description("One sentence on why the status was given.")]
    string Reason,

    [property: Description("The change-log entries the cause relies on, copied from what the tool returned. Empty unless the status is grounded.")]
    IReadOnlyList<Evidence> Evidence,

    [property: Description("True if the model called GetRecentChanges in this request.")]
    bool ToolCalled,

    [property: Description("True if a GetRecentChanges call failed in this request.")]
    bool ToolFailed);

[Description("One change-log entry, as GetRecentChanges returned it.")]
public record Evidence(string Component, string Version, DateTimeOffset ChangedAt, string Description)
{
    public static Evidence From(ChangeEntry c) => new(c.Component, c.Version, c.ChangedAt, c.Description);
}

// Checks the model's probable cause against what the tool returned in the same request. First matching rule wins.
public static class CauseCheck
{
    private static readonly Regex SaysUnknown = new(@"\bunknown\b", RegexOptions.IgnoreCase);

    // Words in a row that must match a change's description before the reason says "its text matches"
    private const int TextMatchWords = 6;

    public static CauseVerification Evaluate(TicketAnalysis analysis, ToolEvidence tool)
    {
        string cause = analysis.ProbableCause ?? "";
        bool saysUnknown = SaysUnknown.IsMatch(cause);
        IReadOnlyList<string> mentioned = Versions.FindAll(cause);

        // The model's claim: its causeVersion field, else the versions in a cause that doesn't say unknown
        List<string> claimed = Versions.Normalize(analysis.CauseVersion) is { } version ? [version]
            : saysUnknown ? []
            : [.. mentioned];

        HashSet<string> returned = tool.Returned.Select(c => c.Version).ToHashSet();

        CauseVerification Result(CauseStatus status, string reason, IEnumerable<ChangeEntry>? evidence = null) =>
            new(status, reason, (evidence ?? []).Select(Evidence.From).ToList(), tool.ToolCalled, tool.ToolFailed);

        // 1. Any version the tool didn't return, even one being ruled out, is a hallucination
        List<string> notReturned = claimed.Concat(mentioned).Distinct().Where(v => !returned.Contains(v)).ToList();
        if (notReturned.Count > 0)
            return Result(CauseStatus.Unverified, $"Cites {string.Join(", ", notReturned)}, {WhyNotReturned(tool)}.");

        // 2. Unknown and no claim
        if (claimed.Count == 0 && saysUnknown)
            return Result(CauseStatus.Unknown, "The cause is given as unknown.");

        // 3. Unknown, but a version is claimed as the cause anyway
        if (claimed.Count > 0 && saysUnknown)
            return Result(CauseStatus.Unverified, $"The cause says unknown but also names {string.Join(", ", claimed)} as the cause.");

        // 4. Every claimed version was returned (rule 1 checked that)
        if (claimed.Count > 0)
        {
            List<ChangeEntry> evidence = tool.Returned.Where(c => claimed.Contains(c.Version)).ToList();
            string components = string.Join(", ", evidence.Select(c => c.Component).Distinct());
            return Result(CauseStatus.Grounded, $"Cites {string.Join(", ", claimed)}, which GetRecentChanges returned for {components} in this request.", evidence);
        }

        // 5. A cause with no version: Llama's habit of copying a change's text and dropping its version.
        // A text match only says where the words came from, not that the change explains the ticket, so it stays unverified.
        string reason = tool.ToolCalled
            ? "Names a cause without citing a change version"
            : "Names a cause without citing a change version, and GetRecentChanges wasn't called";
        if (TextMatch(cause, tool.Returned) is { } match)
            reason += $" (its text matches {match})";
        return Result(CauseStatus.Unverified, reason + ".");
    }

    private static string WhyNotReturned(ToolEvidence tool) =>
        !tool.ToolCalled ? "but GetRecentChanges wasn't called in this request"
        : tool.ToolFailed && tool.Returned.Count == 0 ? "but GetRecentChanges failed in this request"
        : "which GetRecentChanges didn't return in this request";

    // The version of the first returned change whose description shares a run of words with the cause
    private static string? TextMatch(string cause, IReadOnlyList<ChangeEntry> returned)
    {
        List<string> causeWords = Words(cause);
        foreach (ChangeEntry change in returned)
        {
            List<string> words = Words(change.Description);
            int n = Math.Min(TextMatchWords, words.Count);
            if (n < 3)
                continue;

            HashSet<string> runs = Runs(words, n);
            if (Runs(causeWords, n).Overlaps(runs))
                return change.Version;
        }
        return null;
    }

    private static List<string> Words(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}']+").Select(m => m.Value).ToList();

    private static HashSet<string> Runs(List<string> words, int n) =>
        Enumerable.Range(0, Math.Max(0, words.Count - n + 1)).Select(i => string.Join(' ', words.Skip(i).Take(n))).ToHashSet();
}
