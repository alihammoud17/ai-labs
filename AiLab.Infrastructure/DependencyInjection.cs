using AiLab.Domain.Constants;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Settings;
using AiLab.Infrastructure.Files;
using AiLab.Infrastructure.Providers;
using AiLab.Infrastructure.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Infrastructure;

public static class DependencyInjection
{
    // resilience null (console, Rag): a plain OllamaApiClient and the Anthropic and AWS SDK defaults.
    // resilience set (Api): Polly on the Ollama HttpClient, the SDK's own timeout and retry count for Anthropic and Bedrock,
    // so exactly one layer retries per provider.
    // Each host adds its own middleware on top of the keyed provider client (AddChatClient in Program.cs).
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, ResilienceSettings? resilience = null)
    {
        if (resilience is not null)
        {
            // Ollama's HTTP calls go through Polly's standard pipeline: total timeout → retry → circuit breaker → attempt timeout.
            // This is the only retry layer for Ollama (OllamaSharp doesn't retry on its own).
            // Margin on the total timeout: room for the backoff between attempts (about 2 s then 4 s, with jitter).
            TimeSpan ollamaTotalTimeout = resilience.AllAttempts + TimeSpan.FromSeconds(20);
            services.AddHttpClient("ollama", client => client.Timeout = Timeout.InfiniteTimeSpan) // Polly owns the timers, not HttpClient's 100 s default
                .AddStandardResilienceHandler(options =>
                {
                    options.AttemptTimeout.Timeout = resilience.AttemptTimeout;
                    options.Retry.MaxRetryAttempts = resilience.MaxRetries;
                    options.TotalRequestTimeout.Timeout = ollamaTotalTimeout;
                    // The handler's validation requires sampling ≥ 2 × attempt timeout
                    options.CircuitBreaker.SamplingDuration = resilience.AttemptTimeout * 2;
                });
        }

        // The only place that knows which provider is used. Provider and model come from appsettings.json, the API key only
        // from user-secrets. Config is read when the client is first resolved, so test settings are already in place.
        services.AddKeyedSingleton<IChatClient>(ChatClientKeys.Provider, (sp, _) =>
        {
            IConfiguration config = sp.GetRequiredService<IConfiguration>();
            string provider = config["Chat:Provider"] ?? throw new InvalidOperationException("Chat:Provider is missing in appsettings.json.");
            string model = config["Chat:Model"] ?? throw new InvalidOperationException("Chat:Model is missing in appsettings.json.");

            // Three providers, one retry layer each: Polly for Ollama (above), the SDK itself for Anthropic and Bedrock.
            // Anthropic and Bedrock get no Polly: their SDKs already retry (connection errors, timeouts, throttling, 5xx),
            // so they get the same attempt timeout and retry count instead. Polly on top would turn one failure into up to 3 × 3 billed calls.
            // Each branch ignores the other providers' arguments. Bedrock has no key here: the AWS credential chain finds it.
            return ChatClientFactory.Create(provider, model, config["Anthropic:ApiKey"],
                ollamaHttpClient: resilience is not null && provider == "ollama" ? sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama") : null,
                ollamaUrl: config["Chat:OllamaUrl"],
                anthropicAttemptTimeout: resilience?.AttemptTimeout,
                anthropicMaxRetries: resilience?.MaxRetries,
                bedrock: BedrockOptions(config, resilience));
        });

        // Embeddings have their own section (Embedding:*), not Chat:*: the chat model can be Claude, which can't embed, and
        // chat and embeddings can come from different providers (Bedrock chat with Ollama embeddings is a valid setup).
        // Like the chat client, config is read on first resolve, so a host that never asks for it (the API today, the tests)
        // needs no Embedding section. The factory checks the values and names the key to set.
        // Embedding:Dimensions: required for Bedrock, ignored by Ollama. Bedrock:* is the same section the chat client reads.
        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(EmbeddingGeneratorKeys.Provider, (sp, _) =>
        {
            IConfiguration config = sp.GetRequiredService<IConfiguration>();
            string? provider = config["Embedding:Provider"];

            return EmbeddingGeneratorFactory.Create(provider, config["Embedding:Model"],
                ollamaHttpClient: resilience is not null && provider == "ollama" ? sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama") : null,
                ollamaUrl: config["Embedding:OllamaUrl"],
                dimensions: config.GetValue<int?>("Embedding:Dimensions"),
                bedrock: BedrockOptions(config, resilience));
        });

        services.AddSingleton<IProviderErrorClassifier, ProviderErrorClassifier>();
        services.AddSingleton<IChangeLogRepository, InMemoryChangeLogRepository>();
        services.AddSingleton<ITestTicketStore, TestTicketFiles>();
        services.AddSingleton<IJobFileStore, JobFileStore>();
        services.AddSingleton<IEvalRunStore, FileEvalRunStore>();
        services.AddSingleton(TimeProvider.System); // today's date in the system prompt; tests can pin it
        return services;
    }

    // Bedrock:* plus the API's resilience values, the same for the chat client and the embedding generator
    private static BedrockClientOptions BedrockOptions(IConfiguration config, ResilienceSettings? resilience) => new(
        Region: config["Bedrock:Region"],
        ServiceUrl: config["Bedrock:ServiceUrl"],
        EmulatorCaPath: config["Bedrock:EmulatorCaPath"],
        AttemptTimeout: resilience?.AttemptTimeout,
        MaxRetries: resilience?.MaxRetries);
}
