namespace AiLab.Domain.Outputs;

// Tokens and cost of one model call, and the session total after it.
// Tokens are null when the provider reported none; Cost is null when the tokens or the price are unknown,
// so an unknown price never shows up as $0. FinishReason is the provider's text ("stop", "length").
public record TurnUsage(long? InputTokens, long? OutputTokens, string? FinishReason, decimal? Cost, decimal SessionTotal, string? Model);
