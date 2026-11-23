using AiLab.Evals.AskSet;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace AiLab.Evals.Evaluators;

// "Correct" without a judge, as a metric: GoldCheck on the live /ask result. No model, so it costs nothing and gives
// the same verdict every time; the stored-results report (Block 3) calls the same GoldCheck on the same plain values.
public sealed class GoldCheckEvaluator : IEvaluator
{
    public const string GoldCheckMetricName = "Gold check";

    public IReadOnlyCollection<string> EvaluationMetricNames => [GoldCheckMetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(IEnumerable<ChatMessage> messages, ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null, IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var metric = new BooleanMetric(GoldCheckMetricName);
        var result = new EvaluationResult(metric);

        var row = additionalContext?.OfType<AskSetRowContext>().FirstOrDefault();
        var outcome = additionalContext?.OfType<AskOutcomeContext>().FirstOrDefault();
        if (row is null || outcome is null)
        {
            // Like RecallAtK: a missing context is a diagnostic, not a false
            string missing = row is null ? nameof(AskSetRowContext) : nameof(AskOutcomeContext);
            metric.AddDiagnostics(EvaluationDiagnostic.Error($"A value of type {missing} was not found in the additional context."));
            return new ValueTask<EvaluationResult>(result);
        }

        // Plain values out of /ask's response: the same ones Block 3 reads from the stored Ask outcome metadata
        var ask = outcome.Response;
        var citations = ask.Citations.Select(c => new RetrievedChunk(c.Source, c.StartLine, c.EndLine)).ToList();
        var check = GoldCheck.Evaluate(row.Row, modelResponse.Text, AskOutcomeContext.Status(ask), AskOutcomeContext.Gate(ask), citations);

        metric.Reason = check.Reason;
        switch (check.Verdict)
        {
            case GoldVerdict.Correct:
                metric.Value = true;
                metric.Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Exceptional, reason: check.Reason);
                break;
            case GoldVerdict.Wrong:
                metric.Value = false;
                metric.Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Unacceptable, failed: true, check.Reason);
                break;
            default:
                // judge and partial rows: no value, so "not checked" can never read as "wrong" (or as 0% correct)
                metric.Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Inconclusive, failed: false, check.Reason);
                break;
        }

        return new ValueTask<EvaluationResult>(result);
    }
}

// The whole ask-set row, for GoldCheck (GoldSourcesContext carries only the gold sources and mode)
public sealed class AskSetRowContext(AskSetRow row)
    : EvaluationContext(ContextName, Describe(row))
{
    public const string ContextName = "Ask-set row (Gold check)";

    public AskSetRow Row { get; } = row;

    // "A15, all items, match: contract version is supported / supported contract version; required fields and timestamps; …"
    private static string Describe(AskSetRow row) =>
        $"{row.Id}, {row.Check}" + (row.Match is null
            ? ""
            : ", match: " + string.Join("; ", row.Match.Select(fact => string.Join(" / ", fact))));
}
