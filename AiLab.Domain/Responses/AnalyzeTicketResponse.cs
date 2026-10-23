using AiLab.Domain.Outputs;
using System.ComponentModel;

namespace AiLab.Domain.Responses;

// What the endpoint returns. The model's guesses (aiAnalysis) are kept apart from what the server checked (verification),
// so a client can show, hide or discount the guess on its own. The [Description]s here only document the API (OpenAPI);
// they are never sent to the model.
[Description("The analysis, what the server verified about it, and how it was produced.")]
public record AnalyzeTicketResponse(
    [property: Description("Fixed server text, the same on every analysis. Show it with the analysis.")]
    string Notice,

    [property: Description("Everything the model wrote. A guess: check verification before relying on the probable cause.")]
    TicketAnalysis AiAnalysis,

    [property: Description("What the server checked about the probable cause, from the tool results of this request.")]
    CauseVerification Verification,

    [property: Description("Tool calls, round trips, tokens and cost of this request. For testing.")]
    AnalysisDiagnostics Diagnostics)
{
    // Server text, never model text
    public const string NoticeText = "AI-generated analysis. Verify before acting. A grounded cause cites a real recent change, but may still not explain the problem.";
}

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
    bool StrippedFence,

    [property: Description("True if the first reply was unusable and the model was asked once to reply again with only the JSON.")]
    bool Repaired = false);

[Description("One tool call requested by the model.")]
public record ToolCall(
    [property: Description("The tool's name, e.g. GetRecentChanges.")]
    string Name,

    [property: Description("The arguments the model passed, e.g. { \"component\": \"billing\" }.")]
    IDictionary<string, object?>? Arguments);
