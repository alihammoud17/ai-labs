using Microsoft.Extensions.AI;

namespace AiLab.Application.Services;

// The chat experiment switches, in one place. Change the values here between experiment runs (stage 1, step 4).
public static class ChatExperimentSettings
{
    // Crude guard against silently overflowing the model's context window.
    public const int MaxHistoryMessages = 20;

    // Format-forcing system message (step 4.4). Set to null for normal chat.
    // It's the first message in history, and /reset keeps it.
    public static readonly string? JsonSystemMessage = null; // """You are a JSON generator. You will always respond with valid JSON in the shape: {"answer": string, "confidence": number between 0 and 1}. No other text."""

    // Settings sent with every request. Change the values here between experiment runs (step 4);
    // an empty ChatOptions means "use the provider's defaults".
    public static ChatOptions CreateOptions() => new()
    {
        // Temperature = 1.0f,
        // MaxOutputTokens = 10,
    };
}
