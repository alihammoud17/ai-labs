using Amazon;
using Amazon.BedrockRuntime;

namespace AiLab.Infrastructure.Providers;

// The one place that builds an AmazonBedrockRuntimeClient. ChatClientFactory wraps it with AsIChatClient,
// EmbeddingGeneratorFactory with AsIEmbeddingGenerator, so Tuesday's endpoint, TLS and timeout rules hold for both.
// Creating the client makes no call and resolves no credentials: they're looked up on the first request.
internal static class BedrockRuntimeClientFactory
{
    public static AmazonBedrockRuntimeClient Create(BedrockClientOptions options)
    {
        string region = string.IsNullOrWhiteSpace(options.Region) ? "us-east-1" : options.Region;
        var config = new AmazonBedrockRuntimeConfig();

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

        return new AmazonBedrockRuntimeClient(config);
    }
}
