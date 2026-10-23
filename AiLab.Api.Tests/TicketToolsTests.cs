using AiLab.Application.Services;
using AiLab.Domain.Entities;
using AiLab.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiLab.Api.Tests;

public class TicketToolsTests
{
    private readonly InMemoryChangeLogRepository _repository = new();

    [Theory]
    [InlineData("auth")]
    [InlineData("billing")]
    [InlineData("invoices")]
    [InlineData("tenants")]
    public void ParseChanges_reads_back_exactly_what_the_tool_returned(string component)
    {
        TicketTools tools = new(_repository, NullLogger<TicketTools>.Instance);

        IReadOnlyList<ChangeEntry> parsed = TicketTools.ParseChanges(component, tools.GetRecentChanges(component));

        Assert.Equal(_repository.GetByComponent(component), parsed);
    }

    [Fact]
    public void ParseChanges_finds_nothing_in_the_unknown_component_message()
    {
        TicketTools tools = new(_repository, NullLogger<TicketTools>.Instance);

        Assert.Empty(TicketTools.ParseChanges("payments", tools.GetRecentChanges("payments")));
    }
}
