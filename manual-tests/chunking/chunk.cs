#:project ../../AiLab.Application/AiLab.Application.csproj
// Manual test for the two chunkers. From the repo root:
//   dotnet run manual-tests/chunking/chunk.cs -- <file.md> [fixed|heading] [maxChars overlap minTail]
// Prints every chunk: position, line range, heading, size, then the text.
using AiLab.Application.Chunking;
using AiLab.Domain.DTOs;
using AiLab.Domain.Settings;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run manual-tests/chunking/chunk.cs -- <file.md> [fixed|heading] [maxChars overlap minTail]");
    return;
}

string path = args[0];
string name = args.Length > 1 ? args[1] : "heading";
ChunkingOptions options = args.Length > 4
    ? new ChunkingOptions(int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]))
    : ChunkingOptions.Default;
IChunker chunker = name == "fixed" ? new FixedSizeChunker(options) : new HeadingChunker(options);

IReadOnlyList<TextChunk> chunks = chunker.Chunk(Path.GetFileName(path), File.ReadAllText(path));

Console.WriteLine($"{chunker.Name} chunker, {options.MaxChars}/{options.OverlapChars}/{options.MinTailChars} characters: {chunks.Count} chunks\n");
foreach (TextChunk chunk in chunks)
{
    Console.WriteLine($"── {chunk.Key}  lines {chunk.StartLine}-{chunk.EndLine}  {chunk.Text.Length} chars (~{IChunker.EstimateTokens(chunk.Text)} tokens)");
    Console.WriteLine($"   heading: {chunk.Heading ?? "(none)"}");
    Console.WriteLine($"   hash:    {chunk.ContentHash[..12]}…");
    foreach (string line in chunk.Text.Split('\n'))
        Console.WriteLine($"   │ {line}");
    Console.WriteLine();
}
