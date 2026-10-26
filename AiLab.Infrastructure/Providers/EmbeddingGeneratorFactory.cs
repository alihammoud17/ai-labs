using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AiLab.Infrastructure.Providers;

public static class EmbeddingGeneratorFactory
{
    // Only Ollama: Anthropic has no embeddings API, so "anthropic" fails loudly instead of falling back.
    // The model decides the vector size (nomic-embed-text 768, all-minilm 384), so switching models is a config change.
    // Its own OllamaApiClient, separate from the chat client, with the embedding model as its default.
    // The API can pass the resilient "ollama" HttpClient, like ChatClientFactory; the console passes none.
    public static IEmbeddingGenerator<string, Embedding<float>> Create(
        string? provider,
        string? model,
        HttpClient? ollamaHttpClient = null,
        string? ollamaUrl = null)
    {
        // Checked here, not in AddInfrastructure, so an empty value fails the same way as a missing one and the tests can reach it
        if (string.IsNullOrWhiteSpace(provider))
            throw new InvalidOperationException("Embedding:Provider is missing. Set it in appsettings.json (only \"ollama\" is supported).");
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Embedding:Model is missing. Set it in appsettings.json (for example \"nomic-embed-text\").");

        return provider switch
        {
            "ollama" => ollamaHttpClient is null
                ? new OllamaApiClient(OllamaUri(ollamaUrl), model)
                : new OllamaApiClient(WithBaseAddress(ollamaHttpClient, ollamaUrl), model),

            "anthropic" => throw new NotSupportedException(
                "Embedding:Provider 'anthropic' is not supported: Anthropic has no embeddings API. Set Embedding:Provider to \"ollama\"."),

            _ => throw new NotSupportedException($"Embedding:Provider '{provider}' is not supported. Set Embedding:Provider to \"ollama\".")
        };
    }

    private static Uri OllamaUri(string? ollamaUrl) =>
        new(string.IsNullOrWhiteSpace(ollamaUrl) ? "http://localhost:11434" : ollamaUrl);

    // OllamaApiClient(HttpClient, ...) sends to the client's BaseAddress
    private static HttpClient WithBaseAddress(HttpClient client, string? ollamaUrl)
    {
        client.BaseAddress = OllamaUri(ollamaUrl);
        return client;
    }
}
