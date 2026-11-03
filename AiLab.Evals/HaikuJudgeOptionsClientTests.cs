using System.Runtime.CompilerServices;
using AiLab.Evals.Hosting;
using AiLab.Evals.Judging;
using Microsoft.Extensions.AI;

namespace AiLab.Evals;

// Offline: what reaches Haiku after HaikuJudgeOptionsClient, with the options MEAI's quality evaluators send.
public class HaikuJudgeOptionsClientTests
{
    // As GroundednessEvaluator sends them (RetrievalEvaluator: the same with 1600)
    private static ChatOptions EvaluatorOptions(int? maxOutputTokens = 800) => new()
    {
        Temperature = 0.0f,
        MaxOutputTokens = maxOutputTokens,
        TopP = 1.0f,
        PresencePenalty = 0.0f,
        FrequencyPenalty = 0.0f,
        ResponseFormat = ChatResponseFormat.Text,
    };

    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "Rate this.")];

    [Fact]
    public async Task Drops_temperature_top_p_and_penalties()
    {
        RecordingChatClient inner = new();

        await new HaikuJudgeOptionsClient(inner).GetResponseAsync(Messages, EvaluatorOptions(), TestContext.Current.CancellationToken);

        ChatOptions sent = Assert.Single(inner.Options)!;
        Assert.Null(sent.Temperature);
        Assert.Null(sent.TopP);
        Assert.Null(sent.PresencePenalty);
        Assert.Null(sent.FrequencyPenalty);
    }

    [Theory]
    [InlineData(800, 2000)]
    [InlineData(1600, 2000)]
    [InlineData(3000, 3000)]
    [InlineData(null, 2000)]
    public async Task Raises_MaxOutputTokens_to_the_floor_but_never_lowers_it(int? requested, int expected)
    {
        RecordingChatClient inner = new();

        await new HaikuJudgeOptionsClient(inner).GetResponseAsync(Messages, EvaluatorOptions(requested), TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.Single(inner.Options)!.MaxOutputTokens);
    }

    [Fact]
    public async Task No_options_still_gets_the_floor()
    {
        RecordingChatClient inner = new();

        await new HaikuJudgeOptionsClient(inner).GetResponseAsync(Messages, null, TestContext.Current.CancellationToken);

        Assert.Equal(HaikuJudgeOptionsClient.DefaultMinimumOutputTokens, Assert.Single(inner.Options)!.MaxOutputTokens);
    }

    [Fact]
    public async Task Keeps_the_response_format_and_the_messages()
    {
        RecordingChatClient inner = new();

        await new HaikuJudgeOptionsClient(inner).GetResponseAsync(Messages, EvaluatorOptions(), TestContext.Current.CancellationToken);

        Assert.Same(ChatResponseFormat.Text, Assert.Single(inner.Options)!.ResponseFormat);
        Assert.Equal("Rate this.", Assert.Single(Assert.Single(inner.Messages)).Text);
    }

    // MEAI computes the cache key from the caller's options, so they must stay as they were
    [Fact]
    public async Task Never_changes_the_callers_options()
    {
        RecordingChatClient inner = new();
        ChatOptions options = EvaluatorOptions();

        await new HaikuJudgeOptionsClient(inner).GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        Assert.Equal(0.0f, options.Temperature);
        Assert.Equal(1.0f, options.TopP);
        Assert.Equal(0.0f, options.PresencePenalty);
        Assert.Equal(0.0f, options.FrequencyPenalty);
        Assert.Equal(800, options.MaxOutputTokens);
        Assert.NotSame(options, inner.Options[0]);
    }

    [Fact]
    public async Task Streaming_gets_the_same_treatment()
    {
        RecordingChatClient inner = new();

        await foreach (ChatResponseUpdate _ in new HaikuJudgeOptionsClient(inner)
            .GetStreamingResponseAsync(Messages, EvaluatorOptions(), TestContext.Current.CancellationToken))
        {
        }

        ChatOptions sent = Assert.Single(inner.Options)!;
        Assert.Null(sent.Temperature);
        Assert.Null(sent.TopP);
        Assert.Equal(2000, sent.MaxOutputTokens);
    }

    [Fact]
    public async Task ScenarioChatClient_forwards_to_its_target_and_refuses_without_one()
    {
        RecordingChatClient target = new();
        ScenarioChatClient client = new();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(Messages, null, TestContext.Current.CancellationToken));

        client.Target = target;
        ChatResponse response = await client.GetResponseAsync(Messages, null, TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Text);
        Assert.Single(target.Messages);
    }

    // Records what it was sent and answers "ok"
    private sealed class RecordingChatClient : IChatClient
    {
        public List<ChatOptions?> Options { get; } = [];
        public List<List<ChatMessage>> Messages { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages.Add([.. messages]);
            Options.Add(options);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Messages.Add([.. messages]);
            Options.Add(options);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
