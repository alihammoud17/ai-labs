using AiLab.Domain.Outputs;
using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;
using AiLab.Evals.Retrieval;
using AiLab.Evals.Summary;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace AiLab.Evals;

// Offline (week 8 Task 5.2): Knowledge Base answers stored in /ask's shape and scored by the same GoldCheck; no gate means
// E5 is wrong; no token counts means the cost reads "?", never $0.
public class KnowledgeBaseOutcomeEvaluatorTests
{
    private const string Arn = "arn:aws:bedrock:us-east-1::foundation-model/amazon.nova-lite-v1:0";
    private static readonly RetrievedChunk ContractsPassage = new("docs/contracts.md", 1, 63);

    private static async Task<EvaluationResult> Evaluate(string id, string answer, params RetrievedChunk[] citations)
    {
        var kb = new KnowledgeBaseAnswer(answer, [], "session");
        return await new KnowledgeBaseOutcomeEvaluator().EvaluateAsync(
            [new ChatMessage(ChatRole.User, AskSetData.Row(id).Question)],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)),
            additionalContext: [new KnowledgeBaseAskContext(kb, citations, [], Arn, 12.5), new AskSetRowContext(AskSetData.Row(id))],
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_quoted_passage_holding_the_fact_and_its_gold_line_is_correct_and_stored_like_an_ask_run()
    {
        var result = await Evaluate("A3", "No language model runs in this emulator. … Current contract version: `2026-03-18`. …", ContractsPassage);

        Assert.True(result.Get<BooleanMetric>("Gold check").Value);
        var outcome = result.Get<StringMetric>("Ask outcome");
        Assert.Equal("grounded", outcome.Value);
        var meta = outcome.Metadata!;
        Assert.Equal("none", meta["gate"]);
        Assert.Equal(Arn, meta["model"]);
        Assert.Equal("", meta["inputTokens"]);
        Assert.Equal("12.5", meta["callMs"]);
        Assert.Equal([ContractsPassage], EvalSummary.ParseCitations(meta["citations"])); // what GoldCheckReport reads back
    }

    [Fact]
    public async Task Without_a_gate_E5_is_wrong()
    {
        var result = await Evaluate("E5", "No language model runs in this emulator. … # Architecture …", new RetrievedChunk("docs/architecture.md", 1, 24));

        var gold = result.Get<BooleanMetric>("Gold check");
        Assert.False(gold.Value);
        Assert.Contains("none", gold.Reason);
    }

    [Fact]
    public void A_kb_run_with_no_token_counts_costs_unknown_not_zero()
    {
        var noScores = EvalSummary.Judged.ToDictionary(n => n, _ => (double?)null);
        var noFailures = EvalSummary.Judged.ToDictionary(n => n, _ => (bool?)null);
        var run = new RunRecord("A3", "Single fact", "answer", "1", "grounded",
            new Dictionary<string, string> { ["gate"] = "none", ["model"] = Arn, ["inputTokens"] = "", ["outputTokens"] = "", ["citations"] = "[1] docs/contracts.md:1-63" },
            null, null, null, noScores, noFailures, [], 0, "`2026-03-18`", [ContractsPassage]);

        var row = CostPerCorrectReport.Summarise([run]);
        string report = CostPerCorrectReport.Markdown([new("kb-emulator", [run])], new DateOnly(2026, 11, 25), expectedRuns: 1);

        Assert.Equal((1, 1, 1), (row.Checked, row.Correct, row.Reached));
        Assert.Null(row.Cost);
        Assert.Contains($"no price for {Arn}", report);
        // The row: cost, cost per correct and cost per 1,000 all "?", latency "-" (no retrieval/generation split)
        Assert.Contains($"| kb-emulator | 1 | 1 | 1 | 100% | 1 / 1 | 1 | `{Arn}` | ? | ? | ? | - | - |", report);
        Assert.DoesNotContain("$0.0", report);
        Assert.DoesNotContain("$0 (local)", report);
    }
}
