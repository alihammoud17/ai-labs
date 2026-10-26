using System.Diagnostics;
using System.Numerics.Tensors;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;

namespace AiLab.Application.Services;

public sealed class SimilarityService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
{
    public async Task<SimilarityMatrix> ComputeSimilarityMatrixAsync(
        IReadOnlyList<string> sentences, CancellationToken cancellationToken = default)
    {
        if (sentences.Count == 0)
            throw new ArgumentException("At least one sentence is required.", nameof(sentences));

        var stopwatch = Stopwatch.StartNew();
        var embeddings = await embeddingGenerator.GenerateAsync(sentences, cancellationToken: cancellationToken);
        stopwatch.Stop();

        int n = sentences.Count;
        if (embeddings.Count != n)
            throw new InvalidOperationException($"Expected {n} embeddings, got {embeddings.Count}.");

        int dimensions = embeddings[0].Vector.Length;
        if (embeddings.Any(e => e.Vector.Length != dimensions))
            throw new InvalidOperationException("Embeddings have different lengths.");

        var scores = new float[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = i; j < n; j++) // j >= i: compute half, mirror the rest
            {
                float score = TensorPrimitives.CosineSimilarity(
                    embeddings[i].Vector.Span, embeddings[j].Vector.Span);
                scores[i, j] = score;
                scores[j, i] = score;
            }
        }

        return new SimilarityMatrix(
            sentences, scores, dimensions, embeddings.Usage?.InputTokenCount, stopwatch.Elapsed);
    }
}