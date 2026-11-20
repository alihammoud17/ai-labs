using Amazon;
using Amazon.BedrockRuntime;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AiLab.Infrastructure.Providers;

public static class ChatClientFactory
{
    // Called with only the first three arguments it behaves as before (the console app).
    // The API passes a resilient HttpClient for Ollama and the SDK's own timeout and retry count for Anthropic and Bedrock,
    // so exactly one layer retries per provider.
    public static IChatClient Create(
        string provider,
        string model,
        string? apiKey = null,
        HttpClient? ollamaHttpClient = null,
        string? ollamaUrl = null,
        TimeSpan? anthropicAttemptTimeout = null,
        int? anthropicMaxRetries = null,
        string? bedrockRegion = null,
        string? bedrockServiceUrl = null,
        TimeSpan? bedrockAttemptTimeout = null,
        int? bedrockMaxRetries = null) => provider switch
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

        "bedrock" => CreateBedrock(model, bedrockRegion, bedrockServiceUrl, bedrockAttemptTimeout, bedrockMaxRetries),

        _ => throw new NotSupportedException($"Provider '{provider}' is not supported yet.")
    };

    // No credentials here: the AWS SDK's credential chain finds them (the emulator: AWS_SHARED_CREDENTIALS_FILE from
    // ai-lab-kb.env; the real account: the ai-lab-dev profile via AWS_PROFILE), so keys never sit in config.
    private static IChatClient CreateBedrock(string model, string? region, string? serviceUrl, TimeSpan? attemptTimeout, int? maxRetries)
    {
        region = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region;
        var config = new AmazonBedrockRuntimeConfig();

        // Either a service URL (the emulator) or a region endpoint (real AWS), never both:
        // in the AWS SDK, setting one clears the other.
        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        }
        else
        {
            config.ServiceURL = serviceUrl;
            config.AuthenticationRegion = region; // requests are still signed for a region
        }

        // null keeps the SDK defaults (its own timeout and retry mode); the API passes its resilience values
        if (attemptTimeout is TimeSpan timeout)
            config.Timeout = timeout;
        if (maxRetries is int retries)
            config.MaxErrorRetry = retries;

        // Converse for GetResponseAsync, ConverseStream for GetStreamingResponseAsync
        return new AmazonBedrockRuntimeClient(config).AsIChatClient(model);
    }

    // OllamaApiClient(HttpClient, ...) sends to the client's BaseAddress
    private static HttpClient WithBaseAddress(HttpClient client, string? ollamaUrl)
    {
        client.BaseAddress = new Uri(ollamaUrl ?? "http://localhost:11434");
        return client;
    }
}
