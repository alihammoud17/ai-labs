using AiLab.Domain.DTOs;

namespace AiLab.Api.Tests;

public class TextChunkTests
{
    [Fact]
    public void The_key_is_chunker_source_and_position()
    {
        var chunk = new TextChunk("heading", "docs/contracts.md", 3, "text", "Service Contracts > Authentication", 17, 42);

        Assert.Equal("heading:docs/contracts.md:3", chunk.Key);
    }

    [Fact]
    public void The_content_hash_is_the_sha256_of_the_text_in_lowercase_hex()
    {
        var chunk = new TextChunk("fixed", "README.md", 1, "abc", null, 1, 1);

        // The standard SHA-256 test vector for "abc"
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", chunk.ContentHash);
    }

    [Fact]
    public void Only_the_text_changes_the_hash()
    {
        var chunk = new TextChunk("fixed", "README.md", 1, "Same text.", null, 1, 3);

        Assert.Equal(chunk.ContentHash, (chunk with { Position = 9, Heading = "Moved", StartLine = 40, EndLine = 42 }).ContentHash);
        Assert.NotEqual(chunk.ContentHash, (chunk with { Text = "Same text!" }).ContentHash);
    }
}
