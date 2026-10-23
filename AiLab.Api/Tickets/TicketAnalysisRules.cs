using AiLab.Api.Grounding;

namespace AiLab.Api.Tickets;

// Checks the meaning of a reply that parsed: valid JSON with an empty summary is still no analysis.
// Small slips are fixed instead of failed (blank test cases, more than five, a causeVersion that isn't a version).
public static class TicketAnalysisRules
{
    public const int MaxTestCases = 5;

    // Returns what's wrong (empty: usable) and the cleaned analysis
    public static IReadOnlyList<string> Check(TicketAnalysis analysis, out TicketAnalysis cleaned)
    {
        List<string> testCases = (analysis.TestCases ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Take(MaxTestCases).ToList();
        cleaned = analysis with
        {
            TestCases = testCases,
            // "unknown", "n/a" or "" in causeVersion means no claim
            CauseVersion = Versions.Normalize(analysis.CauseVersion),
        };

        List<string> problems = [];
        if (string.IsNullOrWhiteSpace(analysis.Summary))
            problems.Add("summary is empty");
        if (string.IsNullOrWhiteSpace(analysis.ProbableCause))
            problems.Add("probableCause is empty");
        if (testCases.Count == 0)
            problems.Add("testCases is empty");
        return problems;
    }
}
