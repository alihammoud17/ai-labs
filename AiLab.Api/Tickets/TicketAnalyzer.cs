using AiLab.Api.Grounding;
using AiLab.Api.Tools;
using AiLab.Core.Parsing;
using AiLab.Core.Pricing;
using AiLab.Domain.Constants;
using AiLab.Domain.DTOs;
using AiLab.Domain.Enums;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Domain.Responses;
using Anthropic.Exceptions;
using Microsoft.Extensions.AI;
using OllamaSharp.Models.Exceptions;
using Polly;
using System.Globalization;
using System.Text.Json;

namespace AiLab.Api.Tickets;

// One ticket in, one TicketAnalysis out. It only knows IChatClient; the tool loop happens in the middleware.
public class TicketAnalyzer(IChatClient client, TicketTools tools, IChangeLogRepository changeLog, TimeProvider time, ILogger<TicketAnalyzer> logger)
{
    // The component list comes from the repository, like the tool's parameter description.
    // Today's date is added per request (SystemPrompt), so "recent" means something to the model.
    private readonly string _systemPrompt =
        $"""
        You analyze support tickets for a multi-tenant SaaS with these components: {string.Join(", ", changeLog.GetComponents())}.
        Before you decide on a probable cause, call GetRecentChanges for the component the ticket is about.
        If the ticket gives no component, pick the most likely one from its text.
        Base the probable cause only on what GetRecentChanges returned.
        The change log may not contain the cause. Only name a change if it can actually explain the symptom;
        otherwise say the cause is unknown and list what to check.
        Return only the JSON, with no code fences.
        """;

    // Haiku thinks by default and the thinking counts toward this limit (experiments.md, section 3), so leave room
    private const int MaxOutputTokens = 2000;

    private readonly ChatOptions _options = new()
    {
        MaxOutputTokens = MaxOutputTokens,
        Tools = [tools.AsAIFunction()],
    };

    // The repair call: the tool results are already in the history, so no new tool loop. The tool stays defined
    // because Anthropic rejects a history with tool calls when no tools are defined; ToolMode None tells the model not to call it.
    private readonly ChatOptions _repairOptions = new()
    {
        MaxOutputTokens = MaxOutputTokens,
        Tools = [tools.AsAIFunction()],
        ToolMode = ChatToolMode.None,
    };

    private readonly string? _model = client.GetService<ChatClientMetadata>()?.DefaultModelId;

    public async Task<AnalyzeTicketResponse> AnalyzeAsync(AnalyzeTicketRequest request, CancellationToken cancellationToken)
    {
        TicketRun run = await RunAsync(request, cancellationToken);

        return run.Outcome switch
        {
            RunOutcome.Analyzed => new AnalyzeTicketResponse(AnalyzeTicketResponse.NoticeText, run.Analysis!, run.Verification!, run.Diagnostics),
            RunOutcome.CutOff => throw new ModelCallException("The model's reply was cut off before it finished.", ErrorCodes.ReplyCutOff),
            RunOutcome.NotATicket => throw new TicketRejectedException("This doesn't look like a support ticket.", ErrorCodes.NotATicket),
            RunOutcome.Declined => throw new TicketRejectedException("The model declined to analyze this request.", ErrorCodes.ModelDeclined),
            _ => throw new ModelCallException("The model's reply could not be read as a ticket analysis.", ErrorCodes.ReplyUnreadable),
        };
    }

