using AiLab.Console.Pricing;
using AiLab.Console.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

// Provider and model come from appsettings.json; the API key only from user-secrets
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>()
    .Build();

string provider = config["Chat:Provider"] ?? throw new InvalidOperationException("Chat:Provider is missing in appsettings.json.");
string model = config["Chat:Model"] ?? throw new InvalidOperationException("Chat:Model is missing in appsettings.json.");

IChatClient client = ChatClientFactory.Create(provider, model, config["Anthropic:ApiKey"]);

// Running cost of this session, across all turns
decimal sessionTotal = 0;

// Crude guard against silently overflowing the model's context window.
// Counted in messages, not tokens; replace with token-based trimming once usage is tracked (step 3).
const int MaxHistoryMessages = 20;

// The conversation history. Created once, before the loop,
// so it keeps growing with every turn.
List<ChatMessage> history = [];

// Settings sent with every request. Change the values here between experiment runs (step 4);
// an empty ChatOptions means "use the provider's defaults".
ChatOptions options = new() 
{ 
    Temperature = 1.0f 
};

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

Console.WriteLine($"Chat started with {provider}/{model}. Type /reset for a fresh conversation, /exit to quit. Ctrl+C stops a reply.");

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

    // Start a fresh conversation; system messages stay so a format instruction survives (step 4.4).
    // The session cost keeps counting, since that money is already spent.
    if (input.Equals("/reset", StringComparison.OrdinalIgnoreCase))
    {
        history.RemoveAll(m => m.Role != ChatRole.System);
        Console.WriteLine("[conversation reset]");
        continue;
    }

    // 1. Add your message to the history
    history.Add(new ChatMessage(ChatRole.User, input));

    // 2. Send the WHOLE history, not just the last message
    // 3. Stream and print the answer, keeping every update (they carry usage and finish reason too)
    Console.Write("AI: ");
    List<ChatResponseUpdate> updates = [];
    turnCts = new CancellationTokenSource();
    try
    {
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(history, options, turnCts.Token))
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

    // Usage arrives inside the updates; it's billed even if the reply turns out empty below
    if (response.Usage?.InputTokenCount is long inputTokens && response.Usage.OutputTokenCount is long outputTokens)
    {
        decimal? turnCost = ModelPrices.Cost(model, inputTokens, outputTokens);
        if (turnCost is null)
        {
            Console.WriteLine($"Tokens: in={inputTokens}, out={outputTokens} | Turn: unknown price for '{model}'");
        }
        else
        {
            sessionTotal += turnCost.Value;
            Console.WriteLine($"Tokens: in={inputTokens}, out={outputTokens} | Turn: ${turnCost:0.000000} | Session: ${sessionTotal:0.000000}");
        }
    }
    else
    {
        Console.WriteLine("Tokens: unknown");
    }

    // An empty assistant message would break later turns (Anthropic rejects it), so drop the turn
    if (string.IsNullOrWhiteSpace(response.Text))
    {
        Console.WriteLine($"[empty reply (finish reason: {response.FinishReason}), turn discarded]");
        history.RemoveAt(history.Count - 1);
        continue;
    }

    // 4. Save the AI's reply so it remembers it next turn
    history.AddMessages(response);

    // Drop the oldest user/assistant pair so the conversation keeps starting with a user message.
    // System messages are never trimmed or counted, so a format instruction (step 4.4) stays in place.
    while (history.Count(m => m.Role != ChatRole.System) > MaxHistoryMessages)
    {
        int oldest = history.FindIndex(m => m.Role != ChatRole.System);
        history.RemoveRange(oldest, 2);
    }
}
