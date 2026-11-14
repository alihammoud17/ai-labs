using System.Globalization;
using System.Text;
using System.Text.Json;
using AiLab.Application.Services;
using AiLab.Domain.Interfaces;
using AiLab.Infrastructure.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AiLab.Console.Workflow;

// The same job as the triage agent, as a workflow (week 6 Friday, Block 4): sort → collect → write.
// The code decides every step; the model only sorts the ticket (step 1) and writes the answer (step 3).
// Always 2 model calls, at most 2 lookups. No loop, no tools sent to the model.
// Model calls go through the console's DI IChatClient: no tools are sent, so its function layer just passes them on.
public sealed class TicketWorkflow(IChatClient client, TicketTools ticketTools, IChangeLogRepository changeLog,
    TimeProvider time, IServiceProvider services)
{
    // Haiku thinks by default and the thinking counts toward this limit
    private static readonly ChatOptions Options = new() { MaxOutputTokens = 2000 };

    // Step 1: one typed call that picks the path. The area is an enum, so the schema already limits it; the code checks
    // the component against the change log, so a made-up component never reaches it.
    public async Task<SortStep> SortAsync(string ticket, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> components = changeLog.GetComponents();
        List<ChatMessage> messages =
        [
            new(ChatRole.System,
                $"""
                You sort support tickets for a multi-tenant SaaS. Choose:
                - component: the change-log component the ticket is about, one of {string.Join(", ", components)}, or null if none fits.
                - area: the tenant data that can show the problem, one of {string.Join(", ", Enum.GetValues<TicketArea>().Select(TicketAreas.Name))}.
                  webhooks = webhook deliveries or endpoints, cors = browser CORS errors or allowed origins,
                  usage = API usage or traffic, unknown = none of these.
                Return only the JSON object, with no code fences and no other text.
                """),
            new(ChatRole.User, ticket),
        ];

        // Schema in the prompt, not the provider's native format: Haiku ignored the native one (JobExtractor)
        ChatResponse<TicketRoute> response = await client.GetResponseAsync<TicketRoute>(
            messages, Options, useJsonSchemaResponseFormat: false, cancellationToken: cancellationToken);

        // Unreadable: no repair call (the user's choice), so the workflow stays at 2 model calls and step 3 says "I don't know"
        if (!StructuredReply.TryParse(response, out TicketRoute? route, out _) || route is null)
            return new SortStep(new TicketRoute(null, TicketArea.Unknown), response, Readable: false, Corrected: false);

        string? component = components.FirstOrDefault(c => string.Equals(c, route.Component?.Trim(), StringComparison.OrdinalIgnoreCase));

        // Corrected = the model named a component the change log doesn't know (null is a valid answer).
        // A missing area means unknown; an area outside the enum already failed the parse above.
        bool corrected = route.Component is not null && component is null && !IsNullWord(route.Component);

        return new SortStep(new TicketRoute(component, route.Area ?? TicketArea.Unknown), response, Readable: true, corrected, route);
    }

    // Step 2: the code runs the lookups the route calls for. The model isn't involved, so it can't skip one,
    // repeat one, or write one as text. A failing lookup becomes a failed finding; the workflow goes on.
    public async Task<IReadOnlyList<Finding>> CollectAsync(TicketRoute route, CancellationToken cancellationToken)
    {
        List<Finding> findings = [];

        if (route.Component is string component)
        {
            string lookup = $"GetRecentChanges {JsonSerializer.Serialize(new { component })}";
            try
            {
                findings.Add(new Finding(lookup, ticketTools.GetRecentChanges(component), Failed: false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // TicketTools has logged the details; the answer only says it failed
                findings.Add(new Finding(lookup, "The change log could not be read.", Failed: true));
            }
        }

        if (route.Area is TicketArea area && TicketAreas.LookupFor(area) is var (tool, arguments))
            findings.Add(await CallSaasToolAsync(tool, arguments, cancellationToken));

        return findings;
    }

    // Its own MCP connection, opened and closed on every run (the user's choice): no state shared with /saas or /agent.
    // The login and MCP tokens stay cached in the shared token provider, so this is an initialize + tools/list per run.
    private async Task<Finding> CallSaasToolAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        string lookup = $"{toolName} {JsonSerializer.Serialize(arguments)}";
        await using SaasMcpToolSource saas = ActivatorUtilities.CreateInstance<SaasMcpToolSource>(services);
        try
        {
            IList<AITool> tools = await saas.ConnectAsync(cancellationToken);
            if (tools.OfType<McpClientTool>().FirstOrDefault(t => t.Name == toolName) is not McpClientTool tool)
                return new Finding(lookup, $"The SaaS API offers no tool named {toolName}.", Failed: true);

            CallToolResult result = await tool.CallAsync(arguments, cancellationToken: cancellationToken);
            string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
            return new Finding(lookup, text.Length > 0 ? text : "(empty result)", Failed: result.IsError == true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mostly the SaaS API being down or a bad login; the token provider's messages never contain a secret
            return new Finding(lookup, $"The SaaS API could not be reached: {ex.Message}", Failed: true);
        }
    }

    // Step 3: one call writes the answer from the ticket and the findings, in the same four parts as the agent.
    // Streamed, so it prints as it arrives like /agent.
    public IAsyncEnumerable<ChatResponseUpdate> WriteAsync(string ticket, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        StringBuilder lookups = new();
        if (findings.Count == 0)
            lookups.AppendLine("No lookups were made: the ticket matched no component and no data area.");
        foreach (Finding finding in findings)
            lookups.AppendLine($"- {finding.Lookup}{(finding.Failed ? " (failed)" : "")}:\n{finding.Result}");

        List<ChatMessage> messages =
        [
            new(ChatRole.System,
                $"""
                You answer support tickets for a multi-tenant SaaS. Today is {DateOnly.FromDateTime(time.GetLocalNow().DateTime).ToString("yyyy-MM-dd (dddd)", CultureInfo.InvariantCulture)}.
                The user message has the ticket and the results of the lookups that were made for it. The SaaS data
                covers only the logged-in tenant. Base the answer only on those results. Only name a change or a setting
                if it can actually explain the symptom. If the results don't show a cause, say "I don't know" and list
                what to check next. Never invent data.

                Answer in these four parts, with these headings:
                Summary: the problem in one or two sentences.
                What I found: each lookup and what it returned, one line each.
                Likely cause: the cause and the evidence for it, or "I don't know".
                Next step: what the support team or the customer should do now.
                """),
            new(ChatRole.User, $"Ticket:\n{ticket}\n\nLookups:\n{lookups}"),
        ];

        return client.GetStreamingResponseAsync(messages, Options, cancellationToken);
    }

    // Some models write the word instead of a JSON null
    private static bool IsNullWord(string value) =>
        value.Trim() is "" || value.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)
                           || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);
}

// Step 1's result: the checked route, the raw response (for its tokens), and whether the reply was readable or corrected.
// Original = what the model said before the check, for printing a correction.
public sealed record SortStep(TicketRoute Route, ChatResponse Response, bool Readable, bool Corrected, TicketRoute? Original = null);

// One lookup of step 2 and what it returned, as text for the writing prompt
public sealed record Finding(string Lookup, string Result, bool Failed);
