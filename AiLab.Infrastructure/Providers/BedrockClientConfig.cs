using Amazon;
using Amazon.Runtime;

namespace AiLab.Infrastructure.Providers;

// The endpoint, timeout and TLS rules every Bedrock client shares (the runtime client for chat and embeddings, the agent
// runtime client for Knowledge Bases), written once. Both SDK configs derive from ClientConfig, so one method fills either.
internal static class BedrockClientConfig
{
    public const string DefaultRegion = "us-east-1";

    public static TConfig Apply<TConfig>(TConfig config, BedrockClientOptions options) where TConfig : ClientConfig
    {
        string region = string.IsNullOrWhiteSpace(options.Region) ? DefaultRegion : options.Region;

        // Either a service URL (the emulator) or a region endpoint (real AWS), never both:
        // in the AWS SDK, setting one clears the other.
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        }
        else
        {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = region; // requests are still signed for a region
        }

        // null keeps the SDK defaults (its own timeout and retry mode); the API passes its resilience values
        if (options.AttemptTimeout is TimeSpan timeout)
            config.Timeout = timeout;
        if (options.MaxRetries is int retries)
            config.MaxErrorRetry = retries;

        // aws-emulator only: trust its self-signed CA and nothing else. Refused without a service URL, so the emulator's
        // CA can never end up trusted for a real AWS endpoint.
        if (!string.IsNullOrWhiteSpace(options.EmulatorCaPath))
        {
            if (string.IsNullOrWhiteSpace(options.ServiceUrl))
                throw new InvalidOperationException("Bedrock:EmulatorCaPath is set but Bedrock:ServiceUrl isn't: the emulator CA is only for the emulator's URL.");
            config.HttpClientFactory = new EmulatorHttpClientFactory(options.EmulatorCaPath);
        }

        return config;
    }
}
