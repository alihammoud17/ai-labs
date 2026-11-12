using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AiLab.Application.Services;

// The chat history, one turn at a time. It only knows IChatClient, never which provider is behind it.
// The caller streams the reply (StartTurn), then hands the updates back (CompleteTurn) or drops the turn (DiscardTurn).
public class ChatConversation
{
    private readonly IChatClient _client;
    private readonly SessionCostTracker _cost;

    // The settings from ChatExperimentSettings, sent with every request
    private readonly ChatOptions _options = ChatExperimentSettings.CreateOptions();

    // The conversation history. Created once, so it keeps growing with every turn.
    private readonly List<ChatMessage> _history = [];

    public ChatConversation(IChatClient client, SessionCostTracker cost)
    {
        _client = client;
        _cost = cost;

        if (ChatExperimentSettings.JsonSystemMessage is not null)
            _history.Add(new ChatMessage(ChatRole.System, ChatExperimentSettings.JsonSystemMessage));
    }

    // The tools the model may call, and the instructions that go with them, sent from the next turn on (/saas on).
    // Instructions travel with each request (ChatOptions.Instructions) and are never stored in the history,
    // so turning tools off leaves no system message behind. null for both = back to ChatExperimentSettings' options (/saas off).
    public void SetTools(IList<AITool>? tools, string? instructions)
    {
        _options.Tools = tools;
        _options.Instructions = instructions;
    }

    // Start a fresh conversation; system messages stay so a format instruction survives (step 4.4).
    // The session cost keeps counting, since that money is already spent.
    public void Reset() => _history.RemoveAll(m => m.Role != ChatRole.System);

    // 1. Add your message to the history
    // 2. Send the WHOLE history, not just the last message
    public IAsyncEnumerable<ChatResponseUpdate> StartTurn(string input, CancellationToken cancellationToken)
    {
        _history.Add(new ChatMessage(ChatRole.User, input));
        return _client.GetStreamingResponseAsync(_history, _options, cancellationToken);
    }

    // A cancelled or failed reply: the user's message goes too
    public void DiscardTurn() => _history.RemoveAt(_history.Count - 1);

    // 3. Every streamed update (they carry usage and finish reason too) back into one response
    public ChatTurnResult CompleteTurn(List<ChatResponseUpdate> updates)
    {
        // Rebuild the full response from the updates
        ChatResponse response = updates.ToChatResponse();

        TurnUsage usage = _cost.Add(response);

        // A cut-off answer looks like normal text that just ends; only the finish reason says it's incomplete.
        // On a thinking model (Haiku 5.5) the limit can be used up by thinking, leaving no text at all.
        bool cutOff = response.FinishReason == ChatFinishReason.Length;

        // An empty assistant message would break later turns (Anthropic rejects it), so drop the turn
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            DiscardTurn();
            return new ChatTurnResult(usage, cutOff, Empty: true, JsonValid: null, JsonError: null);
        }

        // Strict check on purpose: a reply wrapped in ```json fences counts as invalid, like it would for a real parser
        bool? jsonValid = null;
        string? jsonError = null;
        if (ChatExperimentSettings.JsonSystemMessage is not null)
        {
            try
            {
                using JsonDocument _ = JsonDocument.Parse(response.Text);
                jsonValid = true;
            }
            catch (JsonException ex)
            {
                jsonValid = false;
                jsonError = ex.Message;
            }
        }

        // 4. Save the AI's reply so it remembers it next turn
        _history.AddMessages(response);

        // Drop the oldest whole turn: from its user message up to (not including) the next user message.
        // A plain turn is 2 messages, but a tool turn is more (user → assistant call → tool result → assistant text),
        // and cutting one in half leaves a tool result without its call, which Anthropic rejects (400).
        // So the conversation always starts with a user message. System messages are never trimmed or counted,
        // so a format instruction (step 4.4) stays in place. The newest turn is never dropped, even if it alone is over the limit.
        while (_history.Count(m => m.Role != ChatRole.System) > ChatExperimentSettings.MaxHistoryMessages)
        {
            int oldest = _history.FindIndex(m => m.Role != ChatRole.System);
            int next = _history.FindIndex(oldest + 1, m => m.Role == ChatRole.User);
            if (next < 0)
                break;

            _history.RemoveRange(oldest, next - oldest);
        }

        return new ChatTurnResult(usage, cutOff, Empty: false, jsonValid, jsonError);
    }
}
