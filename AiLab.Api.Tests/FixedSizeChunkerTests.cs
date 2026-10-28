using AiLab.Application.Chunking;
using AiLab.Domain.DTOs;
using AiLab.Domain.Settings;

namespace AiLab.Api.Tests;

// Small windows (100 characters, 20 overlap, 10 minimum tail) so a few lines of text make several chunks
public class FixedSizeChunkerTests
{
    private static readonly ChunkingOptions Small = new(maxChunkSize: 100, overlapSize: 20, minTrailChars: 10);

    [Fact]
    public void Consecutive_chunks_share_at_most_the_overlap_and_no_word_is_lost()
    {
        string text = Words(120);

        IReadOnlyList<TextChunk> chunks = new FixedSizeChunker(Small).Chunk("doc.md", text);

        Assert.True(chunks.Count > 5);
        for (int i = 1; i < chunks.Count; i++)
        {
            string[] previous = chunks[i - 1].Text.Split(' ');
            string[] current = chunks[i].Text.Split(' ');
            // The next chunk starts with the last few words of the previous one
            int shared = Enumerable.Range(1, previous.Length).Last(n => n == 1 || previous[^n..].SequenceEqual(current[..Math.Min(n, current.Length)]));
            Assert.Equal(previous[^shared..], current[..shared]);
            Assert.InRange(string.Join(' ', current[..shared]).Length, 1, Small.OverlapChars);
        }

        // Every word, in order, once the overlaps are removed
        Assert.Equal(text.Split(' '), Distinct(chunks.SelectMany(c => c.Text.Split(' '))));
    }

    [Fact]
    public void Chunks_stay_within_max_chars_and_cut_between_words()
    {
        string text = Words(120);
        HashSet<string> words = [.. text.Split(' ')];

        IReadOnlyList<TextChunk> chunks = new FixedSizeChunker(Small).Chunk("doc.md", text);

        Assert.All(chunks.SkipLast(1), c => Assert.InRange(c.Text.Length, 1, Small.MaxChars));
        Assert.All(chunks, c => Assert.All(c.Text.Split(' '), w => Assert.Contains(w, words)));
    }

    [Fact]
    public void Positions_are_sequential_from_1_and_keys_follow_them()
    {
        IReadOnlyList<TextChunk> chunks = new FixedSizeChunker(Small).Chunk("docs/a.md", Words(60));

        Assert.Equal(Enumerable.Range(1, chunks.Count), chunks.Select(c => c.Position));
        Assert.Equal("fixed:docs/a.md:2", chunks[1].Key);
        Assert.All(chunks, c => Assert.Equal("fixed", c.Chunker));
    }

    [Fact]
    public void Line_numbers_cover_the_chunk_text()
    {
        string[] lines = Enumerable.Range(1, 30).Select(i => $"Line {i:00} has a few words.").ToArray();

        IReadOnlyList<TextChunk> chunks = new FixedSizeChunker(Small).Chunk("doc.md", string.Join('\n', lines));

        Assert.Equal(1, chunks[0].StartLine);
        Assert.Equal(30, chunks[^1].EndLine);
        Assert.All(chunks, c =>
        {
            // The first and last lines of the chunk are (parts of) the start and end lines
            Assert.Contains(c.Text.Split('\n')[0], lines[c.StartLine - 1]);
            Assert.Contains(c.Text.Split('\n')[^1], lines[c.EndLine - 1]);
            Assert.Contains(c.Text, string.Join('\n', lines[(c.StartLine - 1)..c.EndLine]));
        });
    }

    [Fact]
    public void Windows_line_ends_give_the_same_chunks_without_carriage_returns()
    {
        string unix = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"Line {i} of the file."));

        IReadOnlyList<TextChunk> fromUnix = new FixedSizeChunker(Small).Chunk("doc.md", unix);
        IReadOnlyList<TextChunk> fromWindows = new FixedSizeChunker(Small).Chunk("doc.md", unix.Replace("\n", "\r\n"));

        Assert.Equal(fromUnix, fromWindows);
        Assert.DoesNotContain(fromWindows, c => c.Text.Contains('\r'));
    }

    [Fact]
    public void Heading_is_the_path_of_the_nearest_heading_above_the_start()
    {
        string markdown = "Intro words before any heading.\n\n# Guide\n\n## Setup\n\n" + Words(40);

        IReadOnlyList<TextChunk> chunks = new FixedSizeChunker(Small).Chunk("doc.md", markdown);

        Assert.Null(chunks[0].Heading);
        Assert.All(chunks.Skip(1), c => Assert.Equal("Guide > Setup", c.Heading));
    }

    [Fact]
    public void A_short_last_chunk_is_merged_into_the_previous_one()
    {
        // 104 characters: a second window would bring only a few new characters
        string text = Words(20) + " w999";

        IReadOnlyList<TextChunk> chunks = new FixedSizeChunker(Small).Chunk("doc.md", text);

        TextChunk only = Assert.Single(chunks);
        Assert.Equal(text, only.Text);
    }

    [Fact]
    public void A_word_longer_than_a_window_is_cut_and_the_chunker_still_ends()
    {
        string text = new string('x', 350) + " end";

        IReadOnlyList<TextChunk> chunks = new FixedSizeChunker(Small).Chunk("doc.md", text);

        Assert.InRange(chunks.Count, 4, 6);
        Assert.EndsWith("end", chunks[^1].Text);
        Assert.All(chunks.SkipLast(1), c => Assert.Equal(Small.MaxChars, c.Text.Length));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\n \t ")]
    public void A_blank_file_makes_no_chunks(string markdown) =>
        Assert.Empty(new FixedSizeChunker(Small).Chunk("doc.md", markdown));

    [Fact]
    public void The_default_options_are_about_500_tokens_with_50_of_overlap()
    {
        IReadOnlyList<TextChunk> chunks = new FixedSizeChunker().Chunk("doc.md", Words(1500));

        Assert.All(chunks.SkipLast(1), c => Assert.InRange(IChunker.EstimateTokens(c.Text), 450, 500));
    }

    // "w001 w002 ...": every word unique, 4 characters, so a cut can be checked word by word
    internal static string Words(int count) => string.Join(' ', Enumerable.Range(1, count).Select(i => $"w{i:000}"));

    // Drops the repeats the overlaps add (the words are unique, so a repeat is always an overlap)
    private static IEnumerable<string> Distinct(IEnumerable<string> words)
    {
        var seen = new HashSet<string>();
        return words.Where(seen.Add);
    }
}
