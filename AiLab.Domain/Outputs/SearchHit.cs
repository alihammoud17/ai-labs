namespace AiLab.Domain.Outputs;

// Score is cosine distance: 0 = identical, lower = closer
public sealed record SearchHit(string Key, string Text, string Source, double Score);
