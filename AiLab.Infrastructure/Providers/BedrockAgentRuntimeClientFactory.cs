using Amazon.BedrockAgentRuntime;

namespace AiLab.Infrastructure.Providers;

// The one place that builds an AmazonBedrockAgentRuntimeClient (Knowledge Base Retrieve / RetrieveAndGenerate), from the
// same BedrockClientOptions and BedrockClientConfig rules as the runtime client, so the KB is reached at the same endpoint
// with the same TLS and timeouts as Bedrock chat. Creating the client makes no call and resolves no credentials.
internal static class BedrockAgentRuntimeClientFactory
{
    public static AmazonBedrockAgentRuntimeClient Create(BedrockClientOptions options) =>
        new(BedrockClientConfig.Apply(new AmazonBedrockAgentRuntimeConfig(), options));
}
