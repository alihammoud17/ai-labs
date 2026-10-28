using System.Security.Cryptography;
using System.Text;
using AiLab.Infrastructure.VectorStore;
using Microsoft.Extensions.AI;

namespace AiLab.Rag.Tests.Fakes;

// A fixed 768-number vector per text, from a hash of the text: the same text always gets the same vector (distance 0),
// different texts get unrelated ones. Records the size of every call and reports characters / 4 as its token usage.
// Throws the given exception instead, if any.
// No model, no network.
public sealed class HashedEmbeddingGenerator(Exception? fail = null) : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<int> BatchSizes { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (fail is not null)
            throw fail;

        string[] texts = values.ToArray();
        BatchSizes.Add(texts.Length);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(texts.Select(t => new Embedding<float>(Vector(t))))
        {
            Usage = new UsageDetails { InputTokenCount = texts.Sum(t => (t.Length + 3) / 4) },
        });
    }

    public static float[] Vector(string text)
    {
        // A stable seed (string.GetHashCode changes between runs)
        var random = new Random(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        return Enumerable.Range(0, TextChunkRecord.Dimensions).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
