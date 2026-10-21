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

    public static JobPosting? LoadExpected(string expectedPath) =>
        // Same options as GetResponseAsync<T> (camelCase names), so the golden file reads like the model's reply
        JsonSerializer.Deserialize<JobPosting>(File.ReadAllText(expectedPath), AIJsonUtilities.DefaultOptions);

    public static void PrintScore(JobPosting actual, JobPosting expected)
    {
        int fieldsMatched = 0;
        fieldsMatched += PrintField("title", actual.Title, expected.Title);
        fieldsMatched += PrintField("company", actual.Company, expected.Company);
        fieldsMatched += PrintField("location", actual.Location, expected.Location);
        fieldsMatched += PrintField("workMode", actual.WorkMode?.ToString(), expected.WorkMode?.ToString());
        fieldsMatched += PrintField("yearsOfExperience", actual.YearsOfExperience?.ToString(), expected.YearsOfExperience?.ToString());
        Console.WriteLine($"Fields: {fieldsMatched} of 5 exact");

        // Recall: how many golden skills the model found. Strict names (ignoring case), so it's a lower bound;
        // the misses are listed so a human can accept equivalent wording.
        HashSet<string> actualSkills = new(actual.RequiredSkills.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        HashSet<string> expectedSkills = new(expected.RequiredSkills.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

        List<string> missed = expected.RequiredSkills.Where(s => !actualSkills.Contains(s.Trim())).ToList();
        List<string> extra = actual.RequiredSkills.Where(s => !expectedSkills.Contains(s.Trim())).ToList();

        Console.WriteLine($"Skills recall: matched {expected.RequiredSkills.Count - missed.Count} of {expected.RequiredSkills.Count}");
        if (missed.Count > 0)
            Console.WriteLine($"  missed: {string.Join(", ", missed)}");
        if (extra.Count > 0)
            Console.WriteLine($"  extra (not in golden): {string.Join(", ", extra)}");
    }

    // Exact match after trimming, ignoring case; null only matches null
    private static int PrintField(string name, string? actual, string? expected)
    {
        bool match = string.Equals(actual?.Trim(), expected?.Trim(), StringComparison.OrdinalIgnoreCase);
        Console.WriteLine(match
            ? $"  ✅ {name}"
            : $"  ❌ {name}: got {Show(actual)}, expected {Show(expected)}");
        return match ? 1 : 0;
    }

    private static string Show(string? value) => value is null ? "null" : $"\"{value}\"";
}
