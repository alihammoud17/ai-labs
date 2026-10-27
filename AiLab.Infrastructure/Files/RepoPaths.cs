namespace AiLab.Infrastructure.Files;

// dotnet run --project starts a web project in its own folder (AiLab.Api/, AiLab.Rag/), not where it was typed,
// so repo files are found from the app's folder rather than the working directory.
public static class RepoPaths
{
    // The first folder above the app that holds AiLab.slnx; the working directory if there is none
    public static string Root()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AiLab.slnx")))
                return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    // A relative path is taken from the repo root; an absolute one is kept
    public static string Resolve(string path) => Path.GetFullPath(path, Root());
}
