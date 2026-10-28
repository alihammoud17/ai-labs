namespace AiLab.Domain.Constants;

// The values of TextChunk.Chunker: one collection holds all of them, and searches filter on it
public static class ChunkerNames
{
    public const string Fixed = "fixed";
    public const string Heading = "heading";
    // POST /dev/seed: one chunk per line of the similarity sentences
    public const string Lines = "lines";

    public static readonly IReadOnlyList<string> All = [Fixed, Heading, Lines];
}
