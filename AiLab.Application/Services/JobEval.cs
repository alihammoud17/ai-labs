using AiLab.Domain.DTOs;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;
using System.Text.RegularExpressions;

namespace AiLab.Application.Services;

// /eval without the console: every inputs/job-N.txt with a golden answer, run N times with one prompt and scored.
// Separate from the chat, so the history is untouched.
public class JobEval(JobExtractor extractor, IJobFileStore files, IEvalRunStore evalRuns, SessionCostTracker cost)
{
    // "/eval v2": the prompt, the inputs and a run folder. Nothing runs yet.
    public async Task<JobEvalPlan> PrepareAsync(string version)
    {
        string model = cost.Model ?? "unknown-model";

        // Read once, so every run of this eval uses the same prompt text
        string promptPath = Path.Combine("prompts", $"extract-{version}.txt");
        if (!files.Exists(promptPath))
            return new JobEvalPlan(version, files.FullPath(promptPath), [], null, "", [], model, "");
        string instruction = await files.ReadAllTextAsync(promptPath);

        // job-N.txt with a job-N.expected.json next to it, sorted by N so job-10 comes after job-9
        List<JobEvalInput> inputs = [];
        List<JobEvalSkip> skipped = [];
        IEnumerable<(int Number, string Path)> candidates = files.FindInputs()
            .Select(path => (Match: Regex.Match(Path.GetFileName(path), @"^job-(\d+)\.txt$"), Path: path))
            .Where(c => c.Match.Success)
            .Select(c => (int.Parse(c.Match.Groups[1].Value), c.Path))
            .OrderBy(c => c.Item1);
        foreach ((int _, string path) in candidates)
        {
            string expectedPath = JobScorer.ExpectedPathFor(path);
            if (!files.Exists(expectedPath))
                continue;

            JobPosting? expected = files.LoadExpected(expectedPath, out string? error);
            if (expected is null)
            {
                skipped.Add(new JobEvalSkip(path, expectedPath, error));
                continue;
            }
            inputs.Add(new JobEvalInput(Path.GetFileNameWithoutExtension(path), path, expected));
        }

        if (inputs.Count == 0)
            return new JobEvalPlan(version, null, skipped, files.FullPath("inputs"), instruction, [], model, "");

        // One folder per eval, so the replies can be re-scored later without new model calls
        string runFolder = evalRuns.CreateRunFolder($"{DateTime.Now:yyyyMMdd-HHmmss}_{model}_{version}");
        return new JobEvalPlan(version, null, skipped, null, instruction, inputs, model, runFolder);
    }

    // Runs the plan. onRun is called after each run, so the caller can show it at once.
    // Cancelling the token cancels the call in flight; the loop then stops and returns the runs so far.
    public async Task<JobEvalResult> RunAsync(JobEvalPlan plan, int runs, Action<JobEvalRun> onRun, CancellationToken cancellationToken)
    {
        List<JobEvalRun> results = [];
        string? stoppedBecause = null;

        foreach ((string name, string path, JobPosting expected) in plan.Inputs)
        {
            string postingText = await files.ReadAllTextAsync(path);
            for (int run = 1; run <= runs && stoppedBecause is null; run++)
            {
                ChatResponse<JobPosting> response;
                try
                {
                    response = await extractor.ExtractAsync(plan.Instruction, postingText, cancellationToken);
                }
                // OllamaSharp can report a cancelled call as "500 (Internal Server Error)" instead of
                // OperationCanceledException, so the token decides whether Ctrl+C caused the failure
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    stoppedBecause = "cancelled";
                    break;
                }
                catch (Exception ex)
                {
                    // A provider error (server down, rate limit) would most likely repeat on every remaining run
                    stoppedBecause = $"error on {name} run {run}: {ex.Message}";
                    break;
                }

                await evalRuns.WriteAsync(plan.RunFolder, $"{name}_run-{run}.txt", response.Text);

                bool parsed = StructuredReply.TryParse(response, out JobPosting? job, out bool strippedFence) && job is not null;
                JobScore? score = parsed ? JobScorer.Score(job!, expected) : null;
                TurnUsage usage = cost.Add(response);
                JobEvalRun result = new(name, run, score, expected.RequiredSkills.Count, usage.InputTokens, usage.OutputTokens, usage.Cost,
                    strippedFence, response.FinishReason == ChatFinishReason.Length);
                results.Add(result);

                onRun(result);

                // Ctrl+C can land just as a call finishes; that run is kept, but nothing new starts
                if (cancellationToken.IsCancellationRequested)
                    stoppedBecause = "cancelled";
            }

            if (stoppedBecause is not null)
                break;
        }

        return new JobEvalResult(results, stoppedBecause);
    }

    // A pass is one run number over all inputs (run 1 of every input, then run 2, ...).
    // Average, lowest and highest are taken over passes, so the spread shows how much the same prompt moves between runs.
    public JobEvalTotals Summarize(JobEvalResult result, int inputCount)
    {
        IReadOnlyList<JobEvalRun> results = result.Runs;
        if (results.Count == 0)
            return new JobEvalTotals(0, 0, [], inputCount, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, null, cost.Total);

        List<JobEvalPass> passes = results
            .GroupBy(r => r.Run)
            .OrderBy(g => g.Key)
            .Select(g => new JobEvalPass(
                g.Key,
                g.Count(),
                g.Sum(r => r.Score?.FieldsCorrect ?? 0),
                g.Sum(r => r.Score?.SkillsMatched ?? 0),
                g.Sum(r => r.SkillsTotal)))
            .ToList();

        // From the passes, not the input list, so a cancelled eval shows "of 5" for one finished input instead of "of 25"
        int fieldsTotal = passes.Max(p => p.Inputs) * 5;
        int skillsTotal = passes.Max(p => p.SkillsTotal);

        return new JobEvalTotals(
            results.Count,
            results.Count(r => r.Score is null),
            passes,
            inputCount,
            fieldsTotal, passes.Average(p => p.Fields), passes.Min(p => p.Fields), passes.Max(p => p.Fields),
            skillsTotal, passes.Average(p => p.Skills), passes.Min(p => p.Skills), passes.Max(p => p.Skills),
            results.Sum(r => r.InputTokens ?? 0),
            results.Sum(r => r.OutputTokens ?? 0),
            results.Any(r => r.InputTokens is null || r.OutputTokens is null),
            results.Any(r => r.Cost is null) ? null : results.Sum(r => r.Cost!.Value),
            cost.Total);
    }
}
