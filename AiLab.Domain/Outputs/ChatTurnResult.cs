namespace AiLab.Domain.Outputs;

// One finished chat turn. Empty means the reply had no text and the turn was dropped from the history.
// JsonValid and JsonError are only set while the format-forcing system message is on.
public record ChatTurnResult(TurnUsage Usage, bool CutOff, bool Empty, bool? JsonValid, string? JsonError);
