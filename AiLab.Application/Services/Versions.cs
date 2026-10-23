using System.Text.RegularExpressions;

namespace AiLab.Application.Services;

// Change versions as the change log writes them: "4.18.0", also "v4.18.0" in a model's text
public static class Versions
{
    // "4.18.0" but not "14.18.0" or "4.18.01"; "v4.18.0." at the end of a sentence still counts (same rule as TicketScorer)
    private static readonly Regex InText = new(@"(?<![\d.])v?(?<version>\d+\.\d+\.\d+)(?!\.?\d)", RegexOptions.IgnoreCase);
    private static readonly Regex Exact = new(@"^\d+\.\d+\.\d+$");

    // Every version mentioned in a text, without the "v", in order of appearance
    public static IReadOnlyList<string> FindAll(string text) =>
        InText.Matches(text).Select(m => m.Groups["version"].Value).Distinct().ToList();

    // The model's causeVersion as a plain version, or null when it isn't one ("unknown", "n/a", "")
    public static string? Normalize(string? version)
    {
        string? trimmed = version?.Trim().TrimStart('v', 'V');
        return trimmed is not null && Exact.IsMatch(trimmed) ? trimmed : null;
    }
}
