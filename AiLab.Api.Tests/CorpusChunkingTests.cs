using AiLab.Application.Chunking;
using AiLab.Domain.DTOs;
using AiLab.Domain.Settings;
using AiLab.Infrastructure.Files;

namespace AiLab.Api.Tests;

// Both chunkers over the real snapshot in data/corpus (offline: files only, no embeddings)
public class CorpusChunkingTests
{
    public static TheoryData<string> Chunkers => [FixedSizeChunker.ChunkerName, HeadingChunker.ChunkerName];

    [Theory]
    [MemberData(nameof(Chunkers))]
    public void Every_file_gives_sequential_non_empty_chunks_within_the_size_limit(string name)
    {
        IChunker chunker = Create(name);
        ChunkingOptions options = ChunkingOptions.Default;

        foreach ((string source, string markdown) in Corpus())
        {
            IReadOnlyList<TextChunk> chunks = chunker.Chunk(source, markdown);

            Assert.NotEmpty(chunks);
            Assert.Equal(Enumerable.Range(1, chunks.Count), chunks.Select(c => c.Position));
            Assert.All(chunks, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Text));
                Assert.InRange(c.StartLine, 1, c.EndLine);
                // The window, plus a merged tail, plus (heading chunker) the heading path in front
                Assert.InRange(c.Text.Length, 1, options.MaxChars + options.MinTailChars + (c.Heading?.Length ?? 0) + 2);
            });
            Assert.Equal(chunks.Count, chunks.Select(c => c.Key).Distinct().Count());
        }
    }

    [Fact]
    public void The_heading_chunker_finds_the_sections_the_questions_point_at()
    {
        IReadOnlyList<TextChunk> chunks = Corpus().SelectMany(f => new HeadingChunker().Chunk(f.Source, f.Markdown)).ToList();

        Assert.Contains(chunks, c => c.Source == "docs/contracts.md" && c.Heading == "Service Contracts > Internal billing subscription events > Authentication");
        Assert.Contains(chunks, c => c.Source == "docs/decisions.md" && c.Heading == "Decisions and Open Review Items > Accepted decisions > CORS is a configured allowlist outside Development");
        Assert.Contains(chunks, c => c.Source == "docs/architecture.md" && c.Heading == "Architecture > Tenant resolution and isolation");
        Assert.Contains(chunks, c => c.Source == "docs/decisions.md" && c.Heading == "Decisions and Open Review Items > Accepted decisions > Current billing durability is file-backed");
        Assert.Contains(chunks, c => c.Source == "README.md" && c.Heading == "Multi-Tenant SaaS API > Current limitations");
    }

    private static IChunker Create(string name) => name == FixedSizeChunker.ChunkerName ? new FixedSizeChunker() : new HeadingChunker();

    // Source = the path relative to data/corpus, with forward slashes, as /dev/ingest will store it
    private static IEnumerable<(string Source, string Markdown)> Corpus()
    {
        string root = RepoPaths.Resolve("data/corpus");
        return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)));
    }
}
