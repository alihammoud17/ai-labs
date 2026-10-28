using AiLab.Domain.DTOs;
using AiLab.Domain.Settings;

namespace AiLab.Application.Chunking;

// Windows of about MaxChars over the whole file, ignoring its structure: a cut can fall inside a section, a table or a code block.
// The text is the raw markdown (heading lines included); Heading is the path of the nearest heading above the window's start.
public sealed class FixedSizeChunker(ChunkingOptions? options = null) : IChunker
{
    public const string ChunkerName = "fixed";

    private readonly ChunkingOptions _options = options ?? ChunkingOptions.Default;

    public string Name => ChunkerName;

    public IReadOnlyList<TextChunk> Chunk(string source, string markdown)
    {
        var markdownText = new MarkdownText(markdown);

        return FixedWindows.Split(markdownText.Text, 0, markdownText.Text.Length, _options)
            .Select((window, i) => new TextChunk(
                Name,
                source,
                Position: i + 1,
                markdownText.Text[window.Start..window.End],
                markdownText.HeadingPathAt(window.Start),
                StartLine: markdownText.LineAt(window.Start),
                EndLine: markdownText.LineAt(window.End - 1)))
            .ToList();
    }
}
