using System.Diagnostics;
using AiLab.Application.Services;
using AiLab.Console.Chat;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;

namespace AiLab.Console.Workflow;

// Inside AiLab.Console.*, a bare "Console" would mean the AiLab.Console namespace
using Console = System.Console;

// /workflow <ticket> (week 6 Friday, Block 4): runs TicketWorkflow's three steps and prints each one with its tokens,
// for comparison with /agent. Stateless: every /workflow is a new ticket, with no history and no follow-ups.
// The cost goes into the console's session total.
public sealed class WorkflowCommands(TicketWorkflow workflow, TurnCancellation cancellation, SessionCostTracker cost)
{
    public async Task RunAsync(string ticket)
    {
        if (ticket.Length == 0)
        {
            Console.WriteLine("Usage: /workflow <ticket text> (each run is a new ticket, no follow-ups)");
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        List<TurnUsage> usages = [];

        CancellationToken cancellationToken = cancellation.Begin();
        try
        {
            // Step 1: sort (model)
            SortStep sort = await workflow.SortAsync(ticket, cancellationToken);
            TurnUsage sortUsage = cost.Add(sort.Response);
            usages.Add(sortUsage);
            Console.WriteLine($"[step 1 sort: component={sort.Route.Component ?? "none"}, area={TicketAreas.Name(sort.Route.Area ?? TicketArea.Unknown)}{SortNote(sort)}]");
            UsageLine.Print(sortUsage);

            // Step 2: collect (code only)
            IReadOnlyList<Finding> findings = await workflow.CollectAsync(sort.Route, cancellationToken);
            if (findings.Count == 0)
                Console.WriteLine("[step 2 collect: no lookups (no component, area unknown)]");
            foreach (Finding finding in findings)
                Console.WriteLine($"[step 2 collect: {finding.Lookup}{(finding.Failed ? " failed" : $" → {LineCount(finding.Result)} lines")}]");

            // Step 3: write (model), printed as it arrives
            Console.Write("Workflow: ");
            List<ChatResponseUpdate> updates = [];
            await foreach (ChatResponseUpdate update in workflow.WriteAsync(ticket, findings, cancellationToken))
            {
                Console.Write(update.Text);
                updates.Add(update);
            }

            // OllamaSharp ends the stream quietly when the token is cancelled instead of throwing (Wednesday's fix)
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine();

            ChatResponse written = updates.ToChatResponse();
            TurnUsage writeUsage = cost.Add(written);
            usages.Add(writeUsage);
            Console.Write("[step 3 write] ");
            UsageLine.Print(writeUsage);
            if (written.FinishReason == ChatFinishReason.Length)
                Console.WriteLine("Warning: answer was cut off (output token limit reached)");
            if (string.IsNullOrWhiteSpace(written.Text))
                Console.WriteLine($"[empty answer (finish reason: {writeUsage.FinishReason})]");

            stopwatch.Stop();
            PrintTotals(usages, findings.Count, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            // Nothing to discard: the workflow keeps no history. Tokens of a cancelled call aren't counted.
            Console.WriteLine("\n[workflow cancelled]");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[workflow error: {ex.Message}]");
        }
        finally
        {
            cancellation.End();
        }
    }

    // Shows when the code overruled the model, so a bad sort is visible in the output
    private static string SortNote(SortStep sort) =>
        !sort.Readable ? " (reply unreadable, fell back to unknown)"
        : sort.Corrected ? $" (model said component={sort.Original?.Component}; not in the change log, dropped)"
        : "";

    private static int LineCount(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    // Same shape as /agent's totals line, so the two are easy to compare: always 2 model calls here
    private static void PrintTotals(List<TurnUsage> usages, int lookups, TimeSpan elapsed)
    {
        long input = usages.Sum(u => u.InputTokens ?? 0);
        long output = usages.Sum(u => u.OutputTokens ?? 0);
        string price = usages.Any(u => u.Cost is null) ? "unknown price" : $"${usages.Sum(u => u.Cost ?? 0):0.000000}";
        Console.WriteLine($"[workflow: {usages.Count} model calls, {lookups} lookups, in={input}, out={output}, {price}, {elapsed.TotalSeconds:0.0} s]");
    }
}
