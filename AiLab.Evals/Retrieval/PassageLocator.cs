using System.Collections.Concurrent;
using System.Text;
using AiLab.Evals.Evaluators;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals.Retrieval;

// Where a passage the Knowledge Base returned sits in the corpus file, as 1-based lines, so it can be scored against the
// same gold lines as pgvector's chunks (RecallAtKEvaluator.IsHit needs file + start + end line). The KB returns text only.
//
// The text isn't verbatim: aws-emulator collapses every run of whitespace, newlines included, to one space, and cuts the
// passage mid-line (checked 2026-11-24). Real Bedrock may keep the newlines. So both texts are normalised the same way,
// the passage is found in the file's normalised form, and the lines come from a map kept while normalising the FILE:
// normalised character i → the original line it came from. Lines are never counted in the passage, which may have none.
// Not found → no range and a reason, never a guess (no prefix fallback: every emulator passage matches in full).
public sealed class PassageLocator(string corpusRoot)
{
    public const string DefaultCorpus = "data/corpus";

    private readonly ConcurrentDictionary<string, NormalisedFile?> _files = new(StringComparer.Ordinal);

    public PassageLocator() : this(RepoPaths.Resolve(DefaultCorpus)) { }

    public PassageLocation Locate(string source, string passage)
    {
        NormalisedFile? file = _files.GetOrAdd(source, Load);
        if (file is null)
            return PassageLocation.NotFound($"{source} is not in the corpus ({corpusRoot})");

        string needle = Normalise(passage, lines: null).Text;
        if (needle.Length == 0)
            return PassageLocation.NotFound($"empty passage for {source}");

        // The first occurrence: a passage long enough to be a chunk doesn't repeat inside one file
        int at = file.Text.IndexOf(needle, StringComparison.Ordinal);
        if (at < 0)
            return PassageLocation.NotFound($"passage not found in {source}");

        int start = file.Lines[at];
        int end = file.Lines[at + needle.Length - 1]; // the line holding the last matched character: a mid-line cut ends there
        return PassageLocation.Found(new RetrievedChunk(source, start, end));
    }

    private NormalisedFile? Load(string source)
    {
        string path = Path.GetFullPath(Path.Combine(corpusRoot, source));
        // A source that climbs out of the corpus (../) is not a corpus file
        if (!path.StartsWith(Path.GetFullPath(corpusRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
            return null;

        var lines = new List<int>();
        var (text, _) = Normalise(File.ReadAllText(path), lines);
        return new NormalisedFile(text, [.. lines]);
    }

    // Every run of whitespace → one space, trimmed at both ends. With `lines`, records for each kept character the
    // 1-based line of the original character it stands for; a collapsed run is recorded at the line of its first
    // whitespace character, which is never the first or last character of a match anyway (both texts are trimmed).
    internal static (string Text, int Count) Normalise(string text, List<int>? lines)
    {
        var sb = new StringBuilder(text.Length);
        int line = 1;
        bool pendingSpace = false;
        int spaceLine = 1;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!pendingSpace)
                    spaceLine = line;
                pendingSpace = sb.Length > 0; // no leading space
                if (c == '\n')
                    line++;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                lines?.Add(spaceLine);
                pendingSpace = false;
            }
            sb.Append(c);
            lines?.Add(line);
        }
        return (sb.ToString(), sb.Length);
    }

    private sealed record NormalisedFile(string Text, int[] Lines);
}

// A located passage (Chunk set, Reason null) or a passage that couldn't be placed (Chunk null, Reason says why).
// The caller lists the unlocated ones and counts them as non-hits; it never drops them.
public sealed record PassageLocation(RetrievedChunk? Chunk, string? Reason)
{
    public bool Located => Chunk is not null;

    public static PassageLocation Found(RetrievedChunk chunk) => new(chunk, null);

    public static PassageLocation NotFound(string reason) => new(null, reason);
}
