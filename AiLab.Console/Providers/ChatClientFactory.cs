using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AiLab.Console.Providers;

public static class ChatClientFactory
{
    public static IChatClient Create(string provider, string model, string? apiKey = null) => provider switch
    {
        "ollama" => new OllamaApiClient(new Uri("http://localhost:11434"), model),

        "anthropic" => new AnthropicClient { ApiKey = apiKey ?? throw new InvalidOperationException("Anthropic:ApiKey is missing.") }
                           .AsIChatClient(model),

        _ => throw new NotSupportedException($"Provider '{provider}' is not supported yet.")
    };
}