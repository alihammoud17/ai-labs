using System.Net;
using AiLab.Application.Services;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;

namespace AiLab.Console.Chat;

// Inside AiLab.Console.*, a bare "Console" would mean the AiLab.Console namespace
using Console = System.Console;

// /similarity: reads the sentences and prints the grid. The scores come only from SimilarityService.
public class EmbeddingCommands(SimilarityService similarity, IEmbeddingGenerator<string, Embedding<float>> generator, TurnCancellation cancellation)
{
    private const string DefaultPath = "inputs/similarity-sentences.txt";

    // At or above HighScore a pair counts as similar (green), at or below LowScore as unrelated (dark grey)
    private const float HighScore = 0.70f;
    private const float LowScore = 0.40f;

    // Provider, model and (if the provider knows it) dimensions, as the generator reports them
    private readonly EmbeddingGeneratorMetadata? _metadata = generator.GetService<EmbeddingGeneratorMetadata>();

    // "/similarity" or "/similarity inputs/other.txt"; one sentence per line, relative to the repo root like /extract
    public async Task SimilarityAsync(string arguments)
    {
        string path = arguments.Length == 0 ? DefaultPath : arguments;
        if (!File.Exists(path))
        {
            Console.WriteLine($"[file not found: {Path.GetFullPath(path)}]");
            return;
        }

        List<string> sentences = (await File.ReadAllLinesAsync(path))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        if (sentences.Count < 2)
        {
            Console.WriteLine($"Usage: /similarity [path]   one sentence per line, at least 2 (found {sentences.Count} in {path}; default {DefaultPath})");
            return;
        }

        Console.WriteLine($"[embedding {sentences.Count} sentences from {path}...]");
        SimilarityMatrix matrix;
        CancellationToken cancellationToken = cancellation.Begin();
        try
        {
            matrix = await similarity.ComputeSimilarityMatrixAsync(sentences, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[similarity cancelled]");
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[error: {ex.Message}]");
            PrintHint(ex);
            return;
        }
        finally
        {
            cancellation.End();
        }

        PrintHeader(matrix);
        PrintGrid(matrix);
        PrintKey(matrix);
    }

    // No status code: nothing answered (connection refused), so Ollama isn't running or the URL is wrong.
    // 404: Ollama answered but doesn't have the model (it isn't pulled).
    private void PrintHint(Exception ex)
    {
        if (ex is HttpRequestException { StatusCode: null })
            Console.WriteLine($"[is Ollama running? Start it, and check Embedding:OllamaUrl ({_metadata?.ProviderUri?.ToString() ?? "unknown"})]");
        else if (ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound } || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"[is the model pulled? Run: ollama pull {_metadata?.DefaultModelId ?? "nomic-embed-text"}]");
    }

    private void PrintHeader(SimilarityMatrix matrix)
    {
        string provider = _metadata?.ProviderName ?? "unknown provider";
        string model = _metadata?.DefaultModelId ?? "unknown model";
        int dimensions = _metadata?.DefaultModelDimensions ?? matrix.Dimensions;
        string tokens = matrix.InputTokens?.ToString() ?? "n/a";
        Console.WriteLine($"Model: {provider}/{model} | Dimensions: {dimensions} | Tokens: {tokens} | Time: {matrix.Elapsed.TotalMilliseconds:0} ms");
    }

    // S1..Sn across the top, one row per sentence; the key underneath keeps the grid narrow
    private static void PrintGrid(SimilarityMatrix matrix)
    {
        int n = matrix.Sentences.Count;
        Console.WriteLine();
        Console.Write(new string(' ', 4));
        for (int j = 0; j < n; j++)
            Console.Write($"{Label(j),6}");
        Console.WriteLine();

        for (int i = 0; i < n; i++)
        {
            Console.Write($"{Label(i),-4}");
            for (int j = 0; j < n; j++)
                PrintCell(matrix.Scores[i, j], diagonal: i == j);
            Console.WriteLine();
        }
    }

    private static void PrintCell(float score, bool diagonal)
    {
        // The diagonal is each sentence against itself (1.00), so colouring it would only add noise.
        // Compared after rounding, so the colour matches the printed number (0.401 prints as 0.40 and is grey too).
        float shown = MathF.Round(score, 2);
        ConsoleColor? colour = diagonal ? null
            : shown >= HighScore ? ConsoleColor.Green
            : shown <= LowScore ? ConsoleColor.DarkGray
            : null;

        if (colour is not null)
            Console.ForegroundColor = colour.Value;
        Console.Write($"{score,6:F2}");
        if (colour is not null)
            Console.ResetColor();
    }

    private static void PrintKey(SimilarityMatrix matrix)
    {
        Console.WriteLine();
        for (int i = 0; i < matrix.Sentences.Count; i++)
            Console.WriteLine($"{Label(i),-4}{matrix.Sentences[i]}");
    }

    private static string Label(int index) => $"S{index + 1}";
}
