using AiLab.Application.Services;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;

namespace AiLab.Console.Chat;

// Inside AiLab.Console.*, a bare "Console" would mean the AiLab.Console namespace
using Console = System.Console;

// The chat loop: reads commands, streams replies and prints. The history and costs are in ChatConversation,
// /extract and /eval in JobCommands. The experiment switches are in ChatExperimentSettings (AiLab.Application).
public class ChatSession(IChatClient client, ChatConversation conversation, JobCommands jobs, TurnCancellation cancellation)
{
    // Provider and model as the client reports them, for the welcome line
    private readonly ChatClientMetadata? _metadata = client.GetService<ChatClientMetadata>();

    public async Task RunAsync()
    {
        // Ctrl+C while a reply is streaming cancels just that reply.
        // Ctrl+C at the prompt still exits the app as usual.
        Console.CancelKeyPress += (_, e) =>
        {
            if (!cancellation.IsActive)
                return;

            e.Cancel = true;
            cancellation.Cancel();
        };

        Console.WriteLine($"Chat started with {_metadata?.ProviderName ?? "unknown provider"}/{_metadata?.DefaultModelId ?? "unknown model"}. Type /extract [v1|v2] <path> to extract a job posting, /eval <v1|v2> [runs] to score a prompt on all inputs, /reset for a fresh conversation, /exit to quit. Ctrl+C stops a reply.");

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
                conversation.Reset();
                Console.WriteLine("[conversation reset]");
                continue;
            }

            // Typed extraction (deliverable 3); separate from the chat, so history is untouched
            if (input.Equals("/extract", StringComparison.OrdinalIgnoreCase) || input.StartsWith("/extract ", StringComparison.OrdinalIgnoreCase))
            {
                await jobs.ExtractAsync(input["/extract".Length..].Trim());
                continue;
            }

            // Repeated scored runs over every input with a golden answer; also separate from the chat
            if (input.Equals("/eval", StringComparison.OrdinalIgnoreCase) || input.StartsWith("/eval ", StringComparison.OrdinalIgnoreCase))
            {
                await jobs.EvalAsync(input["/eval".Length..].Trim());
                continue;
            }

            await RunTurnAsync(input);
        }
    }

    private async Task RunTurnAsync(string input)
    {
        // Stream and print the answer, keeping every update (they carry usage and finish reason too)
        Console.Write("AI: ");
        List<ChatResponseUpdate> updates = [];
        CancellationToken cancellationToken = cancellation.Begin();
        try
        {
            await foreach (ChatResponseUpdate update in conversation.StartTurn(input, cancellationToken))
            {
                Console.Write(update.Text);
                updates.Add(update);
            }
            Console.WriteLine();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[reply cancelled, turn discarded]");
            conversation.DiscardTurn();
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[error: {ex.Message}; turn discarded]");
            conversation.DiscardTurn();
            return;
        }
        finally
        {
            cancellation.End();
        }

        ChatTurnResult turn = conversation.CompleteTurn(updates);

        UsageLine.Print(turn.Usage);

        if (turn.CutOff)
            Console.WriteLine("Warning: answer was cut off (output token limit reached)");

        if (turn.Empty)
        {
            Console.WriteLine($"[empty reply (finish reason: {turn.Usage.FinishReason}), turn discarded]");
            return;
        }

        if (turn.JsonValid == true)
            Console.WriteLine("Valid JSON");
        else if (turn.JsonValid == false)
            Console.WriteLine($"Invalid JSON: {turn.JsonError}");
    }
}
