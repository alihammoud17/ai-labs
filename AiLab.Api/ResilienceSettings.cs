namespace AiLab.Api;

// The "Resilience" section of appsettings.json: timeouts, retries and the tool-loop cap in one place
public record ResilienceSettings
{
    // One HTTP call to the provider (Polly's attempt timeout for Ollama, the SDK's Timeout for Anthropic)
    public int AttemptTimeoutSeconds { get; init; } = 60;

    // Retries after the first attempt, done by exactly one layer per provider
    public int MaxRetries { get; init; } = 2;

    // The whole POST /analyze-ticket, tool loop included (ASP.NET Core request timeouts)
    public int RequestTimeoutSeconds { get; init; } = 240;

    // Model calls per request, the first one included (FunctionInvokingChatClient.MaximumIterationsPerRequest)
    public int MaxToolIterations { get; init; } = 3;

    public TimeSpan AttemptTimeout => TimeSpan.FromSeconds(AttemptTimeoutSeconds);

    // Worst case for one model call: every attempt runs into its timeout
    public TimeSpan AllAttempts => AttemptTimeout * (MaxRetries + 1);
}
