using Microsoft.Extensions.AI;

namespace AiLab.Evals.Hosting;

// The keyed "provider" chat client of the eval host. /ask's generation goes to whatever Target is set for the current
// iteration: the ScenarioRun's cached client (Haiku answers, cached with the judges' calls) or a plain Ollama client.
// A plain field, not an AsyncLocal: TestServer doesn't flow the test's ExecutionContext into the request by default.
// That's safe because the iterations of one eval class run one after another, and each class has its own host.
public sealed class ScenarioChatClient : IChatClient
{
    private volatile IChatClient? _target;

    public IChatClient? Target
    {
        get => _target;
        set => _target = value;
    }

    private IChatClient Current => _target
        ?? throw new InvalidOperationException("No chat client is set for this iteration: set ScenarioChatClient.Target before calling /ask.");

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Current.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Current.GetStreamingResponseAsync(messages, options, cancellationToken);

    // Metadata (provider, model) comes from the current target, so logs and usage name the real model
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ScenarioChatClient) ? this : _target?.GetService(serviceType, serviceKey);

    // The targets belong to their owners (the ScenarioRun, the fixture)
    public void Dispose() { }
}
