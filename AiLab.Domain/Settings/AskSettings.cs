using AiLab.Domain.Constants;

namespace AiLab.Domain.Settings;

// The "Ask" section of AiLab.Rag's appsettings.json: retrieval, the distance gate and generation limits for POST /ask
public record AskSettings
{
    // Chunks retrieved when the request has no top
    public int Top { get; init; } = 5;

    // The largest top a request may ask for (each chunk is ~500 tokens of prompt)
    public int MaxTop { get; init; } = 10;

    // Which chunker's chunks are searched (section 12 of experiments.md: heading ranked better than fixed)
    public string Chunker { get; init; } = ChunkerNames.Heading;

    // The gate: no hits, or a best hit farther than this (cosine distance), abstains without a model call. Calibrated in 4.8.
    public double MaxDistance { get; init; } = 0.50;

    public int MaxQuestionChars { get; init; } = 500;

    // ChatOptions.MaxOutputTokens for the answer (Haiku's thinking counts toward it too)
    public int MaxOutputTokens { get; init; } = 800;

    // The system prompt, relative to the repo root
    public string PromptPath { get; init; } = "prompts/ask-v1.md";

    // The rate limiter on POST /ask: requests per client IP per minute (fixed window). Every allowed request may cost a model call.
    public int PermitsPerMinute { get; init; } = 10;
}
