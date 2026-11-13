using AiLab.Application.Agents;
using AiLab.Infrastructure.Repositories;

namespace AiLab.Api.Tests;

public class TriageAgentInstructionsTests
{
    private static readonly string Text =
        TriageAgentInstructions.Build(new DateOnly(2026, 11, 13), new InMemoryChangeLogRepository().GetComponents());

    [Fact]
    public void Build_states_todays_date_and_weekday() =>
        Assert.Contains("Today is 2026-11-13 (Friday).", Text);

    [Fact]
    public void Build_lists_the_change_logs_components() =>
        Assert.Contains("auth, billing, invoices, tenants", Text);

    [Theory]
    [InlineData("Summary:")]
    [InlineData("What I found:")]
    [InlineData("Likely cause:")]
    [InlineData("Next step:")]
    public void Build_asks_for_the_four_parts(string heading) =>
        Assert.Contains(heading, Text);

    [Fact]
    public void Build_allows_an_honest_unknown() =>
        Assert.Contains("I don't know", Text);
}
