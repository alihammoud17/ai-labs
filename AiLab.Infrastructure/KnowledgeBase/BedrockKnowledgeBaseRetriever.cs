using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;

namespace AiLab.Infrastructure.KnowledgeBase;

// IKnowledgeBaseRetriever over Bedrock's Retrieve (the agent runtime API). Public, and it takes the client rather than
// building one, so a test can pass a real SDK client with fake credentials and fake HTTP (BedrockKnowledgeBaseRetrieverTests);
// in DI the client comes from BedrockAgentRuntimeClientFactory (AddKnowledgeBase). AWS errors are not caught here.
public sealed class BedrockKnowledgeBaseRetriever(IAmazonBedrockAgentRuntime client, KnowledgeBaseSettings settings) : IKnowledgeBaseRetriever
{
    public const int MaxTop = 100; // Retrieve's numberOfResults range is 1–100

    public async Task<IReadOnlyList<KnowledgeBasePassage>> RetrieveAsync(string query, int top, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(top, MaxTop);

        var search = new KnowledgeBaseVectorSearchConfiguration { NumberOfResults = top };
        if (settings.SearchType is not null)
            search.OverrideSearchType = SearchType.FindValue(settings.SearchType);

        RetrieveResponse response = await client.RetrieveAsync(new RetrieveRequest
        {
            KnowledgeBaseId = settings.Id,
            RetrievalQuery = new KnowledgeBaseQuery { Text = query },
            RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration { VectorSearchConfiguration = search },
        }, ct);

        // Rank = the order the service returned them (best first); the score is kept as it came (relevance, higher is better)
        return [.. (response.RetrievalResults ?? []).Select((r, i) => new KnowledgeBasePassage(
            CorpusPath(r.Location),
            r.Content?.Text ?? "",
            r.Score ?? 0,
            i + 1))];
    }

    // s3://ai-lab-docs/docs/contracts.md → docs/contracts.md: the key under the bucket, which is the path relative to the
    // corpus folder the bucket was synced from (docker/bedrock-kb/bedrock-kb-emulator.sh). Any other location type is a KB
    // this adapter wasn't written for: fail with what came back rather than invent a source no gold line can match.
    public static string CorpusPath(RetrievalResultLocation? location)
    {
        string? uri = location?.S3Location?.Uri;
        if (uri is null || !uri.StartsWith("s3://", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Knowledge Base result without an S3 location (type '{location?.Type?.Value ?? "none"}'): only S3 data sources are supported.");

        int slash = uri.IndexOf('/', "s3://".Length);
        if (slash < 0 || slash == uri.Length - 1)
            throw new InvalidOperationException($"Knowledge Base result URI '{uri}' has no object key after the bucket.");
        return Uri.UnescapeDataString(uri[(slash + 1)..]);
    }
}
