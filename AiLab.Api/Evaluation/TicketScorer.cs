using AiLab.Api.Grounding;
using AiLab.Domain.Enums;
using System.Text.RegularExpressions;

namespace AiLab.Api.Evaluation;

// Scores a reply against the version that explains the ticket (null: no change explains it)
public static class TicketScorer
{
    // From the model's claim (causeVersion) first, because its text often drops the version (experiments.md, section 9):
    // 1. causeVersion is the expected version → right
    // 2. causeVersion is any other version → invented
    // 3. no causeVersion and the cause says "unknown" → unknown
    // 4. no causeVersion and no "unknown" (older replies have no such field): the text check, in this order:
    //    the expected version → right; any other change-log version → invented; no version → unknown
    public static Verdict Score(string probableCause, string? causeVersion, string? expectedVersion, IEnumerable<string> allVersions)
    {
        if (Versions.Normalize(causeVersion) is { } claimed)
            return claimed == expectedVersion ? Verdict.Right : Verdict.Invented;

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
