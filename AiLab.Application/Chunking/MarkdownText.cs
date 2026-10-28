using System.Text.RegularExpressions;

namespace AiLab.Application.Chunking;

// One markdown file, read once for both chunkers: the text with \n line ends, where each line starts,
// and the ATX headings (# to ######) outside code fences, each with its section path.
internal sealed partial class MarkdownText
{
    public string Text { get; }
    public IReadOnlyList<MarkdownHeading> Headings { get; }

    // _lineStarts[i] is the offset of line i + 1
    private readonly int[] _lineStarts;

    public MarkdownText(string markdown)
    {
        Text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

        var starts = new List<int> { 0 };
        for (int i = 0; i < Text.Length; i++)
            if (Text[i] == '\n')
                starts.Add(i + 1);
        _lineStarts = starts.ToArray();

        Headings = FindHeadings(Text.Split('\n'));
    }

    public int LineCount => _lineStarts.Length;

    /// <summary>The 1-based line that holds <paramref name="offset"/>.</summary>
    public int LineAt(int offset)
    {
        int index = Array.BinarySearch(_lineStarts, offset);
        // Not found: ~index is the first start after the offset, so the line is the one before it
        return index >= 0 ? index + 1 : ~index;
    }

    /// <summary>The offset where the 1-based <paramref name="line"/> starts; the end of the text past the last line.</summary>
    public int LineStart(int line) => line <= _lineStarts.Length ? _lineStarts[line - 1] : Text.Length;

    /// <summary>The path of the last heading at or above <paramref name="offset"/>, or null when there is none.</summary>
    public string? HeadingPathAt(int offset)
    {
        int line = LineAt(offset);
        return Headings.LastOrDefault(h => h.Line <= line)?.Path;
    }

    private static List<MarkdownHeading> FindHeadings(string[] lines)
    {
        var headings = new List<MarkdownHeading>();
        var stack = new List<(int Level, string Title)>();
        string? fence = null;

        for (int i = 0; i < lines.Length; i++)
        {
            Match fenceLine = FenceRegex().Match(lines[i]);
            if (fence is null && fenceLine.Success)
            {
                fence = fenceLine.Groups["fence"].Value;
                continue;
            }
            if (fence is not null)
            {
                // Closed by a line of only the same character, at least as long as the opening one
                if (fenceLine.Success && fenceLine.Groups["fence"].Value[0] == fence[0]
                    && fenceLine.Groups["fence"].Length >= fence.Length && fenceLine.Groups["info"].Length == 0)
                    fence = null;
                continue;
            }

            Match heading = HeadingRegex().Match(lines[i]);
            if (!heading.Success)
                continue;
            string title = heading.Groups["title"].Value.Trim();
            if (title.Length == 0)
                continue;

            // A skipped level (# then ###) just nests: the path is "A > C"
            int level = heading.Groups["hashes"].Length;
            while (stack.Count > 0 && stack[^1].Level >= level)
                stack.RemoveAt(stack.Count - 1);
            stack.Add((level, title));

            headings.Add(new MarkdownHeading(i + 1, level, title, string.Join(" > ", stack.Select(s => s.Title))));
        }

        return headings;
    }

    // Up to 3 spaces of indent, then ``` or ~~~ (3 or more), then an optional info string such as "json"
    [GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})[ \t]*(?<info>[^`]*?)[ \t]*$")]
    private static partial Regex FenceRegex();

    // "## Title", "## Title ##"; "#5" and "#hashtag" are not headings (a space is required)
    [GeneratedRegex(@"^ {0,3}(?<hashes>#{1,6})(?:[ \t]+(?<title>.*?))?(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex HeadingRegex();
}

internal sealed record MarkdownHeading(int Line, int Level, string Title, string Path);
