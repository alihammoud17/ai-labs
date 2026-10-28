using AiLab.Application.Chunking;
using AiLab.Domain.DTOs;
using AiLab.Domain.Settings;

namespace AiLab.Api.Tests;

public class HeadingChunkerTests
{
    private static readonly HeadingChunker Chunker = new();

    [Fact]
    public void A_hash_inside_a_code_fence_is_not_a_heading()
    {
        const string markdown = """
            # Scripts

            Run this:

            ```bash
            # not a heading
            dotnet test
            ```

            ~~~
            ## not one either
            ~~~

            Then done.
            """;

        TextChunk chunk = Assert.Single(Chunker.Chunk("doc.md", markdown));

        Assert.Equal("Scripts", chunk.Heading);
        Assert.Contains("# not a heading", chunk.Text);
        Assert.Contains("## not one either", chunk.Text);
        Assert.EndsWith("Then done.", chunk.Text);
    }

    [Fact]
    public void A_long_section_is_split_and_every_piece_keeps_the_heading()
    {
        string markdown = "# Guide\n\n## Long\n\n" + FixedSizeChunkerTests.Words(600); // 2,999 characters

        IReadOnlyList<TextChunk> chunks = Chunker.Chunk("doc.md", markdown);

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c =>
        {
            Assert.Equal("Guide > Long", c.Heading);
            Assert.StartsWith("Guide > Long\n\n", c.Text);
            Assert.InRange(c.Text.Length - "Guide > Long\n\n".Length, 1, ChunkingOptions.Default.MaxChars);
        });
        // The first piece starts at the heading line, the second where its own text does
        Assert.Equal(3, chunks[0].StartLine);
        Assert.Equal(5, chunks[1].StartLine);
    }

    [Fact]
    public void A_heading_with_no_body_makes_no_chunk()
    {
        const string markdown = """
            # Service Contracts

            ## Internal billing subscription events

            ### Endpoint

            POST /api/internal/billing/subscription-events

            ## Empty at the end

            """;

        TextChunk chunk = Assert.Single(Chunker.Chunk("doc.md", markdown));

        Assert.Equal("Service Contracts > Internal billing subscription events > Endpoint", chunk.Heading);
    }

    [Fact]
    public void A_skipped_level_nests_and_a_higher_level_closes_it()
    {
        const string markdown = """
            # A
            ### C
            under C
            ## B
            under B
            # D
            under D
            """;

        IReadOnlyList<TextChunk> chunks = Chunker.Chunk("doc.md", markdown);

        Assert.Equal(["A > C", "A > B", "D"], chunks.Select(c => c.Heading));
    }

    [Fact]
    public void A_body_runs_to_the_next_heading_at_any_level()
    {
        const string markdown = """
            ## Parent
            Parent intro.
            ### Child
            Child text.
            """;

        IReadOnlyList<TextChunk> chunks = Chunker.Chunk("doc.md", markdown);

        Assert.Equal(["Parent\n\nParent intro.", "Parent > Child\n\nChild text."], chunks.Select(c => c.Text));
        Assert.Equal([(1, 2), (3, 4)], chunks.Select(c => (c.StartLine, c.EndLine)));
    }

    [Fact]
    public void Text_before_the_first_heading_is_a_chunk_without_a_heading()
    {
        IReadOnlyList<TextChunk> chunks = Chunker.Chunk("doc.md", "A badge line.\n\n# Title\n\nBody.");

        Assert.Null(chunks[0].Heading);
        Assert.Equal("A badge line.", chunks[0].Text);
        Assert.Equal("Title", chunks[1].Heading);
    }

    [Fact]
    public void A_file_with_no_headings_falls_back_to_fixed_windows()
    {
        string markdown = FixedSizeChunkerTests.Words(1000) + "\n\n#hashtag is not a heading either";

        IReadOnlyList<TextChunk> heading = Chunker.Chunk("doc.md", markdown);
        IReadOnlyList<TextChunk> fixedSize = new FixedSizeChunker().Chunk("doc.md", markdown);

        Assert.Equal(fixedSize.Select(c => c.Text), heading.Select(c => c.Text));
        Assert.All(heading, c => Assert.Equal("heading", c.Chunker));
        Assert.All(heading, c => Assert.Null(c.Heading));
    }

    [Fact]
    public void Positions_are_sequential_across_sections_and_split_pieces()
    {
        string markdown = "# One\n\nShort.\n\n# Two\n\n" + FixedSizeChunkerTests.Words(600) + "\n\n# Three\n\nShort again.";

        IReadOnlyList<TextChunk> chunks = Chunker.Chunk("docs/x.md", markdown);

        Assert.Equal([1, 2, 3, 4], chunks.Select(c => c.Position));
        Assert.Equal(["One", "Two", "Two", "Three"], chunks.Select(c => c.Heading));
        Assert.Equal("heading:docs/x.md:4", chunks[^1].Key);
    }

    [Fact]
    public void Closing_hashes_and_indented_headings_are_read_like_markdown()
    {
        IReadOnlyList<TextChunk> chunks = Chunker.Chunk("doc.md", "# Title #\n\n   ## Indented ##\n\nBody.");

        Assert.Equal("Title > Indented", Assert.Single(chunks).Heading);
    }
}
