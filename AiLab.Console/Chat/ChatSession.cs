using AiLab.Application.Services;
using AiLab.Console.Agents;
using AiLab.Domain.Outputs;
using AiLab.Infrastructure.Mcp;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AiLab.Console.Chat;

// Inside AiLab.Console.*, a bare "Console" would mean the AiLab.Console namespace
using Console = System.Console;

// The chat loop: reads commands, streams replies and prints. The history and costs are in ChatConversation,
// /extract and /eval in JobCommands, /similarity in EmbeddingCommands, the SaaS API's MCP tools (/saas) in SaasMcpToolSource,
// the triage agent (/agent) in AgentCommands.
// The experiment switches are in ChatExperimentSettings (AiLab.Application).
public class ChatSession(IChatClient client, ChatConversation conversation, JobCommands jobs, EmbeddingCommands embeddings, TurnCancellation cancellation, SaasMcpToolSource saasTools, AgentCommands agents)
{
    // Sent with every request while /saas is on (ChatOptions.Instructions, never stored in the history)
    private const string SaasInstructions =
        "You can call tools that read the SaaS API's data for the logged-in tenant only; they cannot see any other tenant. " +
        "Answer only from what the tools return, and say so when they return nothing or an error; never invent data.";

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

        Console.WriteLine($"Chat started with {_metadata?.ProviderName ?? "unknown provider"}/{_metadata?.DefaultModelId ?? "unknown model"}. Type /extract [v1|v2] <path> to extract a job posting, /eval <v1|v2> [runs] to score a prompt on all inputs, /similarity [path] for a similarity matrix, /saas on|off for the SaaS API's tools, /agent <ticket> for the triage agent, /reset for a fresh conversation, /exit to quit. Ctrl+C stops a reply.");

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

            // Cosine similarity between sentence embeddings (stage 4); separate from the chat too
            if (input.Equals("/similarity", StringComparison.OrdinalIgnoreCase) || input.StartsWith("/similarity ", StringComparison.OrdinalIgnoreCase))
            {
                await embeddings.SimilarityAsync(input["/similarity".Length..].Trim());
                continue;
            }

            // The SaaS API's MCP tools (week 6): sent to the model only while on
            if (input.Equals("/saas", StringComparison.OrdinalIgnoreCase) || input.StartsWith("/saas ", StringComparison.OrdinalIgnoreCase))
            {
                await SaasAsync(input["/saas".Length..].Trim());
                continue;
            }

            // The triage agent (week 6 Friday): its own session, pipeline and MCP connection, separate from the chat
            if (input.Equals("/agent", StringComparison.OrdinalIgnoreCase) || input.StartsWith("/agent ", StringComparison.OrdinalIgnoreCase))
            {
                await agents.RunAsync(input["/agent".Length..].Trim());
                continue;
            }

            await RunTurnAsync(input);
        }

        // The host is built, not run, so nothing disposes singletons at exit: close the MCP connection here
        await saasTools.DisposeAsync();
        await agents.DisposeAsync();
    }

    private async Task SaasAsync(string argument)
    {
        if (argument.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            if (saasTools.IsConnected)
            {
                Console.WriteLine("[saas already on]");
                return;
            }

            // Inside TurnCancellation, so Ctrl+C stops a hung connect instead of exiting the app
            CancellationToken cancellationToken = cancellation.Begin();
            try
            {
                IList<AITool> tools = await saasTools.ConnectAsync(cancellationToken);
                conversation.SetTools(tools, SaasInstructions);
                Console.WriteLine($"[saas on: {tools.Count} tools: {string.Join(", ", tools.Select(t => t.Name))}]");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[saas connect cancelled]");
            }
            catch (Exception ex)
            {
                // The token provider's messages are written to be shown as they are and never contain a secret
                Console.WriteLine($"[saas error: {ex.Message}]");
            }
            finally
            {
                cancellation.End();
            }
            return;
        }

        if (argument.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            if (!saasTools.IsConnected)
            {
                Console.WriteLine("[saas already off]");
                return;
            }

            // The history holds tool calls and results now: Anthropic rejects them when the request defines no tools,
            // and the tools in them belong to the client being closed. So the conversation starts over.
            conversation.SetTools(null, null);
            await saasTools.DisconnectAsync();
            conversation.Reset();
            Console.WriteLine("[saas off, conversation reset]");
            return;
        }

        Console.WriteLine($"Usage: /saas on|off (now {(saasTools.IsConnected ? "on" : "off")})");
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
                // Tool round trips (only while /saas is on): the call the model asked for, and a failure if the tool threw.
                // These updates have no text, so the reply itself prints as before.
                foreach (AIContent content in update.Contents)
                {
                    if (content is FunctionCallContent call)
                        Console.Write($"\n[tool: {call.Name} {JsonSerializer.Serialize(call.Arguments)}]\n");
                    else if (content is FunctionResultContent { Exception: not null } failed)
                        Console.Write($"[tool failed: {failed.CallId}]\n");
                }

                Console.Write(update.Text);
                updates.Add(update);
            }

            // OllamaSharp ends the stream quietly when the token is cancelled instead of throwing, so without this
            // a Ctrl+C'd reply looked complete and its half answer stayed in the history. Before the WriteLine below,
            // because the cancel message starts with its own new line.
            cancellationToken.ThrowIfCancellationRequested();
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
