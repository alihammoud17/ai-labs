using AiLab.Domain.Interfaces;
using Anthropic.Exceptions;
using OllamaSharp.Models.Exceptions;
using Polly;

namespace AiLab.Infrastructure.Providers;

// Errors from the provider after its retries. ExecutionRejectedException is Polly giving up on Ollama
// (total timeout reached, circuit open). The only place outside the factory that knows the provider SDKs.
public class ProviderErrorClassifier : IProviderErrorClassifier
{
    public bool IsProviderFailure(Exception ex) =>
        ex is HttpRequestException or OllamaException or AnthropicException or ExecutionRejectedException;
}
