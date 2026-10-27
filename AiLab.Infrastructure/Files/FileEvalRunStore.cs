using AiLab.Domain.Interfaces;

namespace AiLab.Infrastructure.Files;

// eval-runs/ in the repo root, for both evals (see RepoPaths for why it isn't the working directory)
public class FileEvalRunStore : IEvalRunStore
{
    public string CreateRunFolder(string runName)
    {
        string runFolder = Path.Combine(RepoPaths.Root(), "eval-runs", runName);
        Directory.CreateDirectory(runFolder);
        return runFolder;
    }

    public Task WriteAsync(string runFolder, string fileName, string content, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(Path.Combine(runFolder, fileName), content, cancellationToken);
}
