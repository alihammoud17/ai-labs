using AiLab.Application.Ask;
using AiLab.Domain.DTOs;
using AiLab.Domain.Enums;
using AiLab.Domain.Outputs;

namespace AiLab.Api.Tests;

public class CitationCheckTests
{
    // Three sources sent, numbered 1..3 as AskPrompt.Build numbers them
    private static readonly IReadOnlyDictionary<int, SearchHit> Sources = new Dictionary<int, SearchHit>
    {
        [1] = Hit("docs/architecture.md", 0, "Architecture > Tenancy", 20, 31, 0.21),
        [2] = Hit("docs/decisions.md", 3, "Decisions > ADR-004", 40, 44, 0.28),
        [3] = Hit("README.md", 1, null, 5, 12, 0.35),
    };

    [Fact]
    public void Every_cited_number_sent_is_grounded_and_citations_come_from_the_map()
    {
        var result = CitationCheck.Evaluate(new("Tenants come from the JWT [1]. See ADR-004 [2].", [2, 1], false), Sources);

        Assert.Equal(GroundingStatus.Grounded, result.Grounding.Status);
        Assert.Equal([1, 2], result.Citations.Select(c => c.N));
        var first = result.Citations[0];
        Assert.Equal(("docs/architecture.md", "Architecture > Tenancy", 20, 31, 0.21),
            (first.Source, first.Heading, first.StartLine, first.EndLine, first.Distance));
    }

    [Fact]
    public void A_number_that_was_not_sent_is_unverified_and_the_real_ones_are_still_listed()
    {
        var result = CitationCheck.Evaluate(new("Rate limits are per tenant [1][7].", [1, 7], false), Sources);

        Assert.Equal(GroundingStatus.Unverified, result.Grounding.Status);
        Assert.Contains("[7]", result.Grounding.Reason);
        Assert.Equal([1], result.Citations.Select(c => c.N));
    }

    [Fact]
    public void A_number_only_in_the_text_counts_as_cited()
    {
        var result = CitationCheck.Evaluate(new("Tenants come from the JWT [1][9].", [1], false), Sources);

        Assert.Equal(GroundingStatus.Unverified, result.Grounding.Status);
        Assert.Contains("[9]", result.Grounding.Reason);
    }

    [Theory]
    [InlineData("Tenants come from the JWT [1, 3].")]
    [InlineData("Tenants come from the JWT [1][3].")]
    [InlineData("Tenants come from the JWT [1] [3].")]
    public void Inline_forms_are_read_without_a_cited_list(string text)
    {
        var result = CitationCheck.Evaluate(new(text, [], false), Sources);

        Assert.Equal(GroundingStatus.Grounded, result.Grounding.Status);
        Assert.Equal([1, 3], result.Citations.Select(c => c.N));
    }

    [Fact]
    public void An_answer_without_citations_is_unverified()
    {
        var result = CitationCheck.Evaluate(new("Tenants come from the JWT.", [], false), Sources);

        Assert.Equal(GroundingStatus.Unverified, result.Grounding.Status);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public void A_year_in_brackets_is_not_a_citation()
    {
        var result = CitationCheck.Evaluate(new("Decided in [2024] [2].", [2], false), Sources);

        Assert.Equal(GroundingStatus.Grounded, result.Grounding.Status);
        Assert.Equal([2], result.Citations.Select(c => c.N));
    }

    [Fact]
    public void Not_in_docs_abstains_and_drops_any_citations()
    {
        var result = CitationCheck.Evaluate(new("The documentation doesn't cover invoices [1].", [1], true), Sources);

        Assert.Equal(GroundingStatus.Abstained, result.Grounding.Status);
        Assert.Empty(result.Citations);
    }

    private static SearchHit Hit(string source, int position, string? heading, int start, int end, double distance) =>
        new(new TextChunk("heading", source, position, $"text of {source}", heading, start, end), distance);
}
