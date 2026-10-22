using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AiLab.Console.Extraction;

// Inside AiLab.Console.*, a bare "Console" would mean the AiLab.Console namespace
using Console = System.Console;

// Compares an extracted JobPosting with the hand-written golden answer (inputs/job-N.expected.json)
public static class JobScorer
{
    // job-1.txt -> job-1.expected.json, in the same folder
    public static string ExpectedPathFor(string inputPath) =>
        Path.Combine(Path.GetDirectoryName(inputPath) ?? "", Path.GetFileNameWithoutExtension(inputPath) + ".expected.json");

    // Same options as GetResponseAsync<T> (camelCase names, WorkMode as OnSite/Hybrid/Remote), so the golden file reads like the model's reply.
    // A golden file that doesn't fit JobPosting returns null with the reason, instead of crashing the chat.
    public static JobPosting? LoadExpected(string expectedPath, out string? error)
    {
        error = null;
        try
        {
            return JsonSerializer.Deserialize<JobPosting>(File.ReadAllText(expectedPath), AIJsonUtilities.DefaultOptions);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    // Scores without printing, so /eval can show one line per run; /extract prints the detail with PrintScore
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

    public static void PrintScore(JobPosting actual, JobPosting expected)
    {
        JobScore score = Score(actual, expected);
        foreach (FieldResult field in score.Fields)
        {
            Console.WriteLine(field.Match
                ? $"  ✅ {field.Name}"
                : $"  ❌ {field.Name}: got {Show(field.Actual)}, expected {Show(field.Expected)}");
        }
        Console.WriteLine($"Fields: {score.FieldsCorrect} of 5 exact");

        Console.WriteLine($"Skills recall: matched {score.SkillsMatched} of {score.SkillsTotal}");
        if (score.MissedSkills.Count > 0)
            Console.WriteLine($"  missed: {string.Join(", ", score.MissedSkills)}");
        if (score.ExtraSkills.Count > 0)
            Console.WriteLine($"  extra (not in golden): {string.Join(", ", score.ExtraSkills)}");
    }

    // Exact match after trimming, ignoring case; null only matches null
    private static FieldResult CompareField(string name, string? actual, string? expected) =>
        new(name, actual, expected, string.Equals(actual?.Trim(), expected?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string Show(string? value) => value is null ? "null" : $"\"{value}\"";
}

public record FieldResult(string Name, string? Actual, string? Expected, bool Match);

// One extraction compared with its golden answer
public record JobScore(List<FieldResult> Fields, int SkillsMatched, int SkillsTotal, List<string> MissedSkills, List<string> ExtraSkills)
{
    public int FieldsCorrect => Fields.Count(f => f.Match);

    public IEnumerable<string> WrongFields => Fields.Where(f => !f.Match).Select(f => f.Name);
}
