using System.Text.RegularExpressions;

namespace AiLab.Api.Evaluation;

public enum Verdict { Right, Unknown, Invented, Failed }

// Scores a probable cause against the version that explains the ticket (null: no change explains it)
public static class TicketScorer
{
    // In this order. "unknown" comes first because a cause can list versions only to rule them out
    // ("Unknown. The auth log has v4.17.1 … and v4.16.0 …, neither explains it"); that isn't an invention.
    public static Verdict Score(string probableCause, string? expectedVersion, IEnumerable<string> allVersions)
    {
        if (Regex.IsMatch(probableCause, @"\bunknown\b", RegexOptions.IgnoreCase))
            return Verdict.Unknown;

        if (expectedVersion is not null && MentionsVersion(probableCause, expectedVersion))
            return Verdict.Right;

        if (allVersions.Any(v => v != expectedVersion && MentionsVersion(probableCause, v)))
            return Verdict.Invented;

        return Verdict.Unknown;
    }

    // Right when a version is expected, unknown when none is
    public static bool IsCorrect(Verdict verdict, string? expectedVersion) =>
        verdict == (expectedVersion is null ? Verdict.Unknown : Verdict.Right);

    // "4.18.0" but not "14.18.0" or "4.18.01"; "v4.18.0." at the end of a sentence still counts
    private static bool MentionsVersion(string text, string version) =>
        Regex.IsMatch(text, $@"(?<![\d.]){Regex.Escape(version)}(?!\.?\d)");
}
