namespace AiLab.Domain.Outputs;

// InputTokens: what the embedding provider counted, summed over the batches; null when it reported no usage
public sealed record IndexResult(int Indexed, long? InputTokens);
