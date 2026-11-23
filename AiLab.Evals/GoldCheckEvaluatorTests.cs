using AiLab.Domain.Enums;
using AiLab.Domain.Responses;
using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace AiLab.Evals;

// Offline: GoldCheckEvaluator turns GoldCheck's verdict into a "Gold check" metric from /ask's response.
// The verdict rules themselves are in GoldCheckTests; these check the mapping and the contexts.
public class GoldCheckEvaluatorTests
{
    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "q")];

    // A /ask response as the eval suite gets it: answer text, status, gate, citations with line ranges
    private static AskResponse Ask(string answer, GroundingStatus status, params AskCitation[] citations) => new(
        answer,
        new AskGrounding(status),
        citations,
        new AskRetrieval("heading", 5, 0.31, GateResult.Passed),
        new AskUsage("claude-haiku-5-5", 1000, 100),
        new AskTimings(50, 1500));

    private static AskCitation Cited(int n, string source, int start, int end) => new(n, source, null, start, end, 0.3);

    private static async Task<BooleanMetric> Evaluate(AskResponse ask, params EvaluationContext[] context)
    {
        var result = await new GoldCheckEvaluator().EvaluateAsync(Messages,
            new ChatResponse(new ChatMessage(ChatRole.Assistant, ask.Answer)),
            additionalContext: context, cancellationToken: TestContext.Current.CancellationToken);
        return result.Get<BooleanMetric>(GoldCheckEvaluator.GoldCheckMetricName);
    }

    [Fact]
    public async Task Correct_answer_is_true_and_not_failed()
    {
        var ask = Ask("The version is `2026-03-18` [1].", GroundingStatus.Grounded, Cited(1, "docs/contracts.md", 43, 65));

        var metric = await Evaluate(ask, new AskSetRowContext(AskSetData.Row("A3")), new AskOutcomeContext(ask));

        Assert.True(metric.Value);
        Assert.False(metric.Interpretation!.Failed);
        Assert.Contains("2026-03-18", metric.Reason);
    }

    // Week 5's HL A3: right date, cited chunk without line 45
    [Fact]
    public async Task Wrong_answer_is_false_and_failed_with_the_reason()
    {
        var ask = Ask("2026-03-18 [3]", GroundingStatus.Grounded, Cited(3, "docs/contracts.md", 3, 5));

        var metric = await Evaluate(ask, new AskSetRowContext(AskSetData.Row("A3")), new AskOutcomeContext(ask));

        Assert.False(metric.Value);
        Assert.True(metric.Interpretation!.Failed);
        Assert.Contains("docs/contracts.md:45", metric.Reason);
    }

    // No data must never look like a wrong answer: no value, inconclusive, not failed
    [Fact]
    public async Task Judge_row_has_no_value_and_does_not_fail()
    {
        var ask = Ask("Registration returns 202 Accepted [1].", GroundingStatus.Grounded, Cited(1, "docs/architecture.md", 32, 40));

        var metric = await Evaluate(ask, new AskSetRowContext(AskSetData.Row("A1")), new AskOutcomeContext(ask));

        Assert.Null(metric.Value);
        Assert.Equal(EvaluationRating.Inconclusive, metric.Interpretation!.Rating);
        Assert.False(metric.Interpretation.Failed);
    }

    [Fact]
    public async Task Missing_row_context_is_a_diagnostic_not_a_false()
    {
        var ask = Ask("2026-03-18 [1]", GroundingStatus.Grounded, Cited(1, "docs/contracts.md", 43, 65));

        var metric = await Evaluate(ask, new AskOutcomeContext(ask));

        Assert.Null(metric.Value);
        Assert.Contains(metric.Diagnostics!, d =>
            d.Severity == EvaluationDiagnosticSeverity.Error && d.Message.Contains(nameof(AskSetRowContext)));
    }
}
