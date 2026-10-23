using AiLab.Api.ChangeLog;
using AiLab.Api.Grounding;
using AiLab.Api.Tools;
using AiLab.Core.Parsing;
using AiLab.Core.Pricing;
using Anthropic.Exceptions;
using Microsoft.Extensions.AI;
using OllamaSharp.Models.Exceptions;
using Polly;
using System.Text.Json;

namespace AiLab.Api.Tickets;

// One ticket in, one TicketAnalysis out. It only knows IChatClient; the tool loop happens in the middleware.
public class TicketAnalyzer(IChatClient client, TicketTools tools, IChangeLogRepository changeLog, ILogger<TicketAnalyzer> logger)
{
    // The component list comes from the repository, like the tool's parameter description
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

        if (run.Analysis is null)
        {
            logger.LogWarning("Reply could not be parsed as TicketAnalysis: {Reply}", run.RawText);
            throw new ModelCallException("The model's reply could not be read as a ticket analysis.");
        }

        return new AnalyzeTicketResponse(run.Analysis, run.Verification!, run.Diagnostics);
    }

    // The model call and its diagnostics, without failing on an unreadable reply: the ticket eval scores those too.
    // Provider and tool-loop failures still throw ModelCallException.
    public async Task<TicketRun> RunAsync(AnalyzeTicketRequest request, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, _systemPrompt),
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

        bool parsed = StructuredReply.TryParse(response, out TicketAnalysis? analysis, out bool strippedFence);

        // The server's check of the cause, against what the tool returned in this request (response.Messages holds the loop)
        CauseVerification? verification = parsed && analysis is not null
            ? CauseCheck.Evaluate(analysis, ToolEvidence.From(response.Messages))
            : null;
        if (verification is not null)
            logger.LogInformation("Cause {CauseStatus}: {Reason}", verification.CauseStatus, verification.Reason);

        return new TicketRun(response.Text, parsed ? analysis : null, verification,
            new AnalysisDiagnostics(_model, toolCalls, roundTrips, inputTokens, outputTokens, cost, strippedFence));
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

// One model answer: the reply as sent, the parsed analysis and its verification (both null when it couldn't be read)
// and the diagnostics
public record TicketRun(string RawText, TicketAnalysis? Analysis, CauseVerification? Verification, AnalysisDiagnostics Diagnostics);
