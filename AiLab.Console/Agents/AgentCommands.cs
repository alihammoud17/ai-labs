using System.Diagnostics;
using System.Text.Json;
using AiLab.Application.Services;
using AiLab.Console.Chat;
using AiLab.Domain.Outputs;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AiLab.Console.Agents;

// Inside AiLab.Console.*, a bare "Console" would mean the AiLab.Console namespace
using Console = System.Console;

// /agent (week 6 Friday): the triage agent in the console. Its conversation is an AgentSession, separate from the chat's
// ChatConversation, so the chat history, /saas and /reset are untouched; the cost goes into the same session total.
public sealed class AgentCommands(TriageAgentFactory factory, TurnCancellation cancellation, SessionCostTracker cost) : IAsyncDisposable
{
    private const string Usage = "Usage: /agent <ticket text> (a follow-up is another /agent <text>), /agent reset";

    // Created on the first /agent <text>, dropped on /agent reset. The agent holds the date and the MCP tools,
    // the session the conversation.
    private ChatClientAgent? _agent;
    private AgentSession? _session;

    public async Task RunAsync(string argument)
    {
        if (argument.Length == 0)
        {
            Console.WriteLine($"{Usage} (now {(_session is null ? "no session" : "a session is open")})");
            return;
        }

        // Only the exact word, so a ticket that starts with "reset" is still a ticket
        if (argument.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            await ResetAsync();
            Console.WriteLine("[agent reset: session ended, MCP connection closed]");
            return;
        }

        if (_session is null && !await StartAsync())
            return;

        await RunTurnAsync(argument);
    }

    // Connects the agent's own MCP client and creates the agent and its session. Inside TurnCancellation,
    // so Ctrl+C stops a hung connect instead of exiting the app.
    private async Task<bool> StartAsync()
    {
        CancellationToken cancellationToken = cancellation.Begin();
        try
        {
            _agent = await factory.CreateAsync(cancellationToken);
            _session = await _agent.CreateSessionAsync(cancellationToken);
            Console.WriteLine($"[agent session started: {string.Join(", ", _agent.GetService<ChatOptions>()?.Tools?.Select(t => t.Name) ?? [])}]");
            return true;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[agent start cancelled]");
        }
        catch (Exception ex)
        {
            // Mostly the SaaS API being down or a bad login; the token provider's messages never contain a secret
            Console.WriteLine($"[agent error: {ex.Message}]");
        }
        finally
        {
            cancellation.End();
        }

        await ResetAsync();
        return false;
    }

    private async Task RunTurnAsync(string input)
    {
        Console.Write("Agent: ");

        List<ChatResponseUpdate> updates = [];
        Dictionary<string, string> callNames = [];
        int functionCalls = 0;
        int toolRounds = 0;
        bool inToolResults = false;
        // A step of the loop is one model call that asks for tools (what Agent:MaxToolIterations counts); it can ask for
        // several functions at once, so each call prints as step.call, e.g. 1.1, 1.2, then 2.1 after the next model call
        int callInStep = 0;
        int lastCallStep = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();

        CancellationToken cancellationToken = cancellation.Begin();
        try
        {
            await foreach (AgentResponseUpdate update in _agent!.RunStreamingAsync(input, _session, cancellationToken: cancellationToken))
            {
                // Each step of the loop: the function the model asked for, and a failure if it threw.
                // These updates carry no text, so the answer itself prints as it streams.
                bool hasResults = false;
                foreach (AIContent content in update.Contents)
                {
                    if (content is FunctionCallContent call)
                    {
                        // Calls come before their results, so the step is the number of result batches so far + 1
                        int step = toolRounds + 1;
                        if (step != lastCallStep)
                        {
                            lastCallStep = step;
                            callInStep = 0;
                        }
                        callInStep++;
                        functionCalls++;

                        string label = $"{step}.{callInStep}: {call.Name}";
                        callNames[call.CallId] = label;
                        Console.Write($"\n[step {label} {JsonSerializer.Serialize(call.Arguments)}]\n");
                    }
                    else if (content is FunctionResultContent result)
                    {
                        hasResults = true;
                        if (result.Exception is not null)
                            Console.Write($"[step {callNames.GetValueOrDefault(result.CallId, result.CallId)} failed]\n");
                    }
                }

                // The function layer sends one batch of results per round trip, so a new batch = one more model call
                // that asked for tools. The last model call (the answer) has no results after it.
                if (hasResults && !inToolResults)
                    toolRounds++;
                inToolResults = hasResults;

                Console.Write(update.Text);
                updates.Add(ToChatResponseUpdate(update));
            }

            // OllamaSharp ends the stream quietly when the token is cancelled instead of throwing (Wednesday's fix
            // in ChatSession). The session may already hold the half answer, so it's dropped below.
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[agent run cancelled, session reset]");
            await NewSessionAsync();
            return;
        }
        catch (Exception ex)
        {
            // The session may hold a tool call without its result now, which Anthropic would reject on the next run
            Console.WriteLine($"\n[agent error: {ex.Message}; session reset]");
            await NewSessionAsync();
            return;
        }
        finally
        {
            cancellation.End();
        }

        stopwatch.Stop();
        PrintTotals(updates, toolRounds, functionCalls, stopwatch.Elapsed);
    }

    // Model calls (the steps with tools + the answer), function calls, time, then the same tokens / cost line as a chat turn.
    // Tokens: ToChatResponse adds up every UsageContent in the stream (one or more per model call), as the chat does.
    private void PrintTotals(List<ChatResponseUpdate> updates, int toolRounds, int functionCalls, TimeSpan elapsed)
    {
        ChatResponse response = updates.ToChatResponse();
        TurnUsage usage = cost.Add(response);

        Console.WriteLine($"[agent: {toolRounds + 1} model calls ({toolRounds} {(toolRounds == 1 ? "step" : "steps")} with tools), {functionCalls} function calls, {elapsed.TotalSeconds:0.0} s]");
        UsageLine.Print(usage);

        if (response.FinishReason == ChatFinishReason.Length)
            Console.WriteLine("Warning: answer was cut off (output token limit reached)");
        if (string.IsNullOrWhiteSpace(response.Text))
            Console.WriteLine($"[empty answer (finish reason: {usage.FinishReason})]");
    }

    // The fields ToChatResponse needs: contents (text, function calls and results, usage), role, ids, finish reason
    private static ChatResponseUpdate ToChatResponseUpdate(AgentResponseUpdate update) => new(update.Role, update.Contents)
    {
        ResponseId = update.ResponseId,
        MessageId = update.MessageId,
        CreatedAt = update.CreatedAt,
        FinishReason = update.FinishReason,
    };

    // Same agent and MCP connection, empty conversation
    private async Task NewSessionAsync()
    {
        _session = _agent is null ? null : await _agent.CreateSessionAsync();
    }

    // Drops the agent and the session before closing the connection, since their MCP tools stop working with it
    private async Task ResetAsync()
    {
        _session = null;
        _agent = null;
        await factory.DisconnectAsync();
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();
}
