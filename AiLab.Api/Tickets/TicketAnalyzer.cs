using AiLab.Api.ChangeLog;
using AiLab.Api.Grounding;
using AiLab.Api.Tools;
using AiLab.Core.Parsing;
using AiLab.Core.Pricing;
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
    private readonly ChatOptions _options = new()
    {
        MaxOutputTokens = 2000,
        Tools = [tools.AsAIFunction()],
    };

    private readonly string? _model = client.GetService<ChatClientMetadata>()?.DefaultModelId;

    public async Task<AnalyzeTicketResponse> AnalyzeAsync(AnalyzeTicketRequest request, CancellationToken cancellationToken)
    {
        TicketRun run = await RunAsync(request, cancellationToken);

        return run.Outcome switch
        {
            RunOutcome.Analyzed => new AnalyzeTicketResponse(AnalyzeTicketResponse.NoticeText, run.Analysis!, run.Verification!, run.Diagnostics),
            RunOutcome.CutOff => throw new ModelCallException("The model's reply was cut off before it finished."),
            _ => throw new ModelCallException("The model's reply could not be read as a ticket analysis."),
        };
    }

    // The model call and its diagnostics, without failing on an unusable reply: the ticket eval scores those too.
    // Provider and tool-loop failures still throw ModelCallException.
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

        ChatResponse<TicketAnalysis> response;
        try
        {
            // Schema in the prompt instead of the native response format: Haiku ignored the native one (experiments.md).
            // The function-invocation middleware runs the tool calls inside this one call.
            response = await client.GetResponseAsync<TicketAnalysis>(messages, _options, useJsonSchemaResponseFormat: false, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Model provider call failed");
            throw new ModelCallException("The model provider is unavailable.", ex);
        }
        catch (ArgumentException ex)
        {
            // Tool calls that kept failing: the middleware sends each failure back to the model and rethrows after
            // MaximumConsecutiveErrorsPerRequest (default 3) failures in a row. Seen with Llama calling GetRecentChanges
            // with no component, which fails argument binding with an ArgumentException. That's the model failing, so a 502.
            // Only ArgumentException: a bug of ours elsewhere should stay a 500, not look like a model problem.
            logger.LogWarning(ex, "Tool loop failed");
            throw new ModelCallException("The model could not use the change-log tool correctly.", ex);
        }

        AnalysisDiagnostics diagnostics = Diagnostics(response, strippedFence: false);

        // Cut off by MaxOutputTokens: the JSON is incomplete, so it's not a parse error. No repair: the same limit would cut it again.
        if (response.FinishReason == ChatFinishReason.Length)
        {
            logger.LogWarning("Reply cut off at {OutputTokens} output tokens (MaxOutputTokens {MaxOutputTokens}): {Reply}",
                diagnostics.OutputTokens, _options.MaxOutputTokens, response.Text);
            return new TicketRun(RunOutcome.CutOff, response.Text, null, null, diagnostics);
        }

        if (!StructuredReply.TryParse(response, out TicketAnalysis? parsed, out bool strippedFence) || parsed is null)
        {
            logger.LogWarning("Reply could not be parsed as TicketAnalysis: {Reply}", response.Text);
            return new TicketRun(RunOutcome.Unreadable, response.Text, null, null, diagnostics);
        }
        diagnostics = diagnostics with { StrippedFence = strippedFence };

        // Valid JSON can still be empty: that counts as an unreadable reply too
        IReadOnlyList<string> problems = TicketAnalysisRules.Check(parsed, out TicketAnalysis analysis);
        if (problems.Count > 0)
        {
            logger.LogWarning("Reply parsed but isn't usable ({Problems}): {Reply}", string.Join(", ", problems), response.Text);
            return new TicketRun(RunOutcome.Unreadable, response.Text, null, null, diagnostics);
        }

        // The server's check of the cause, against what the tool returned in this request (response.Messages holds the loop)
        CauseVerification verification = CauseCheck.Evaluate(analysis, ToolEvidence.From(response.Messages));
        logger.LogInformation("Cause {CauseStatus}: {Reason}", verification.CauseStatus, verification.Reason);

        return new TicketRun(RunOutcome.Analyzed, response.Text, analysis, verification, diagnostics);
    }

    private string SystemPrompt() =>
        $"{_systemPrompt}\nToday is {time.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.";

    private AnalysisDiagnostics Diagnostics(ChatResponse response, bool strippedFence)
    {
        // Every tool call the model made, across all round trips (response.Messages holds the whole loop)
        List<ToolCall> toolCalls = response.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(c => new ToolCall(c.Name, c.Arguments))
            .ToList();

        // One assistant message per model call: each tool-call step plus the final answer
        int roundTrips = response.Messages.Count(m => m.Role == ChatRole.Assistant);

        // The middleware adds up usage over the loop
        long? inputTokens = response.Usage?.InputTokenCount;
        long? outputTokens = response.Usage?.OutputTokenCount;
        decimal? cost = _model is null ? null : ModelPrices.Cost(_model, inputTokens ?? 0, outputTokens ?? 0);

        // Arguments are JsonElements, which Serilog would destructure field by field; log them as JSON text instead
        string toolCallText = string.Join(", ", toolCalls.Select(c => $"{c.Name}({JsonSerializer.Serialize(c.Arguments)})"));
        logger.LogInformation("Ticket analyzed: {RoundTrips} round trips, tool calls [{ToolCalls}], tokens in={InputTokens} out={OutputTokens}, cost {Cost}",
            roundTrips, toolCallText, inputTokens, outputTokens, cost);

        return new AnalysisDiagnostics(_model, toolCalls, roundTrips, inputTokens, outputTokens, cost, strippedFence);
    }

    // Errors from the provider after its retries. ExecutionRejectedException is Polly giving up on Ollama
    // (total timeout reached, circuit open). A cancellation we didn't ask for is an SDK timeout;
    // one we did ask for (client gone, request timeout) is left alone so ASP.NET Core can handle it.
    private static bool IsProviderFailure(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        HttpRequestException or OllamaException or AnthropicException or ExecutionRejectedException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };
}

// How a model answer ended. Only Analyzed has an analysis and a verification.
public enum RunOutcome { Analyzed, CutOff, Unreadable }

// One model answer: how it ended, the reply as sent, the cleaned analysis and its verification (both null unless Analyzed)
// and the diagnostics
public record TicketRun(RunOutcome Outcome, string RawText, TicketAnalysis? Analysis, CauseVerification? Verification, AnalysisDiagnostics Diagnostics);
