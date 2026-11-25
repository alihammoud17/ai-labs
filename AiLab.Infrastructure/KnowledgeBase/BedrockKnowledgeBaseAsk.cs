using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;

namespace AiLab.Infrastructure.KnowledgeBase;

// IKnowledgeBaseAsk over Bedrock's RetrieveAndGenerate, answering with KnowledgeBase:ModelArn. Like the retriever it takes
// the client, so a test can pass a real SDK client with fake HTTP. Sources go through the same CorpusPath as Retrieve's.
public sealed class BedrockKnowledgeBaseAsk(IAmazonBedrockAgentRuntime client, KnowledgeBaseSettings settings) : IKnowledgeBaseAsk
{
    public async Task<KnowledgeBaseAnswer> AskAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        string modelArn = settings.ModelArn
            ?? throw new InvalidOperationException("KnowledgeBase:ModelArn is not set: RetrieveAndGenerate needs the model to answer with.");

        RetrieveAndGenerateResponse response = await client.RetrieveAndGenerateAsync(new RetrieveAndGenerateRequest
        {
            Input = new RetrieveAndGenerateInput { Text = question },
            RetrieveAndGenerateConfiguration = new RetrieveAndGenerateConfiguration
            {
                Type = RetrieveAndGenerateType.KNOWLEDGE_BASE,
                KnowledgeBaseConfiguration = new KnowledgeBaseRetrieveAndGenerateConfiguration
                {
                    KnowledgeBaseId = settings.Id,
                    ModelArn = modelArn,
                },
            },
        }, ct);

        var citations = (response.Citations ?? []).Select(c => new KnowledgeBaseCitation(
            c.GeneratedResponsePart?.TextResponsePart?.Span?.Start,
            c.GeneratedResponsePart?.TextResponsePart?.Span?.End,
            [.. (c.RetrievedReferences ?? []).Select(r => new KnowledgeBaseReference(
                BedrockKnowledgeBaseRetriever.CorpusPath(r.Location), r.Content?.Text ?? ""))])).ToList();

        return new KnowledgeBaseAnswer(response.Output?.Text ?? "", citations, response.SessionId);
    }
}
