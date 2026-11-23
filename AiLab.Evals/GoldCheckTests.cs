using AiLab.Evals.AskSet;
using AiLab.Evals.Evaluators;

namespace AiLab.Evals;

// Offline: the gold check on real rows of ask-set-v1.json, including the week 5 answers that shaped each rule.
// Asserts on the part of the reason that matters, not the whole sentence, so a wording change doesn't break them all.
public class GoldCheckTests
{
    private static RetrievedChunk Chunk(string file, int start, int end) => new(file, start, end);

    private static GoldCheckResult Check(string id, string answer, params RetrievedChunk[] citations) =>
        GoldCheck.Evaluate(AskSetData.Row(id), answer, status: "grounded", gate: "passed", citations);

    // A3's gold is docs/contracts.md line 45; 43-65 is the heading chunk that holds it
    private static readonly RetrievedChunk A3Chunk = Chunk("docs/contracts.md", 43, 65);

    // 1
    [Fact]
    public void Exact_row_with_the_value_and_a_citation_that_holds_the_gold_line_is_correct()
    {
        var result = Check("A3", "The version is `2026-03-18` [1]", A3Chunk);

        Assert.Equal(GoldVerdict.Correct, result.Verdict);
        Assert.Contains("2026-03-18", result.Reason);
    }

    // 2
    [Fact]
    public void Exact_row_with_another_value_is_wrong_and_names_the_missing_fact()
    {
        var result = Check("A3", "The version is `2026-03-17` [1]", A3Chunk);

        Assert.Equal(GoldVerdict.Wrong, result.Verdict);
        Assert.Contains("missing: 2026-03-18", result.Reason);
    }

    // 3: week 5's HL A3, the right date cited to the contract's first lines
    [Fact]
    public void Right_value_cited_to_a_chunk_without_the_gold_line_is_wrong_and_names_the_gold_line()
    {
        var result = Check("A3", "2026-03-18 [3]", Chunk("docs/contracts.md", 3, 5));

        Assert.Equal(GoldVerdict.Wrong, result.Verdict);
        Assert.Contains("facts found", result.Reason);
        Assert.Contains("docs/contracts.md:45", result.Reason);
    }

    [Fact]
    public void Right_value_with_no_citation_is_wrong()
    {
        var result = Check("A3", "The version is 2026-03-18.");

        Assert.Equal(GoldVerdict.Wrong, result.Verdict);
        Assert.Contains("no citation", result.Reason);
    }

    // 4: the second alternative proves the fact as well as the first
    [Theory]
    [InlineData("Login is limited to 10 per minute per IP [1].")]
    [InlineData("Login allows **10 requests per minute** per IP [1].")]
    public void Any_alternative_proves_the_fact(string answer)
    {
        var result = Check("G2", answer, Chunk("docs/architecture.md", 40, 46));

        Assert.Equal(GoldVerdict.Correct, result.Verdict);
    }

    // 5
    [Fact]
    public void List_row_with_one_fact_missing_is_wrong_and_names_exactly_that_fact()
    {
        var result = Check("N7", "Run `./dev.sh bootstrap`, then `./dev.sh run` [1].", Chunk("README.md", 44, 60));

        Assert.Equal(GoldVerdict.Wrong, result.Verdict);
        Assert.Equal("missing: dev.sh seed", result.Reason);
    }

    // 6: order and casing don't matter; backticks and line breaks are normalised away
    [Fact]
    public void List_row_with_every_fact_in_another_order_and_casing_is_correct()
    {
        var result = Check("N7", "Use `./DEV.SH RUN` after\n`./Dev.sh   Seed`, and start with dev.sh BOOTSTRAP [1].",
            Chunk("README.md", 44, 60));

        Assert.Equal(GoldVerdict.Correct, result.Verdict);
    }

