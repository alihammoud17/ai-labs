using AiLab.Domain.Interfaces;

namespace AiLab.Infrastructure.Files;

// eval-runs/ in the repo root, for both evals. dotnet run --project starts the Api in AiLab.Api/,
// so the repo root is found from the app's folder rather than the working directory.
public class FileEvalRunStore : IEvalRunStore
{
    public string CreateRunFolder(string runName)
    {
        string runFolder = Path.Combine(RepoRoot(), "eval-runs", runName);
        Directory.CreateDirectory(runFolder);
        return runFolder;
    }

    public Task WriteAsync(string runFolder, string fileName, string content, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(Path.Combine(runFolder, fileName), content, cancellationToken);

    // The first folder above the app that holds AiLab.slnx; the working directory if there is none
    private static string RepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AiLab.slnx")))
                return dir.FullName;
        return Directory.GetCurrentDirectory();
    }
}
