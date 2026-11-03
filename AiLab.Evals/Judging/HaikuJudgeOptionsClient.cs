using Microsoft.Extensions.AI;

namespace AiLab.Evals.Judging;

// Between MEAI's cache and the Anthropic client. The quality evaluators send Temperature 0, TopP 1 and zero penalties,
// which Haiku 5.5 refuses (it accepts only its default temperature), and MaxOutputTokens 800 (Groundedness) or 1600
// (Retrieval), which its default thinking can use up before the score is written (B7 was cut off at 800 in week 4).
// So: drop the sampling options, raise the output limit to a floor, pass everything else (ResponseFormat, tools) unchanged.
// The caller's ChatOptions are cloned, never changed: MEAI's cache key is computed from them.
public sealed class HaikuJudgeOptionsClient(IChatClient inner, int minimumOutputTokens = HaikuJudgeOptionsClient.DefaultMinimumOutputTokens)
    : DelegatingChatClient(inner)
{
    public const int DefaultMinimumOutputTokens = 2000;

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Adjust(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Adjust(options), cancellationToken);

    internal ChatOptions Adjust(ChatOptions? options)
    {
        ChatOptions adjusted = options?.Clone() ?? new ChatOptions();
        adjusted.Temperature = null;
        adjusted.TopP = null;
        adjusted.PresencePenalty = null;
        adjusted.FrequencyPenalty = null;
        adjusted.MaxOutputTokens = Math.Max(adjusted.MaxOutputTokens ?? 0, minimumOutputTokens);
        return adjusted;
    }
}
