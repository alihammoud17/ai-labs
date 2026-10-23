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
    // resilience null (console): a plain OllamaApiClient and the Anthropic SDK defaults (10 minutes per attempt, 2 retries).
    // resilience set (Api): Polly on the Ollama HttpClient, the SDK's own Timeout and MaxRetries for Anthropic,
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

            // Anthropic gets no Polly: its SDK already retries (connection errors, timeouts, 408/409/429/5xx, honouring retry-after),
            // so it gets the same attempt timeout and retry count instead. Polly on top would turn one failure into up to 3 × 3 billed calls.
            return ChatClientFactory.Create(provider, model, config["Anthropic:ApiKey"],
                ollamaHttpClient: resilience is not null && provider == "ollama" ? sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama") : null,
                ollamaUrl: config["Chat:OllamaUrl"],
                anthropicAttemptTimeout: resilience?.AttemptTimeout,
                anthropicMaxRetries: resilience?.MaxRetries);
        });

        services.AddSingleton<IProviderErrorClassifier, ProviderErrorClassifier>();
        services.AddSingleton<IChangeLogRepository, InMemoryChangeLogRepository>();
        services.AddSingleton<ITestTicketStore, TestTicketFiles>();
        services.AddSingleton<IJobFileStore, JobFileStore>();
        services.AddSingleton<IEvalRunStore, FileEvalRunStore>();
        services.AddSingleton(TimeProvider.System); // today's date in the system prompt; tests can pin it
        return services;
    }
}
