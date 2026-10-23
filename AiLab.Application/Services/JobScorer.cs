using AiLab.Domain.DTOs;
using AiLab.Domain.Outputs;

namespace AiLab.Application.Services;

// Compares an extracted JobPosting with the hand-written golden answer (inputs/job-N.expected.json)
public static class JobScorer
{
    // job-1.txt -> job-1.expected.json, in the same folder
    public static string ExpectedPathFor(string inputPath) =>
        Path.Combine(Path.GetDirectoryName(inputPath) ?? "", Path.GetFileNameWithoutExtension(inputPath) + ".expected.json");

    // Scores without printing, so /eval can show one line per run; /extract prints the detail
    public static JobScore Score(JobPosting actual, JobPosting expected)
    {
        List<FieldResult> fields =
        [
            CompareField("title", actual.Title, expected.Title),
            CompareField("company", actual.Company, expected.Company),
            CompareField("location", actual.Location, expected.Location),
            CompareField("workMode", actual.WorkMode?.ToString(), expected.WorkMode?.ToString()),
            CompareField("yearsOfExperience", actual.YearsOfExperience?.ToString(), expected.YearsOfExperience?.ToString()),
        ];

        // Recall: how many golden skills the model found. Strict names (ignoring case), so it's a lower bound;
        // the misses are listed so a human can accept equivalent wording.
        HashSet<string> actualSkills = new(actual.RequiredSkills.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        HashSet<string> expectedSkills = new(expected.RequiredSkills.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

        List<string> missed = expected.RequiredSkills.Where(s => !actualSkills.Contains(s.Trim())).ToList();
        List<string> extra = actual.RequiredSkills.Where(s => !expectedSkills.Contains(s.Trim())).ToList();

        return new JobScore(fields, expected.RequiredSkills.Count - missed.Count, expected.RequiredSkills.Count, missed, extra);
    }

    // Exact match after trimming, ignoring case; null only matches null
    private static FieldResult CompareField(string name, string? actual, string? expected) =>
        new(name, actual, expected, string.Equals(actual?.Trim(), expected?.Trim(), StringComparison.OrdinalIgnoreCase));
}
