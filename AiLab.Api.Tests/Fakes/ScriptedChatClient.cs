using Microsoft.Extensions.AI;

namespace AiLab.Api.Tests.Fakes;

// The provider at the bottom of the pipeline: returns the scripted replies in order and records what it received.
// The real function-invocation and logging middleware run above it, so a scripted tool call really runs the tool.
public class ScriptedChatClient(params ChatResponse[] replies) : IChatClient
{
    private readonly Queue<ChatResponse> _replies = new(replies);

    // One entry per model call: the messages and options exactly as the middleware sent them
    public List<(List<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((messages.ToList(), options));
        if (!_replies.TryDequeue(out ChatResponse? reply))
            throw new InvalidOperationException($"The script has no reply for model call {Calls.Count}.");
        return Task.FromResult(reply);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The API doesn't stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("scripted", null, "scripted-model")
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose() { }

    // The model asks for GetRecentChanges(component)
    public static ChatResponse ToolCall(string component, string callId = "call-1") =>
        new(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(callId, "GetRecentChanges", new Dictionary<string, object?> { ["component"] = component })]))
        {
            FinishReason = ChatFinishReason.ToolCalls,
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 },
        };

    // The model's final text reply
    public static ChatResponse Reply(string text, ChatFinishReason? finishReason = null) =>
        new(new ChatMessage(ChatRole.Assistant, text))
        {
            FinishReason = finishReason ?? ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = 200, OutputTokenCount = 50 },
        };
}
