namespace AiLab.Domain.Interfaces;

// The repo's eval-runs/ folder: raw replies from the evals, kept so they can be re-scored without new model calls
public interface IEvalRunStore
{
    // Creates eval-runs/<runName> and returns its full path
    string CreateRunFolder(string runName);

    Task WriteAsync(string runFolder, string fileName, string content, CancellationToken cancellationToken = default);
}
