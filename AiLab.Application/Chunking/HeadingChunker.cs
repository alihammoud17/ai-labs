using AiLab.Domain.DTOs;
using AiLab.Domain.Settings;

namespace AiLab.Application.Chunking;

// One chunk per section: a heading's body runs to the next heading at any level, so a parent's chunk holds only its own
// intro text. A section longer than MaxChars is split with the fixed window and every piece keeps the Heading.
// The text starts with the heading path ("Service Contracts > Internal billing subscription events > Authentication"),
// so the embedding knows which section a piece came from even when the piece itself never names it.
public sealed class HeadingChunker(ChunkingOptions? options = null) : IChunker
{
    public const string ChunkerName = "heading";

    private readonly ChunkingOptions _options = options ?? ChunkingOptions.Default;

    public string Name => ChunkerName;

    public IReadOnlyList<TextChunk> Chunk(string source, string markdown)
    {
        var markdownText = new MarkdownText(markdown);

        // No headings: nothing to split by, so fixed windows, still stored as "heading" chunks (their own keys and filter)
        if (markdownText.Headings.Count == 0)
            return new FixedSizeChunker(_options).Chunk(source, markdown).Select(c => c with { Chunker = Name }).ToList();

        var chunks = new List<TextChunk>();

        // Text before the first heading (a badge line, an intro) is a section without a heading
        AddSection(chunks, markdownText, source, heading: null, 0, markdownText.LineStart(markdownText.Headings[0].Line));

        for (int i = 0; i < markdownText.Headings.Count; i++)
        {
            MarkdownHeading heading = markdownText.Headings[i];
            int bodyStart = markdownText.LineStart(heading.Line + 1);
            int bodyEnd = i + 1 < markdownText.Headings.Count ? markdownText.LineStart(markdownText.Headings[i + 1].Line) : markdownText.Text.Length;
            AddSection(chunks, markdownText, source, heading, bodyStart, bodyEnd);
        }

        return chunks;
    }

    private void AddSection(List<TextChunk> chunks, MarkdownText markdownText, string source, MarkdownHeading? heading, int bodyStart, int bodyEnd)
    {
        // A blank body gives no windows, so a heading followed straight by another heading makes no chunk
        List<(int Start, int End)> pieces = FixedWindows.Split(markdownText.Text, bodyStart, bodyEnd, _options);

        for (int i = 0; i < pieces.Count; i++)
        {
            (int start, int end) = pieces[i];
            string body = markdownText.Text[start..end];
            chunks.Add(new TextChunk(
                Name,
                source,
                Position: chunks.Count + 1,
                heading is null ? body : $"{heading.Path}\n\n{body}",
                heading?.Path,
                // The first piece covers the heading line too; later pieces start where their own text does
                StartLine: i == 0 && heading is not null ? heading.Line : markdownText.LineAt(start),
                EndLine: markdownText.LineAt(end - 1)));
        }
    }
}
