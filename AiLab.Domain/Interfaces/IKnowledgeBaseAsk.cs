using AiLab.Domain.Outputs;

namespace AiLab.Domain.Interfaces;

// The managed one-call RAG path (week 8: Bedrock's RetrieveAndGenerate): retrieval and generation by the service, with no
// gate and the service's prompt. Separate from IKnowledgeBaseRetriever, so retrieval-only callers (the retrieval evals, a
// future Retrieve + own gate) don't depend on generation, and a knowledge base that can't generate still retrieves.
public interface IKnowledgeBaseAsk
{
    /// <summary>
    /// Asks the knowledge base one question, in a new session. The service always generates an answer (there is no
    /// abstain status); citations point from answer spans to passages. Provider failures are not caught.
    /// </summary>
    Task<KnowledgeBaseAnswer> AskAsync(string question, CancellationToken ct = default);
}
