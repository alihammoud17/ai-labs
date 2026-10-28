using AiLab.Domain.Settings;

namespace AiLab.Application.Chunking;

// The fixed-size window, shared by FixedSizeChunker (the whole file) and HeadingChunker (a section that is too long)
internal static class FixedWindows
{
    /// <summary>
    /// Cuts <c>text[from..to)</c> into windows of at most <see cref="ChunkingOptions.MaxChars"/>, each starting
    /// about <see cref="ChunkingOptions.OverlapChars"/> before the previous one ended. Returns [Start, End) offsets
    /// into <paramref name="text"/>, trimmed of whitespace; none when the range is blank.
    /// </summary>
    public static List<(int Start, int End)> Split(string text, int from, int to, ChunkingOptions options)
    {
        var windows = new List<(int Start, int End)>();
        int start = SkipWhitespace(text, from, to);

        while (start < to)
        {
            int end = to;
            if (to - start > options.MaxChars)
            {
                end = start + options.MaxChars;
                // Back to the last whitespace, but only as far as just past the overlap: the next window starts at
                // end - overlap, so it always lands after this start and the loop moves forward. No whitespace there
                // (one very long word or line): cut mid-word.
                int earliest = start + options.OverlapChars + 1;
                for (int i = end; i >= earliest; i--)
                    if (char.IsWhiteSpace(text[i]))
                    {
                        end = i;
                        break;
                    }
            }

            windows.Add((start, TrimEnd(text, start, end)));
            if (end >= to)
                break;

            int next = end - options.OverlapChars;
            // Start the overlap at a word, not halfway through one
            if (!char.IsWhiteSpace(text[next - 1]))
                while (next < end && !char.IsWhiteSpace(text[next]))
                    next++;
            start = SkipWhitespace(text, next, to);
        }

        // A short last window is mostly the previous one's overlap: give its few new characters to the previous window.
        // That window may then be up to MinTailChars over MaxChars.
        if (windows.Count >= 2 && windows[^1].End - windows[^2].End < options.MinTailChars)
        {
            windows[^2] = (windows[^2].Start, windows[^1].End);
            windows.RemoveAt(windows.Count - 1);
        }

        return windows;
    }

    private static int SkipWhitespace(string text, int from, int to)
    {
        while (from < to && char.IsWhiteSpace(text[from]))
            from++;
        return from;
    }

    private static int TrimEnd(string text, int start, int end)
    {
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;
        return end;
    }
}
