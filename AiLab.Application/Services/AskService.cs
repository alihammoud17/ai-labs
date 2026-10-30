using System.Diagnostics;
using AiLab.Application.Ask;
using AiLab.Domain.Constants;
using AiLab.Domain.DTOs;
using AiLab.Domain.Enums;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Domain.Responses;
using AiLab.Domain.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiLab.Application.Services;

public sealed class AskService(IVectorSearchService vectorSearchService, IChatClient client, IJobFileStore files,
    IProviderErrorClassifier errors, AskSettings settings, ILogger<AskService> logger) : IAskService
{

    private readonly IVectorSearchService _vectorSearchService = vectorSearchService;

    // No tools and no temperature: Haiku accepts only its default (CLAUDE.md). Haiku's thinking counts toward the limit too.
    private readonly ChatOptions _options = new() { MaxOutputTokens = settings.MaxOutputTokens };

    private readonly string? _model = client.GetService<ChatClientMetadata>()?.DefaultModelId;

    public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken ct = default)
    {
        var (question, top) = Validate(request);

        // Errors from the search (missing collection, Ollama or Postgres down) go up to RagExceptionHandler as they are
        var stopwatch = Stopwatch.StartNew();
        var searchResult = await _vectorSearchService.SearchAsync(question, top, settings.Chunker, ct);
        var retrievalMs = stopwatch.ElapsedMilliseconds;

        // Closest first, so the first hit is the best one. Compared unrounded; lower distance = closer.
        double? bestDistance = searchResult.Count > 0 ? searchResult[0].Score : null;
        var gate = bestDistance switch
        {
            null => GateResult.NoHits,
            double d when d > settings.MaxDistance => GateResult.TooFar,
            _ => GateResult.Passed,
        };

        // Every request, passed or not: the data for calibrating Ask:MaxDistance (4.8). The second-best distance shows
        // how clearly the best hit stands out.
        double? secondDistance = searchResult.Count > 1 ? searchResult[1].Score : null;
        logger.LogInformation(
            "Ask gate {Gate}: best distance {BestDistance}, second {SecondDistance}, max {MaxDistance}, {Hits} hits in {RetrievalMs} ms",
            gate, bestDistance, secondDistance, settings.MaxDistance, searchResult.Count, retrievalMs);

        var retrieval = new AskRetrieval(settings.Chunker, top, bestDistance, gate);
        if (gate != GateResult.Passed)
            return Abstained(gate, bestDistance, retrieval, retrievalMs);

        return await GenerateAsync(question, searchResult, retrieval, retrievalMs, ct);
    }

    // The gate closed: no model call, so no answer, citations, usage or generation time
    private AskResponse Abstained(GateResult gate, double? bestDistance, AskRetrieval retrieval, long retrievalMs)
    {
        var reason = gate == GateResult.NoHits
            ? "Nothing was found in the docs."
            : $"The closest section of the docs (distance {bestDistance:0.####}) is farther than {settings.MaxDistance}; the docs probably don't cover this.";

        return new AskResponse(
            Answer: "",
            Grounding: new AskGrounding(GroundingStatus.Abstained, reason),
            Citations: [],
            Retrieval: retrieval,
            Usage: new AskUsage(null, null, null),
            Timings: new AskTimings(retrievalMs, null));
    }

    // The gate passed: one model call (plus one repair if the reply is unreadable), then the citation check
    private async Task<AskResponse> GenerateAsync(string question, IReadOnlyList<SearchHit> hits, AskRetrieval retrieval,
        long retrievalMs, CancellationToken ct)
    {
        var prompt = AskPrompt.Build(await LoadInstructionsAsync(ct), question, hits);

        var stopwatch = Stopwatch.StartNew();
        ChatResponse<AskAnswer> first = await CallAsync([.. prompt.Messages], ct);
        List<ChatResponse> responses = [first];
        var reading = Read(first);

        // One repair, never more: the conversation goes back with a short note on what was wrong
        if (reading.Answer is null)
        {
            logger.LogWarning("Ask reply unusable ({Problem}), asking once for a repair: {Reply}", reading.Problem, first.Text);
            List<ChatMessage> repair =
            [
                .. prompt.Messages,
                .. first.Messages,
                new(ChatRole.User, $"Your last reply wasn't valid JSON for the schema ({reading.Problem}). Reply with only the JSON."),
            ];
            ChatResponse<AskAnswer> second = await CallAsync(repair, ct);
            responses.Add(second);
            reading = Read(second);

            if (reading.Answer is null)
            {
                logger.LogWarning("Ask repair failed ({Problem}): {Reply}", reading.Problem, second.Text);
                throw new ModelCallException("The model's reply could not be read as an answer.", ErrorCodes.ReplyUnreadable);
            }
            logger.LogInformation("Ask repair worked: the second reply is usable");
        }
        var generationMs = stopwatch.ElapsedMilliseconds;

        // A repair adds its own call, so usage is summed over both
        var usage = new AskUsage(
            responses[^1].ModelId ?? _model,
            Sum(responses.Select(r => r.Usage?.InputTokenCount)),
            Sum(responses.Select(r => r.Usage?.OutputTokenCount)));
        logger.LogInformation(
            "Ask answered: notInDocs {NotInDocs}, cited [{CitedSources}], tokens in={InputTokens} out={OutputTokens}, {GenerationMs} ms, repaired {Repaired}",
            reading.Answer.NotInDocs, string.Join(", ", reading.Answer.CitedSources), usage.InputTokens, usage.OutputTokens,
            generationMs, responses.Count > 1);

        // The server's check of the citations, against the sources sent in this request
        var check = CitationCheck.Evaluate(reading.Answer, prompt.Sources);
        logger.LogInformation("Ask grounding {GroundingStatus}: {Reason}", check.Grounding.Status, check.Grounding.Reason);

        return new AskResponse(reading.Answer.Answer, check.Grounding, check.Citations, retrieval, usage,
            new AskTimings(retrievalMs, generationMs));
    }

    // Read on every request, so an edit to the prompt applies without a restart (useful while calibrating in 4.8).
    // The host makes Ask:PromptPath absolute (RepoPaths); a missing file is a server problem, so a 500.
    private async Task<string> LoadInstructionsAsync(CancellationToken ct)
    {
        if (!files.Exists(settings.PromptPath))
            throw new InvalidOperationException($"Ask prompt not found: {files.FullPath(settings.PromptPath)}. Check Ask:PromptPath.");
        return await files.ReadAllTextAsync(settings.PromptPath, ct);
    }

    private async Task<ChatResponse<AskAnswer>> CallAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        try
        {
            // Schema in the prompt instead of the native response format: Haiku ignored the native one (experiments.md)
            return await client.GetResponseAsync<AskAnswer>(messages, _options, useJsonSchemaResponseFormat: false, cancellationToken: ct);
        }
        catch (Exception ex) when (IsProviderFailure(ex, ct))
        {
            logger.LogWarning(ex, "Model provider call failed");
            throw new ModelCallException("The model provider is unavailable.", ErrorCodes.ProviderUnavailable, ex);
        }
    }

    // What one reply is worth: a usable answer, or the problem to send back in the repair. Cut-off and declined replies throw.
    private Reading Read(ChatResponse<AskAnswer> response)
    {
        // The JSON is incomplete, so it's not a parse error. No repair: the same limit would cut it again.
        if (response.FinishReason == ChatFinishReason.Length)
        {
            logger.LogWarning("Ask reply cut off at {OutputTokens} output tokens (MaxOutputTokens {MaxOutputTokens}): {Reply}",
                response.Usage?.OutputTokenCount, settings.MaxOutputTokens, response.Text);
            throw new ModelCallException("The model's reply was cut off before it finished.", ErrorCodes.ReplyCutOff);
        }

        // The provider's own safety stop. Not expected on questions about our docs; no separate code in the /ask contract.
        if (response.FinishReason == ChatFinishReason.ContentFilter)
        {
            logger.LogWarning("Ask reply stopped by the provider's content filter: {Reply}", response.Text);
            throw new ModelCallException("The model declined to answer.", ErrorCodes.ReplyUnreadable);
        }

        if (!StructuredReply.TryParse(response, out AskAnswer? parsed, out _) || parsed is null)
            return new Reading(null, "it didn't parse");

        // A missing list is the sign of an answer cut short, not of "no citations". Ollama's JSON mode only lets the model
        // write valid JSON, so a " copied unescaped from a source (status: "pending_verification") ends the answer string,
        // and the model can only close the object there: valid JSON, half an answer, no citedSources (4.8, 2026-10-30).
        if (parsed.CitedSources is null)
            return new Reading(null, "citedSources is missing; a double quote inside answer ends it early, so write values in backticks instead");

        var answer = new AskAnswer(parsed.Answer?.Trim() ?? "", parsed.CitedSources, parsed.NotInDocs);

        // Valid JSON can still be empty: an answer has to say something, even "the docs don't cover it"
        return answer.Answer.Length == 0
            ? new Reading(null, "answer is empty")
            : new Reading(answer, null);
    }

    // Null only when no response reported usage
    private static long? Sum(IEnumerable<long?> values) =>
        values.Any(v => v is not null) ? values.Sum(v => v ?? 0) : null;

    // Errors from the provider after its retries. A cancellation we didn't ask for is an SDK timeout; one we did ask for
    // (client gone) is left alone so ASP.NET Core can handle it. Same rule as TicketAnalyzer.
    private bool IsProviderFailure(Exception ex, CancellationToken ct) =>
        errors.IsProviderFailure(ex) || ex is OperationCanceledException && !ct.IsCancellationRequested;

    private sealed record Reading(AskAnswer? Answer, string? Problem);

    // The question trimmed and the top to use. Limits come from AskSettings (Ask:*), so the messages name them.
    // Top null means the default; 0 or a negative number is an error, not the default.
    private (string Question, int Top) Validate(AskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            throw new InvalidRequestException("Question is required.");

        // Counted after trimming, so surrounding whitespace doesn't use up the limit
        var question = request.Question.Trim();
        if (question.Length > settings.MaxQuestionChars)
            throw new InvalidRequestException($"Question must be at most {settings.MaxQuestionChars} characters.");

        var top = request.Top ?? settings.Top;
        if (top < 1 || top > settings.MaxTop)
            throw new InvalidRequestException($"top must be between 1 and {settings.MaxTop}.");

        return (question, top);
    }
}
