using System.Globalization;
using AiLab.Domain.Outputs;
using AiLab.Evals.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace AiLab.Evals.Evaluators;

// One RetrieveAndGenerate answer from the Knowledge Base, with its citations already placed in the corpus (PassageLocator)
// and the ones that couldn't be placed. ModelArn: the model the KB answered with; CallMs: the one call, retrieval and
// generation together (the service doesn't split them).
public sealed class KnowledgeBaseAskContext(KnowledgeBaseAnswer answer, IReadOnlyList<RetrievedChunk> citations,
    IReadOnlyList<UnlocatedPassage> unlocated, string modelArn, double callMs)
    : EvaluationContext(ContextName, $"{citations.Count} located citation(s), {unlocated.Count} not located, model {modelArn}")
{
    public const string ContextName = "Knowledge Base answer";

    // The KB has no abstain status and no gate: every question gets an answer from the model (week 8, Task 1.4)
    public const string Status = "grounded";
    public const string Gate = "none";

    public KnowledgeBaseAnswer Answer { get; } = answer;
    public IReadOnlyList<RetrievedChunk> Citations { get; } = citations;
    public IReadOnlyList<UnlocatedPassage> Unlocated { get; } = unlocated;
    public string ModelArn { get; } = modelArn;
    public double CallMs { get; } = callMs;
}

// Stores a Knowledge Base answer in the same shape as /ask's runs, so GoldCheckReport and CostPerCorrectReport read both:
// "Ask outcome" (status grounded, gate none, citations as "[n] source:start-end", the model, NO token counts: the service
// reports none, so the cost report shows "?", never $0) and "Gold check" from the same GoldCheck.Evaluate as /ask's.
// Latency goes in "callMs", not in retrievalMs / generationMs, which a single managed call can't split honestly.
public sealed class KnowledgeBaseOutcomeEvaluator : IEvaluator
{
    public IReadOnlyCollection<string> EvaluationMetricNames => [AskOutcomeEvaluator.OutcomeMetricName, GoldCheckEvaluator.GoldCheckMetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(IEnumerable<ChatMessage> messages, ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null, IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = new StringMetric(AskOutcomeEvaluator.OutcomeMetricName);
        var gold = new BooleanMetric(GoldCheckEvaluator.GoldCheckMetricName);
        var result = new EvaluationResult(outcome, gold);

        var kb = additionalContext?.OfType<KnowledgeBaseAskContext>().FirstOrDefault();
        var row = additionalContext?.OfType<AskSetRowContext>().FirstOrDefault();
        if (kb is null || row is null)
        {
            string missing = kb is null ? nameof(KnowledgeBaseAskContext) : nameof(AskSetRowContext);
            foreach (var metric in new EvaluationMetric[] { outcome, gold })
                metric.AddDiagnostics(EvaluationDiagnostic.Error($"A value of type {missing} was not found in the additional context."));
            return new ValueTask<EvaluationResult>(result);
        }

        outcome.Value = KnowledgeBaseAskContext.Status;
        outcome.Reason = "Knowledge Base answer: no abstain status and no gate.";
        outcome.AddOrUpdateMetadata("gate", KnowledgeBaseAskContext.Gate);
        outcome.AddOrUpdateMetadata("citations", string.Join(" ", kb.Citations.Select((c, i) => $"[{i + 1}] {c.Source}:{c.StartLine}-{c.EndLine}")));
        outcome.AddOrUpdateMetadata("notLocated", string.Join("; ", kb.Unlocated.Select(u => $"{u.Source}: {u.Reason}")));
        outcome.AddOrUpdateMetadata("model", kb.ModelArn);
        outcome.AddOrUpdateMetadata("inputTokens", "");
        outcome.AddOrUpdateMetadata("outputTokens", "");
        outcome.AddOrUpdateMetadata("callMs", kb.CallMs.ToString("0.####", CultureInfo.InvariantCulture));

        var check = GoldCheck.Evaluate(row.Row, modelResponse.Text, KnowledgeBaseAskContext.Status, KnowledgeBaseAskContext.Gate, kb.Citations);
        gold.Reason = check.Reason;
        switch (check.Verdict)
        {
            case GoldVerdict.Correct:
                gold.Value = true;
                gold.Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Exceptional, reason: check.Reason);
                break;
            case GoldVerdict.Wrong:
                gold.Value = false;
                gold.Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Unacceptable, failed: true, check.Reason);
                break;
            default:
                gold.Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Inconclusive, failed: false, check.Reason);
                break;
        }

        return new ValueTask<EvaluationResult>(result);
    }
}
