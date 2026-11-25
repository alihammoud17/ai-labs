using AiLab.Domain.Outputs;

namespace AiLab.Domain.Interfaces;

// Retrieval from a managed knowledge base (week 8: a Bedrock Knowledge Base), with no generation. The port keeps the
// AWS SDK inside Infrastructure: callers (the evals today) see corpus-relative sources and plain values.
public interface IKnowledgeBaseRetriever
{
    /// <summary>
    /// The <paramref name="top"/> best passages for <paramref name="query"/>, best first, as the knowledge base ranks them.
    /// The service may return fewer (on aws-emulator: one passage per document). Each passage's
    /// <see cref="KnowledgeBasePassage.Score"/> is a relevance score, higher is better. Provider failures are not caught:
    /// they reach <c>IProviderErrorClassifier</c> like any other provider error.
    /// </summary>
    Task<IReadOnlyList<KnowledgeBasePassage>> RetrieveAsync(string query, int top, CancellationToken ct = default);
}
