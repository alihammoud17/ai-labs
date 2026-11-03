using AiLab.Evals.AskSet;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace AiLab.Evals.Evaluators;

// Layer 1 of the eval plan, without a model: is the answer among the chunks /ask sent?
// A hit is a retrieved chunk from the gold file whose line range contains a gold line. Lines, not chunk keys,
// so the gold set stays valid when the chunker changes.
// RetrievalEvaluator (MEAI) asks a judge whether the chunks look relevant; it doesn't know the gold answers. This does.
public sealed class RecallAtKEvaluator(int k = 5) : IEvaluator
{
    public const string FirstHitRankMetricName = "First hit rank";

    public string RecallMetricName { get; } = $"Recall@{k}";

    public IReadOnlyCollection<string> EvaluationMetricNames => [RecallMetricName, FirstHitRankMetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(IEnumerable<ChatMessage> messages, ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null, IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var recall = new NumericMetric(RecallMetricName);
        var firstHit = new NumericMetric(FirstHitRankMetricName);
        var result = new EvaluationResult(recall, firstHit);

        var gold = additionalContext?.OfType<GoldSourcesContext>().FirstOrDefault();
        var retrieved = additionalContext?.OfType<RetrievedChunksContext>().FirstOrDefault();
        if (gold is null || retrieved is null)
        {
            // Like the built-in evaluators: a missing context is a diagnostic, not a low score
            string missing = gold is null ? nameof(GoldSourcesContext) : nameof(RetrievedChunksContext);
            foreach (var metric in new[] { recall, firstHit })
                metric.AddDiagnostics(EvaluationDiagnostic.Error($"A value of type {missing} was not found in the additional context."));
            return new ValueTask<EvaluationResult>(result);
        }

        // abstain and gate rows: nothing to find, so no value and no failure
        if (gold.Sources.Count == 0)
        {
            const string reason = "No gold source: this question isn't in the docs.";
            recall.Reason = firstHit.Reason = reason;
            recall.Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Inconclusive, failed: false, reason);
            firstHit.Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Inconclusive, failed: false, reason);
            return new ValueTask<EvaluationResult>(result);
        }

        var top = retrieved.Chunks.Take(k).ToList();
        var hitSources = gold.Sources.Where(g => top.Any(c => IsHit(g, c))).ToList();

        // any: one gold source answers alone. all: every gold source is needed (B6, G1), so the share found.
        recall.Value = gold.Mode == "all"
            ? (double)hitSources.Count / gold.Sources.Count
            : hitSources.Count > 0 ? 1 : 0;
        recall.Reason = hitSources.Count == 0
            ? $"No gold source in the top {k}: {Describe(gold.Sources)}."
            : $"{hitSources.Count} of {gold.Sources.Count} gold sources in the top {k} (mode {gold.Mode}): {Describe(hitSources)}.";
        recall.Interpretation = recall.Value == 1
            ? new EvaluationMetricInterpretation(EvaluationRating.Exceptional, reason: "Every needed gold source was sent.")
            : new EvaluationMetricInterpretation(EvaluationRating.Unacceptable, failed: true, reason: "A needed gold source wasn't sent.");

        // For MRR later: 1 / rank, averaged over the answerable rows
        int index = top.FindIndex(c => gold.Sources.Any(g => IsHit(g, c)));
        if (index >= 0)
        {
            firstHit.Value = index + 1;
            firstHit.Reason = $"First gold chunk at rank {index + 1}: {top[index]}.";
            firstHit.Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Good, reason: firstHit.Reason);
        }
        else
        {
            firstHit.Reason = $"No gold chunk in the top {k}.";
            // Recall@k already fails for this; the rank only has nothing to report
            firstHit.Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Inconclusive, failed: false, firstHit.Reason);
        }

        return new ValueTask<EvaluationResult>(result);
    }

    public static bool IsHit(GoldSource gold, RetrievedChunk chunk) =>
        chunk.Source == gold.File && gold.Lines.Any(n => chunk.StartLine <= n && n <= chunk.EndLine);

    private static string Describe(IEnumerable<GoldSource> sources) =>
        string.Join(", ", sources.Select(s => $"{s.File}:{string.Join(',', s.Lines)}"));
}

// One chunk as /search returned it, in rank order
public sealed record RetrievedChunk(string Source, int StartLine, int EndLine)
{
    public override string ToString() => $"{Source}:{StartLine}-{EndLine}";
}

// The row's gold sources and mode (any | all)
public sealed class GoldSourcesContext(IReadOnlyList<GoldSource> sources, string mode)
    : EvaluationContext(ContextName, sources.Count == 0
        ? "none"
        : $"{mode}: " + string.Join("; ", sources.Select(s => $"{s.File}:{string.Join(',', s.Lines)}")))
{
    public const string ContextName = "Gold sources (Recall)";

    public IReadOnlyList<GoldSource> Sources { get; } = sources;
    public string Mode { get; } = mode;

    public static GoldSourcesContext From(AskSetRow row) => new(row.Gold, row.GoldMode);
}

// The chunks /ask sent, closest first, with their line ranges
public sealed class RetrievedChunksContext(IReadOnlyList<RetrievedChunk> chunks)
    : EvaluationContext(ContextName, string.Join("\n", chunks.Select((c, i) => $"{i + 1}. {c}")))
{
    public const string ContextName = "Retrieved chunks (Recall)";

    public IReadOnlyList<RetrievedChunk> Chunks { get; } = chunks;
}
