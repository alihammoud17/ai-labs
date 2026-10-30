using System.Diagnostics;
using AiLab.Domain.DTOs;
using AiLab.Domain.Enums;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Domain.Responses;
using AiLab.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace AiLab.Application.Services;

public sealed class AskService(IVectorSearchService vectorSearchService, AskSettings settings, ILogger<AskService> logger) : IAskService
{

    private readonly IVectorSearchService _vectorSearchService = vectorSearchService;

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

    // 4.3 (AskPrompt.Build) and 4.4 (the model call, CitationCheck) go here
    private Task<AskResponse> GenerateAsync(string question, IReadOnlyList<SearchHit> hits, AskRetrieval retrieval,
        long retrievalMs, CancellationToken ct) =>
        throw new NotImplementedException("Generation isn't written yet (4.3, 4.4).");

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
