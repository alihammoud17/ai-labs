using Amazon.BedrockRuntime;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AiLab.Infrastructure.Providers;

public static class EmbeddingGeneratorFactory
{
    // Two providers: Ollama (local, free) and Bedrock (Titan Text Embeddings v2 and others). Anthropic has no embeddings API,
    // so "anthropic" fails loudly instead of falling back.
    // Vector size: Ollama's model decides it (nomic-embed-text 768, all-minilm 384), so `dimensions` is ignored there.
    // Bedrock models take a size in the request (Titan v2: 256, 512 or 1024), so `dimensions` is required: it must match the
    // vector store's size, and no Titan size is 768.
    // Ollama: its own OllamaApiClient, separate from the chat client; the API can pass the resilient "ollama" HttpClient.
    // Bedrock: the client comes from BedrockRuntimeClientFactory, like the chat client's (same endpoint, TLS, timeouts).
    public static IEmbeddingGenerator<string, Embedding<float>> Create(
        string? provider,
        string? model,
        HttpClient? ollamaHttpClient = null,
        string? ollamaUrl = null,
        int? dimensions = null,
        BedrockClientOptions? bedrock = null)
    {
        // Checked here, not in AddInfrastructure, so an empty value fails the same way as a missing one and the tests can reach it
        if (string.IsNullOrWhiteSpace(provider))
            throw new InvalidOperationException("Embedding:Provider is missing. Set it in appsettings.json to \"ollama\" or \"bedrock\".");
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(
                "Embedding:Model is missing. Set it in appsettings.json (for example \"nomic-embed-text\" on Ollama, \"amazon.titan-embed-text-v2:0\" on Bedrock).");

        return provider switch
        {
            "ollama" => ollamaHttpClient is null
                ? new OllamaApiClient(OllamaUri(ollamaUrl), model)
                : new OllamaApiClient(WithBaseAddress(ollamaHttpClient, ollamaUrl), model),

            // One InvokeModel per text, body {"inputText","dimensions"}; the size is sent with every request
            "bedrock" => BedrockRuntimeClientFactory.Create(bedrock ?? new BedrockClientOptions())
                .AsIEmbeddingGenerator(model, BedrockDimensions(dimensions, model)),

            "anthropic" => throw new NotSupportedException(
                "Embedding:Provider 'anthropic' is not supported: Anthropic has no embeddings API. Set Embedding:Provider to \"ollama\" or \"bedrock\"."),

            _ => throw new NotSupportedException($"Embedding:Provider '{provider}' is not supported. Set Embedding:Provider to \"ollama\" or \"bedrock\".")
        };
    }

    // Required, not defaulted: Titan would answer 1024 floats without it, and the store (768 by default) would refuse every one
    private static int BedrockDimensions(int? dimensions, string model) => dimensions switch
    {
        null => throw new InvalidOperationException(
            $"Embedding:Dimensions is missing. Bedrock embedding models need a size (for {model}: 256, 512 or 1024 on Titan v2), " +
            "and it must match the vector store's size."),
        <= 0 => throw new InvalidOperationException($"Embedding:Dimensions must be positive, but is {dimensions}."),
        _ => dimensions.Value,
    };

    private static Uri OllamaUri(string? ollamaUrl) =>
        new(string.IsNullOrWhiteSpace(ollamaUrl) ? "http://localhost:11434" : ollamaUrl);

    // OllamaApiClient(HttpClient, ...) sends to the client's BaseAddress
    private static HttpClient WithBaseAddress(HttpClient client, string? ollamaUrl)
    {
        client.BaseAddress = OllamaUri(ollamaUrl);
        return client;
    }
}