    // The model call and its diagnostics, without failing on an unusable reply: the ticket eval scores those too.
    // Provider and tool failures still throw ModelCallException.
    public async Task<TicketRun> RunAsync(AnalyzeTicketRequest request, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, SystemPrompt()),
            new(ChatRole.User,
                $"""
                Title: {request.Title!.Trim()}
                Component: {(string.IsNullOrWhiteSpace(request.Component) ? "not given" : request.Component.Trim())}

                {request.Description!.Trim()}
                """),
        ];

        ChatResponse<TicketAnalysis> first = await CallAsync(messages, _options, cancellationToken);
        Reading reading = Read(first);
        List<ChatResponse> responses = [first];

        // One repair, never more: the conversation goes back with a short note on what was wrong
        if (reading.Outcome == RunOutcome.Unreadable)
        {
            logger.LogWarning("Reply unusable ({Problem}), asking once for a repair: {Reply}", reading.Problem, first.Text);

            List<ChatMessage> repair =
            [
                .. messages,
                .. first.Messages,
                new(ChatRole.User, $"Your last reply wasn't valid JSON for the schema ({reading.Problem}). Reply with only the JSON."),
            ];
            ChatResponse<TicketAnalysis> second = await CallAsync(repair, _repairOptions, cancellationToken);
            responses.Add(second);
            reading = Read(second);

            if (reading.Outcome == RunOutcome.Analyzed)
                logger.LogInformation("Repair worked: the second reply is usable");
            else
                logger.LogWarning("Repair failed ({Outcome}, {Problem}): {Reply}", reading.Outcome, reading.Problem, second.Text);
        }

        List<ChatMessage> loop = responses.SelectMany(r => r.Messages).ToList();
        AnalysisDiagnostics diagnostics = Diagnostics(responses, reading.StrippedFence, repaired: responses.Count > 1);
        ToolEvidence evidence = ToolEvidence.From(loop);

        if (reading.Outcome == RunOutcome.NotATicket)
            logger.LogWarning("Not a support ticket, the model says: {NotATicketReason}", reading.NotATicketReason);
        if (reading.Outcome != RunOutcome.Analyzed)
            return new TicketRun(reading.Outcome, responses[^1].Text, null, null, diagnostics);

        // The server's check of the cause, against what the tool returned in this request
        CauseVerification verification = CauseCheck.Evaluate(reading.Analysis!, evidence);
        logger.LogInformation("Cause {CauseStatus}: {Reason}", verification.CauseStatus, verification.Reason);

        return new TicketRun(RunOutcome.Analyzed, responses[^1].Text, reading.Analysis, verification, diagnostics);
    }

    private async Task<ChatResponse<TicketAnalysis>> CallAsync(List<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken)
    {
        try
        {
            // Schema in the prompt instead of the native response format: Haiku ignored the native one (experiments.md).
            // The function-invocation middleware runs the tool calls inside this one call.
            return await client.GetResponseAsync<TicketAnalysis>(messages, options, useJsonSchemaResponseFormat: false, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Model provider call failed");
            throw new ModelCallException("The model provider is unavailable.", ErrorCodes.ProviderUnavailable, ex);
        }
        catch (ChangeLogUnavailableException ex)
        {
            // The tool kept failing: the middleware sends each failure to the model and rethrows once more than
            // MaximumConsecutiveErrorsPerRequest (3) fail in a row. With MaxToolIterations 3 the cap usually ends the loop first,
            // and the model answers with toolFailed set (a test shows it); this catch is for the rest, e.g. several calls per step.
            logger.LogWarning(ex, "Change-log tool kept failing");
            throw new ModelCallException("The change log could not be read.", ErrorCodes.ToolFailed, ex);
        }
        catch (ArgumentException ex)
        {
            // Tool calls with bad arguments, again and again. Seen with Llama calling GetRecentChanges with no component,
            // which fails argument binding with an ArgumentException. That's the model failing, so a 502.
            // Only these two exceptions: a bug of ours elsewhere should stay a 500, not look like a model problem.
            logger.LogWarning(ex, "Tool loop failed");
            throw new ModelCallException("The model could not use the change-log tool correctly.", ErrorCodes.ToolFailed, ex);
        }
    }

    // What one reply is worth, in this order: cut off, declined, unparsable, not a ticket, empty fields, usable
    private Reading Read(ChatResponse<TicketAnalysis> response)
    {
        // Cut off by MaxOutputTokens: the JSON is incomplete, so it's not a parse error. No repair: the same limit would cut it again.
        if (response.FinishReason == ChatFinishReason.Length)
        {
            logger.LogWarning("Reply cut off at {OutputTokens} output tokens (MaxOutputTokens {MaxOutputTokens}): {Reply}",
                response.Usage?.OutputTokenCount, MaxOutputTokens, response.Text);
            return new Reading(RunOutcome.CutOff);
        }

        // The provider's own safety stop (Anthropic's refusals map here)
        if (response.FinishReason == ChatFinishReason.ContentFilter)
        {
            logger.LogWarning("Reply stopped by the provider's content filter: {Reply}", response.Text);
            return new Reading(RunOutcome.Declined);
        }

        if (!StructuredReply.TryParse(response, out TicketAnalysis? parsed, out bool strippedFence) || parsed is null)
            return new Reading(RunOutcome.Unreadable, Problem: "it didn't parse");

        if (NotATicketReason(parsed.NotATicketReason) is { } reason)
            return new Reading(RunOutcome.NotATicket, NotATicketReason: reason, StrippedFence: strippedFence);
        parsed = parsed with { NotATicketReason = null }; // "null" or "N/A" as text: drop it, so it never shows in a 200

        // Valid JSON can still be empty: that counts as an unreadable reply too
        IReadOnlyList<string> problems = TicketAnalysisRules.Check(parsed, out TicketAnalysis analysis);
        return problems.Count > 0
            ? new Reading(RunOutcome.Unreadable, Problem: string.Join(", ", problems), StrippedFence: strippedFence)
            : new Reading(RunOutcome.Analyzed, analysis, StrippedFence: strippedFence);
    }

    // Small models write "null" or "N/A" as text instead of a JSON null; those still mean "it's a ticket"
    private static string? NotATicketReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().ToLowerInvariant() is "null" or "none" or "n/a" or "no"
            ? null
            : reason.Trim();

    private string SystemPrompt() =>
        $"{_systemPrompt}\nToday is {time.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.";

    private AnalysisDiagnostics Diagnostics(List<ChatResponse> responses, bool strippedFence, bool repaired)
    {
        List<ChatMessage> loop = responses.SelectMany(r => r.Messages).ToList();

        // Every tool call the model made, across all round trips (response.Messages holds the whole loop)
        List<ToolCall> toolCalls = loop
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(c => new ToolCall(c.Name, c.Arguments))
            .ToList();

        // One assistant message per model call: each tool-call step, the answer and the repair, if any
        int roundTrips = loop.Count(m => m.Role == ChatRole.Assistant);

        // The middleware adds up usage over the loop; a repair adds its own call
        long? inputTokens = Sum(responses.Select(r => r.Usage?.InputTokenCount));
        long? outputTokens = Sum(responses.Select(r => r.Usage?.OutputTokenCount));
        decimal? cost = _model is null ? null : ModelPrices.Cost(_model, inputTokens ?? 0, outputTokens ?? 0);

        // Arguments are JsonElements, which Serilog would destructure field by field; log them as JSON text instead
        string toolCallText = string.Join(", ", toolCalls.Select(c => $"{c.Name}({JsonSerializer.Serialize(c.Arguments)})"));
        logger.LogInformation("Ticket analyzed: {RoundTrips} round trips, tool calls [{ToolCalls}], tokens in={InputTokens} out={OutputTokens}, cost {Cost}, repaired {Repaired}",
            roundTrips, toolCallText, inputTokens, outputTokens, cost, repaired);

        return new AnalysisDiagnostics(_model, toolCalls, roundTrips, inputTokens, outputTokens, cost, strippedFence, repaired);
    }

    // Null only when no response reported usage
    private static long? Sum(IEnumerable<long?> values) =>
        values.Any(v => v is not null) ? values.Sum(v => v ?? 0) : null;

    // Errors from the provider after its retries. ExecutionRejectedException is Polly giving up on Ollama
    // (total timeout reached, circuit open). A cancellation we didn't ask for is an SDK timeout;
    // one we did ask for (client gone, request timeout) is left alone so ASP.NET Core can handle it.
    private static bool IsProviderFailure(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        HttpRequestException or OllamaException or AnthropicException or ExecutionRejectedException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

    private record Reading(RunOutcome Outcome, TicketAnalysis? Analysis = null, string? Problem = null,
        string? NotATicketReason = null, bool StrippedFence = false);
}
