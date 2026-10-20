using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AiLab.Console.Providers;

public static class ChatClientFactory
{
    public static IChatClient Create(string provider, string model) => provider switch
    {
        "ollama" => new OllamaApiClient(new Uri("http://localhost:11434"), model),
        _ => throw new NotSupportedException($"Provider '{provider}' is not supported yet.")
    };
}