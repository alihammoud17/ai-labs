using System.ComponentModel;

namespace AiLab.Api.Tickets;

// What the endpoint returns: the analysis plus what it took to get it (for testing and experiments.md).
// The [Description]s here only document the API (OpenAPI); they are never sent to the model.
[Description("The analysis and how it was produced.")]
public record AnalyzeTicketResponse(
    [property: Description("The model's analysis of the ticket.")]
    TicketAnalysis Analysis,

    [property: Description("Tool calls, round trips, tokens and cost of this request. For testing.")]
    AnalysisDiagnostics Diagnostics);

[Description("How the analysis was produced.")]
public record AnalysisDiagnostics(
    [property: Description("The model that answered, e.g. llama3.2 or claude-haiku-5-5.")]
    string? Model,

    [property: Description("Every tool call the model made, in order, across all round trips.")]
    List<ToolCall> ToolCalls,

    [property: Description("Model calls made for this request: one per tool-call step plus the final answer.")]
    int RoundTrips,

    [property: Description("Input tokens summed over all round trips.")]
    long? InputTokens,

    [property: Description("Output tokens summed over all round trips.")]
    long? OutputTokens,

    [property: Description("Cost in USD from the prices in ModelPrices; null if the model isn't in the table.")]
    decimal? Cost,

    [property: Description("True if the reply only parsed after removing a ```json fence around it.")]
    bool StrippedFence);

[Description("One tool call requested by the model.")]
public record ToolCall(
    [property: Description("The tool's name, e.g. GetRecentChanges.")]
    string Name,

    [property: Description("The arguments the model passed, e.g. { \"component\": \"billing\" }.")]
    IDictionary<string, object?>? Arguments);

[Description("A short error message, safe to show to the caller.")]
public record ErrorResponse(
    [property: Description("What went wrong, in one sentence.")]
    string Error);
