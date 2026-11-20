using AiLab.Domain.Interfaces;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using Anthropic.Exceptions;
using OllamaSharp.Models.Exceptions;
using Polly;

namespace AiLab.Infrastructure.Providers;

// Errors from the provider after its retries. ExecutionRejectedException is Polly giving up on Ollama
// (total timeout reached, circuit open). The only place outside the factory that knows the provider SDKs.
// Bedrock (checked on aws-emulator 2026-11-20):
// - AmazonServiceException: every error the service returns (ValidationException for an unknown model, throttling,
//   AccessDenied on the real account). Like AnthropicException, 4xx included: the caller can't fix it either.
// - BedrockRuntimeEventStreamException: an error inside a ConverseStream; it doesn't derive from AmazonServiceException.
// - TimeoutException: the AWS SDK's attempt timeout (Bedrock:* with the API's resilience settings).
// - Not AmazonClientException: a local problem (no credentials found, bad config) is ours to fix, so it stays a 500.
public class ProviderErrorClassifier : IProviderErrorClassifier
{
    public bool IsProviderFailure(Exception ex) =>
        ex is HttpRequestException or OllamaException or AnthropicException or ExecutionRejectedException
            or AmazonServiceException or BedrockRuntimeEventStreamException or TimeoutException;
}
