using Microsoft.Extensions.AI;

namespace AiLab.Api.Tests.Fakes;

// Turns each text into a vector with the given function and records every call (its size and the exact texts). No model, no network.
// Unlike FixedEmbeddingGenerator, each input gets its own vector, so batched calls line up.
// tokensPerText null: the call reports no usage, like a provider that doesn't count tokens.
public sealed class MappedEmbeddingGenerator(Func<string, float[]> map, int? tokensPerText = null) : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<int> BatchSizes { get; } = [];

    public List<string> Inputs { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        string[] texts = values.ToArray();
        BatchSizes.Add(texts.Length);
        Inputs.AddRange(texts);
        GeneratedEmbeddings<Embedding<float>> embeddings = new(texts.Select(t => new Embedding<float>(map(t))))
        {
            Usage = tokensPerText is null ? null : new UsageDetails { InputTokenCount = texts.Length * tokensPerText },
        };
        return Task.FromResult(embeddings);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
