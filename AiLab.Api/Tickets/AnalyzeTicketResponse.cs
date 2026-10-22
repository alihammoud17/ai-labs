namespace AiLab.Api.Tickets;

// What the endpoint returns: the analysis plus what it took to get it (for testing and experiments.md)
public record AnalyzeTicketResponse(TicketAnalysis Analysis, AnalysisDiagnostics Diagnostics);

public record AnalysisDiagnostics(
    string? Model,
    List<ToolCall> ToolCalls,
    int RoundTrips,
    long? InputTokens,
    long? OutputTokens,
    decimal? Cost,
    bool StrippedFence);

public record ToolCall(string Name, IDictionary<string, object?>? Arguments);

public record ErrorResponse(string Error);
