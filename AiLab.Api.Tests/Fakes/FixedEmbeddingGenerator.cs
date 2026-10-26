using Microsoft.Extensions.AI;

namespace AiLab.Api.Tests.Fakes;

// Returns the given vectors in order, whatever the input, plus the given usage. No model, no network.
public sealed class FixedEmbeddingGenerator(float[][] vectors, UsageDetails? usage = null) : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        GeneratedEmbeddings<Embedding<float>> embeddings = new(vectors.Select(v => new Embedding<float>(v))) { Usage = usage };
        return Task.FromResult(embeddings);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
