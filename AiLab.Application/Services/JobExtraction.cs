using AiLab.Domain.DTOs;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;

namespace AiLab.Application.Services;

// /extract without the console: find the files, run the extraction, read the reply back and load the golden answer.
// Separate from the chat, so the history is untouched.
public class JobExtraction(JobExtractor extractor, IJobFileStore files)
{
    // "/extract v2 inputs/job-1.txt" → prompts/extract-v2.txt. Relative paths start from where the app was started
    // (the repo root for inputs/...).
    public JobExtractionCheck Check(string version, string path)
    {
        string promptPath = Path.Combine("prompts", $"extract-{version}.txt");
        if (!files.Exists(promptPath))
            return new JobExtractionCheck(promptPath, PromptNotFound: files.FullPath(promptPath), FileNotFound: null);
        if (!files.Exists(path))
            return new JobExtractionCheck(promptPath, PromptNotFound: null, FileNotFound: files.FullPath(path));
        return new JobExtractionCheck(promptPath, null, null);
    }

    // Prompts are read on every call, so they can be edited between runs without recompiling
    public async Task<ChatResponse<JobPosting>> ExtractAsync(string promptPath, string path, CancellationToken cancellationToken) =>
        await extractor.ExtractAsync(await files.ReadAllTextAsync(promptPath), await files.ReadAllTextAsync(path), cancellationToken);

    public JobExtractionResult Evaluate(ChatResponse<JobPosting> response, string path)
    {
        // TryGetResult (inside StructuredReply) instead of .Result: a reply that isn't valid JSON for JobPosting is reported instead of throwing
        bool parsed = StructuredReply.TryParse(response, out JobPosting? job, out bool strippedFence) && job is not null;
        bool cutOff = response.FinishReason == ChatFinishReason.Length;

        string expectedPath = JobScorer.ExpectedPathFor(path);
        if (job is null || !files.Exists(expectedPath))
            return new JobExtractionResult(parsed, job, strippedFence, cutOff, expectedPath, HasExpectedFile: false, Expected: null, ExpectedError: null);

        JobPosting? expected = files.LoadExpected(expectedPath, out string? error);
        return new JobExtractionResult(parsed, job, strippedFence, cutOff, expectedPath, HasExpectedFile: true, expected, error);
    }
}
