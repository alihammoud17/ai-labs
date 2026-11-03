using System.Globalization;
using AiLab.Domain.Enums;
using AiLab.Domain.Responses;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace AiLab.Evals.Evaluators;

// What /ask returned besides the answer text: status, gate, distance, tokens, time. The judges don't need it,
// but the summary and layer 2 (abstaining) do.
public sealed class AskOutcomeContext(AskResponse response)
    : EvaluationContext(ContextName, $"status {Status(response)}, gate {Gate(response)}, best distance {response.Retrieval.BestDistance:0.####}")
{
    public const string ContextName = "Ask outcome";

    public AskResponse Response { get; } = response;

    // Something to judge: the gate let it through and the model didn't say "not in the docs"
    public bool Answered => Response.Retrieval.Gate == GateResult.Passed && Response.Grounding.Status != GroundingStatus.Abstained;

    internal static string Status(AskResponse r) => r.Grounding.Status.ToString().ToLowerInvariant();
    internal static string Gate(AskResponse r) => r.Retrieval.Gate switch
    {
        GateResult.Passed => "passed",
        GateResult.NoHits => "no_hits",
        GateResult.TooFar => "too_far",
        var g => g.ToString(),
    };
}

// Records the outcome as a metric, so it lands in the report and the stored results next to the scores.
// No pass/fail: whether abstaining was right depends on the row (layer 2), and Haiku sometimes says "not in the docs"
// with status grounded because it cites what the chunks do say (D2 in week 4).
public sealed class AskOutcomeEvaluator : IEvaluator
{
    public const string OutcomeMetricName = "Ask outcome";

    public IReadOnlyCollection<string> EvaluationMetricNames => [OutcomeMetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(IEnumerable<ChatMessage> messages, ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null, IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var metric = new StringMetric(OutcomeMetricName);
        var context = additionalContext?.OfType<AskOutcomeContext>().FirstOrDefault();
        if (context is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error($"A value of type {nameof(AskOutcomeContext)} was not found in the additional context."));
            return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
        }

        var r = context.Response;
        metric.Value = AskOutcomeContext.Status(r);
        metric.Reason = r.Grounding.Reason;
        metric.AddOrUpdateMetadata("gate", AskOutcomeContext.Gate(r));
        metric.AddOrUpdateMetadata("bestDistance", Format(r.Retrieval.BestDistance));
        metric.AddOrUpdateMetadata("citations", string.Join(" ", r.Citations.Select(c => $"[{c.N}] {c.Source}:{c.StartLine}-{c.EndLine}")));
        metric.AddOrUpdateMetadata("model", r.Usage.Model ?? "");
        metric.AddOrUpdateMetadata("inputTokens", Format(r.Usage.InputTokens));
        metric.AddOrUpdateMetadata("outputTokens", Format(r.Usage.OutputTokens));
        metric.AddOrUpdateMetadata("retrievalMs", Format(r.Timings.RetrievalMs));
        metric.AddOrUpdateMetadata("generationMs", Format(r.Timings.GenerationMs));
        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }

    private static string Format(double? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
    private static string Format(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
}

// Runs a judge only when /ask actually answered. A gated question (E5) has no answer, and "the docs don't cover it"
// isn't something to rate for groundedness or relevance: those runs get an empty, not-failed metric and no model call.
public sealed class AnsweredOnlyEvaluator(IEvaluator inner) : IEvaluator
{
    public IReadOnlyCollection<string> EvaluationMetricNames => inner.EvaluationMetricNames;

    public ValueTask<EvaluationResult> EvaluateAsync(IEnumerable<ChatMessage> messages, ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null, IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = additionalContext?.OfType<AskOutcomeContext>().FirstOrDefault();
        if (outcome is null || outcome.Answered)
            return inner.EvaluateAsync(messages, modelResponse, chatConfiguration, additionalContext, cancellationToken);

        string reason = $"Not judged: /ask abstained (gate {AskOutcomeContext.Gate(outcome.Response)}, status {AskOutcomeContext.Status(outcome.Response)}).";
        var metrics = inner.EvaluationMetricNames.Select(name => (EvaluationMetric)new NumericMetric(name, value: null, reason)
        {
            Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Inconclusive, failed: false, reason),
        });
        return new ValueTask<EvaluationResult>(new EvaluationResult(metrics));
    }
}
