using AiLab.Application.Services;
using AiLab.Domain.DTOs;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;

namespace AiLab.Console.Chat;

// Inside AiLab.Console.*, a bare "Console" would mean the AiLab.Console namespace
using Console = System.Console;

// /extract and /eval: argument parsing and everything they print. The work is in JobExtraction and JobEval.
public class JobCommands(JobExtraction extraction, JobEval eval, SessionCostTracker cost, TurnCancellation cancellation)
{
    // Typed extraction (deliverable 3); separate from the chat, so history is untouched
    public async Task ExtractAsync(string arguments)
    {
        // "/extract v2 inputs/job-1.txt" picks the prompt; "/extract inputs/job-1.txt" means v1
        string[] parts = arguments.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string version = "v1";
        if (parts.Length == 2 && (parts[0].Equals("v1", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("v2", StringComparison.OrdinalIgnoreCase)))
        {
            version = parts[0].ToLowerInvariant();
            parts = parts[1..];
        }

        string path = parts.Length == 1 ? parts[0] : "";
        if (path.Length == 0 || path.Equals("v1", StringComparison.OrdinalIgnoreCase) || path.Equals("v2", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: /extract [v1|v2] <path>   e.g. /extract v2 inputs/job-1.txt");
            return;
        }

        JobExtractionCheck check = extraction.Check(version, path);
        if (check.PromptNotFound is not null)
        {
            Console.WriteLine($"[prompt not found: {check.PromptNotFound}]");
            return;
        }
        if (check.FileNotFound is not null)
        {
            Console.WriteLine($"[file not found: {check.FileNotFound}]");
            return;
        }

        Console.WriteLine($"[extracting with prompt {version}...]");
        ChatResponse<JobPosting> response;
        CancellationToken cancellationToken = cancellation.Begin();
        try
        {
            response = await extraction.ExtractAsync(check.PromptPath, path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[extraction cancelled]");
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[error: {ex.Message}]");
            return;
        }
        finally
        {
            cancellation.End();
        }

        JobExtractionResult result = extraction.Evaluate(response, path);
        if (result.Parsed)
        {
            JobPosting job = result.Job!;
            if (result.StrippedFence)
                Console.WriteLine("[parsed after stripping a ```json fence around the reply]");
            Console.WriteLine($"Title:             {job.Title}");
            Console.WriteLine($"Company:           {job.Company ?? "null"}");
            Console.WriteLine($"Location:          {job.Location ?? "null"}");
            Console.WriteLine($"Work mode:         {job.WorkMode?.ToString() ?? "null"}");
            Console.WriteLine($"Required skills:   {string.Join(", ", job.RequiredSkills)} ({job.RequiredSkills.Count})");
            Console.WriteLine($"Years experience:  {job.YearsOfExperience?.ToString() ?? "null"}");
        }
        else
        {
            Console.WriteLine("❌ Could not parse the reply into a JobPosting. Raw reply:");
            Console.WriteLine(response.Text);
        }

        UsageLine.Print(cost.Add(response));
        if (result.CutOff)
            Console.WriteLine("⚠ Answer was cut off (output token limit reached)");

        if (result.Job is null || !result.HasExpectedFile)
            return;

        if (result.Expected is null)
        {
            Console.WriteLine($"[can't score: {result.ExpectedPath} doesn't fit JobPosting: {result.ExpectedError}]");
            return;
        }

        Console.WriteLine($"Score vs {result.ExpectedPath}:");
        PrintScore(result.Job, result.Expected);
    }

    // "/eval v2 3": every inputs/job-N.txt with a golden answer, run 3 times each with prompts/extract-v2.txt
    public async Task EvalAsync(string arguments)
    {
        string[] parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int runs = 3;
        if (parts.Length is < 1 or > 2
            || !(parts[0].Equals("v1", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("v2", StringComparison.OrdinalIgnoreCase))
            || (parts.Length == 2 && (!int.TryParse(parts[1], out runs) || runs < 1)))
        {
            Console.WriteLine("Usage: /eval <v1|v2> [runs]   e.g. /eval v2 3 (runs defaults to 3)");
            return;
        }
        string version = parts[0].ToLowerInvariant();

        JobEvalPlan plan = await eval.PrepareAsync(version);
        if (plan.PromptNotFound is not null)
        {
            Console.WriteLine($"[prompt not found: {plan.PromptNotFound}]");
            return;
        }
        foreach (JobEvalSkip skip in plan.Skipped)
            Console.WriteLine($"[skipping {skip.Path}: {skip.ExpectedPath} doesn't fit JobPosting: {skip.Error}]");
        if (plan.NoInputsIn is not null)
        {
            Console.WriteLine($"[no inputs with a golden answer in {plan.NoInputsIn}]");
            return;
        }

        Console.WriteLine($"[eval {version}: {plan.Inputs.Count} inputs × {runs} run{(runs == 1 ? "" : "s")} on {plan.Model}, replies saved to {plan.RunFolder}/]");

        // Ctrl+C cancels the call in flight; the eval then stops and still prints the totals so far
        JobEvalResult result;
        CancellationToken cancellationToken = cancellation.Begin();
        try
        {
            result = await eval.RunAsync(plan, runs, PrintEvalRun, cancellationToken);
        }
        finally
        {
            cancellation.End();
        }

        if (result.StoppedBecause is not null)
            Console.WriteLine($"[eval stopped ({result.StoppedBecause}) after {result.Runs.Count} of {plan.Inputs.Count * runs} runs]");

        PrintEvalTotals(version, eval.Summarize(result, plan.Inputs.Count), runs);
    }

    private static void PrintScore(JobPosting actual, JobPosting expected)
    {
        JobScore score = JobScorer.Score(actual, expected);
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

    private static string Show(string? value) => value is null ? "null" : $"\"{value}\"";

    private static void PrintEvalRun(JobEvalRun result)
    {
        string parsed = result.Score is null ? "❌ not parsed" : result.StrippedFence ? "✅ parsed (fence)" : "✅ parsed";
        string fields = result.Score is null ? "fields 0/5" : $"fields {result.Score.FieldsCorrect}/5";
        if (result.Score is not null && result.Score.FieldsCorrect < 5)
            fields += $" (wrong: {string.Join(", ", result.Score.WrongFields)})";
        string skills = $"skills {result.Score?.SkillsMatched ?? 0} of {result.SkillsTotal}";
        string tokens = $"in={result.InputTokens?.ToString() ?? "?"} out={result.OutputTokens?.ToString() ?? "?"}";
        string cost = result.Cost is null ? "$?" : $"${result.Cost:0.000000}";

        Console.WriteLine($"{result.Input} run {result.Run}  {parsed}  {fields}  {skills}  {tokens}  {cost}{(result.CutOff ? "  ⚠ cut off" : "")}");
    }

    private static void PrintEvalTotals(string version, JobEvalTotals totals, int runs)
    {
        Console.WriteLine($"\nTotals for prompt {version}:");
        if (totals.RunCount == 0)
        {
            Console.WriteLine("  no completed runs");
            return;
        }

        Console.WriteLine($"  Parse failures: {totals.ParseFailures} of {totals.RunCount} runs");

        foreach (JobEvalPass pass in totals.Passes)
        {
            string partial = pass.Inputs < totals.InputCount ? $" (only {pass.Inputs} of {totals.InputCount} inputs)" : "";
            Console.WriteLine($"  Pass {pass.Run}: fields {pass.Fields}/{pass.Inputs * 5}, skills {pass.Skills} of {pass.SkillsTotal}{partial}");
        }

        if (totals.Passes.Any(p => p.Inputs < totals.InputCount))
            Console.WriteLine("  ⚠ The eval didn't finish, so the passes cover different inputs and aren't comparable.");

        Console.WriteLine($"  Fields correct: avg {totals.FieldsAverage:0.0} of {totals.FieldsTotal}, lowest {totals.FieldsLowest}, highest {totals.FieldsHighest} (over {totals.Passes.Count} of {runs} passes)");
        Console.WriteLine($"  Skills recall:  avg {totals.SkillsAverage:0.0} of {totals.SkillsTotal}, lowest {totals.SkillsLowest}, highest {totals.SkillsHighest}");

        string unknownUsage = totals.SomeUsageUnknown ? " (some runs reported no usage)" : "";
        string cost = totals.Cost is null ? "unknown" : $"${totals.Cost:0.000000}";
        Console.WriteLine($"  Tokens: in={totals.InputTokens}, out={totals.OutputTokens}{unknownUsage} | Cost: {cost} | Session: ${totals.SessionTotal:0.000000}");
    }
}
