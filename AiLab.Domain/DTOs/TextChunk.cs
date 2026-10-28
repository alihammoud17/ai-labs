using System.Security.Cryptography;
using System.Text;

namespace AiLab.Domain.DTOs;

/// <summary>
/// One piece of a document, as a chunker cuts it.
/// <paramref name="Chunker"/>: "fixed", "heading" or "lines" (the seed sentences).
/// <paramref name="Position"/>: the chunk's order within its source for that chunker.
/// <paramref name="Heading"/>: the section path joined with " > ", or null when no heading is above the chunk.
/// <paramref name="StartLine"/> / <paramref name="EndLine"/>: the lines of the source the chunk covers, 1-based and inclusive.
/// </summary>
public sealed record TextChunk(string Chunker, string Source, int Position, string Text, string? Heading, int StartLine, int EndLine)
{
    // Built here, not by each chunker, so every key has the same shape. Re-ingesting the same position replaces the row.
    public string Key => $"{Chunker}:{Source}:{Position}";

    // Lets a re-ingest skip chunks whose text hasn't changed (step 3.6)
    public string ContentHash => HashOf(Text);

    /// <summary>SHA-256 of the UTF-8 text, as 64 lowercase hex characters.</summary>
    public static string HashOf(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
