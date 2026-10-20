using AiLab.Console.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

IChatClient client = ChatClientFactory.Create(
    "anthropic",
    "claude-haiku-5-5",
    config["Anthropic:ApiKey"]);


// Crude guard against silently overflowing the model's context window.
// Counted in messages, not tokens; replace with token-based trimming once usage is tracked (step 3).
const int MaxHistoryMessages = 20;

// The conversation history. Created once, before the loop,
// so it keeps growing with every turn.
List<ChatMessage> history = [];

// Ctrl+C while a reply is streaming cancels just that reply.
// Ctrl+C at the prompt still exits the app as usual.
CancellationTokenSource? turnCts = null;
Console.CancelKeyPress += (_, e) =>
{
    if (turnCts is null)
        return;

    e.Cancel = true;
    turnCts.Cancel();
};

Console.WriteLine("Chat started. Type /exit to quit. Ctrl+C stops a reply.");

while (true)
{
    Console.Write("\nYou: ");
    string? input = Console.ReadLine();

    // null means input has ended (Ctrl+D / Ctrl+Z or piped input ran out)
    if (input is null)
        break;

    input = input.Trim();

    if (input.Length == 0)
        continue;

    if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
        break;

    // 1. Add your message to the history
    history.Add(new ChatMessage(ChatRole.User, input));

    // 2. Send the WHOLE history, not just the last message
    // 3. Stream and print the answer, keeping every update (they carry usage and finish reason too)
    Console.Write("AI: ");
    List<ChatResponseUpdate> updates = [];
    turnCts = new CancellationTokenSource();
    try
    {
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(history, cancellationToken: turnCts.Token))
        {
            Console.Write(update.Text);
            updates.Add(update);
        }
        Console.WriteLine();
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("\n[reply cancelled, turn discarded]");
        history.RemoveAt(history.Count - 1);
        continue;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[error: {ex.Message}; turn discarded]");
        history.RemoveAt(history.Count - 1);
        continue;
    }
    finally
    {
        turnCts.Dispose();
        turnCts = null;
    }

    // Rebuild the full response from the updates
    ChatResponse response = updates.ToChatResponse();

    // An empty assistant message would break later turns (Anthropic rejects it), so drop the turn
    if (string.IsNullOrWhiteSpace(response.Text))
    {
        Console.WriteLine($"[empty reply (finish reason: {response.FinishReason}), turn discarded]");
        history.RemoveAt(history.Count - 1);
        continue;
    }

    // 4. Save the AI's reply so it remembers it next turn
    history.AddMessages(response);

    // Drop the oldest user/assistant pair so history keeps starting with a user message
    while (history.Count > MaxHistoryMessages)
        history.RemoveRange(0, 2);
}
