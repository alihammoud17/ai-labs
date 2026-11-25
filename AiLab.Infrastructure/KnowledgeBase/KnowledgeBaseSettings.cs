using Amazon.BedrockAgentRuntime;
using Microsoft.Extensions.Configuration;

namespace AiLab.Infrastructure.KnowledgeBase;

// KnowledgeBase:* (week 8 Tuesday). Id is the Bedrock Knowledge Base ID (on aws-emulator: KB_ID from ai-lab-kb.env).
// SearchType: SEMANTIC or HYBRID, null = Bedrock decides (HYBRID works only on OpenSearch Serverless with a text field).
// ModelArn: the model RetrieveAndGenerate answers with (Block 5), null when only Retrieve is used.
// The endpoint, region and emulator CA are Bedrock:*, the same section the chat and embedding clients read.
public sealed record KnowledgeBaseSettings(string Id, string? SearchType = null, string? ModelArn = null)
{
    public const string Section = "KnowledgeBase";

    // null when KnowledgeBase:Id isn't set: no knowledge base configured. A bad SearchType stops here, at registration.
    public static KnowledgeBaseSettings? From(IConfiguration configuration)
    {
        string? id = configuration[$"{Section}:Id"]?.Trim();
        if (string.IsNullOrEmpty(id))
            return null;

        string? searchType = configuration[$"{Section}:SearchType"]?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(searchType))
            searchType = null;
        else if (searchType != Amazon.BedrockAgentRuntime.SearchType.SEMANTIC.Value && searchType != Amazon.BedrockAgentRuntime.SearchType.HYBRID.Value)
            throw new InvalidOperationException($"{Section}:SearchType must be SEMANTIC or HYBRID (or unset), not '{searchType}'.");

        string? modelArn = configuration[$"{Section}:ModelArn"]?.Trim();
        return new KnowledgeBaseSettings(id, searchType, string.IsNullOrEmpty(modelArn) ? null : modelArn);
    }
}
