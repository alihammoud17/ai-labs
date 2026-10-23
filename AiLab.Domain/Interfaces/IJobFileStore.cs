using AiLab.Domain.DTOs;

namespace AiLab.Domain.Interfaces;

// prompts/, inputs/ and the golden answers for /extract and /eval. Paths are relative to the working directory
// (the app is run from the repo root); the path conventions themselves stay with the caller.
public interface IJobFileStore
{
    bool Exists(string path);

    // For "not found" messages
    string FullPath(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    // Every inputs/job-*.txt, unsorted; empty when inputs/ is missing
    IReadOnlyList<string> FindInputs();

    // A golden file that doesn't fit JobPosting returns null with the reason, instead of throwing
    JobPosting? LoadExpected(string expectedPath, out string? error);
}
