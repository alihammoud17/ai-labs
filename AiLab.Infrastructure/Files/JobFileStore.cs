using AiLab.Domain.DTOs;
using AiLab.Domain.Interfaces;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AiLab.Infrastructure.Files;

// prompts/, inputs/ and the golden answers. Relative paths start from where the app was started (the repo root).
public class JobFileStore : IJobFileStore
{
    public bool Exists(string path) => File.Exists(path);

    public string FullPath(string path) => Path.GetFullPath(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public IReadOnlyList<string> FindInputs() =>
        Directory.Exists("inputs") ? Directory.GetFiles("inputs", "job-*.txt") : [];

    // Same options as GetResponseAsync<T> (camelCase names, WorkMode as OnSite/Hybrid/Remote), so the golden file reads like the model's reply.
    // A golden file that doesn't fit JobPosting returns null with the reason, instead of crashing the chat.
    public JobPosting? LoadExpected(string expectedPath, out string? error)
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
}
