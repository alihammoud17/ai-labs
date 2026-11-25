using Amazon.BedrockRuntime;

namespace AiLab.Infrastructure.Providers;

// The one place that builds an AmazonBedrockRuntimeClient. ChatClientFactory wraps it with AsIChatClient,
// EmbeddingGeneratorFactory with AsIEmbeddingGenerator, so Tuesday's endpoint, TLS and timeout rules hold for both.
// The rules themselves live in BedrockClientConfig, shared with the Knowledge Base client (BedrockAgentRuntimeClientFactory).
// Creating the client makes no call and resolves no credentials: they're looked up on the first request.
internal static class BedrockRuntimeClientFactory
{
    public static AmazonBedrockRuntimeClient Create(BedrockClientOptions options) =>
        new(BedrockClientConfig.Apply(new AmazonBedrockRuntimeConfig(), options));
}
