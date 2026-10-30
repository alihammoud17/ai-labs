using Microsoft.Extensions.AI;

namespace AiLab.Rag.Tests.Fakes;

// The chat provider at the bottom of the pipeline: plays the scripted steps in order and records what it received.
// The logging middleware in Program.cs runs above it. A step can throw, to stand in for a provider failure.
// Like AiLab.Api.Tests' ScriptedChatClient, without the tool calls (/ask has no tools).
public sealed class ScriptedChatClient(params Func<ChatResponse>[] steps) : IChatClient
{
    private readonly Queue<Func<ChatResponse>> _steps = new(steps);

    // One entry per model call: the messages and options exactly as they were sent
    public List<(List<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((messages.ToList(), options));
        if (!_steps.TryDequeue(out Func<ChatResponse>? step))
            throw new InvalidOperationException($"The script has no reply for model call {Calls.Count}.");
        return Task.FromResult(step());
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("/ask doesn't stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("scripted", null, "scripted-model")
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose() { }

    // The model's reply. Every call reports 200 input and 50 output tokens.
    public static Func<ChatResponse> Reply(string text, ChatFinishReason? finishReason = null) => () =>
        new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            FinishReason = finishReason ?? ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = 200, OutputTokenCount = 50 },
        };

    // The provider call fails (after its retries)
    public static Func<ChatResponse> Throw(Exception ex) => () => throw ex;
}