    // 7: G1 needs both contracts (all mode); the billing contract alone holds line 45, not the webhook line 128
    [Fact]
    public void Both_needed_row_citing_one_source_is_wrong_and_names_the_uncited_one()
    {
        var result = Check("G1", "Billing is on `2026-03-18` [1] and webhooks on `2026-04-13`.", A3Chunk);

        Assert.Equal(GoldVerdict.Wrong, result.Verdict);
        Assert.Contains("docs/contracts.md:128", result.Reason);
        Assert.DoesNotContain("docs/contracts.md:45", result.Reason);
    }

    [Fact]
    public void Both_needed_row_citing_both_sources_is_correct()
    {
        var result = Check("G1", "Billing is on `2026-03-18` [1] and webhooks on `2026-04-13` [2].",
            A3Chunk, Chunk("docs/contracts.md", 120, 135));

        Assert.Equal(GoldVerdict.Correct, result.Verdict);
    }

    // 8
    [Theory]
    [InlineData("abstained", "Some unrelated sentence.")]
    [InlineData("grounded", "The documentation doesn't cover how long a refresh token is valid.")]
    [InlineData("grounded", "The documentation doesn’t cover refresh tokens.")] // curly apostrophe
    [InlineData("grounded", "notInDocs")]
    public void Abstain_row_that_abstains_by_status_or_in_words_is_correct(string status, string answer)
    {
        var result = GoldCheck.Evaluate(AskSetData.Row("D2"), answer, status, "passed", []);

        Assert.Equal(GoldVerdict.Correct, result.Verdict);
    }

    [Fact]
    public void Abstain_row_with_an_invented_answer_is_wrong_and_quotes_its_start()
    {
        var result = GoldCheck.Evaluate(AskSetData.Row("D2"), "Refresh tokens are valid for 7 days [2].", "grounded", "passed",
            [Chunk("docs/architecture.md", 38, 46)]);

        Assert.Equal(GoldVerdict.Wrong, result.Verdict);
        Assert.Contains("answered instead of abstaining", result.Reason);
        Assert.Contains("7 days", result.Reason);
    }

    // Left out of AbstainPhrases on purpose: llama3.2 said it on D2 in week 5 and then invented a number
    [Fact]
    public void Not_explicitly_stated_is_not_an_abstain()
    {
        var result = GoldCheck.Evaluate(AskSetData.Row("D2"),
            "It is not explicitly stated, but refresh tokens last 30 days.", "grounded", "passed", []);

        Assert.Equal(GoldVerdict.Wrong, result.Verdict);
    }

    // 9: E5 must be stopped by the gate; a model abstain means the gate let it through
    [Theory]
    [InlineData("too_far", "skipped", GoldVerdict.Correct)]
    [InlineData("no_hits", "skipped", GoldVerdict.Correct)]
    [InlineData("passed", "abstained", GoldVerdict.Wrong)]
    public void Gate_row_is_correct_only_when_gated(string gate, string status, GoldVerdict expected)
    {
        var result = GoldCheck.Evaluate(AskSetData.Row("E5"), "I can't help with CSS from these docs.", status, gate, []);

        Assert.Equal(expected, result.Verdict);
        Assert.Contains(gate, result.Reason);
    }

    // 10
    [Theory]
    [InlineData("A1")] // judge
    [InlineData("D1")] // partial
    public void Judge_and_partial_rows_are_not_checked(string id)
    {
        var result = Check(id, "Anything at all.");

        Assert.Equal(GoldVerdict.NotChecked, result.Verdict);
    }

    // A data error, not a verdict: 2.1's guard test keeps it from happening on the real set
    [Fact]
    public void Answer_row_without_a_match_list_throws()
    {
        var row = AskSetData.Row("A3") with { Match = null };

        var error = Assert.Throws<InvalidOperationException>(() =>
            GoldCheck.Evaluate(row, "2026-03-18", "grounded", "passed", [A3Chunk]));
        Assert.Contains("A3", error.Message);
    }
}
