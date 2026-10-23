using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AiLab.Infrastructure.Providers;

public static class ChatClientFactory
{
    // Called with only the first three arguments it behaves as before (the console app).
    // The API passes a resilient HttpClient for Ollama and the SDK's own timeout and retry count for Anthropic,
    // so exactly one layer retries per provider.
    public static IChatClient Create(
        string provider,
        string model,
        string? apiKey = null,
        HttpClient? ollamaHttpClient = null,
        string? ollamaUrl = null,
        TimeSpan? anthropicAttemptTimeout = null,
        int? anthropicMaxRetries = null) => provider switch
    {
        "ollama" => ollamaHttpClient is null
            ? new OllamaApiClient(new Uri(ollamaUrl ?? "http://localhost:11434"), model)
            : new OllamaApiClient(WithBaseAddress(ollamaHttpClient, ollamaUrl), model),

        "anthropic" => new AnthropicClient
        {
            ApiKey = apiKey ?? throw new InvalidOperationException("Anthropic:ApiKey is missing."),
            // null keeps the SDK defaults (10 minutes per attempt, 2 retries)
            Timeout = anthropicAttemptTimeout,
            MaxRetries = anthropicMaxRetries,
        }.AsIChatClient(model),

        _ => throw new NotSupportedException($"Provider '{provider}' is not supported yet.")
    };

    // OllamaApiClient(HttpClient, ...) sends to the client's BaseAddress
    private static HttpClient WithBaseAddress(HttpClient client, string? ollamaUrl)
    {
        client.BaseAddress = new Uri(ollamaUrl ?? "http://localhost:11434");
        return client;
    }
}
