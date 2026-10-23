using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AiLab.Application.Services;

// Reads the T out of a typed response, accepting one ```json fence around the whole reply
public static class StructuredReply
{
    // strippedFence tells the caller the reply only parsed after removing a fence
    public static bool TryParse<T>(ChatResponse<T> response, out T? result, out bool strippedFence)
    {
        strippedFence = false;
        if (response.TryGetResult(out result))
            return true;

        // TryGetResult doesn't strip fences. Haiku wrapped otherwise correct JSON in ```json ... ``` (experiments.md),
        // so strip a fence around the whole reply only; markdown inside values is content (section 4 decision).
        string text = response.Text.Trim();
        if (!text.StartsWith("```") || !text.EndsWith("```") || text.Length < 6)
            return false;

        int firstNewLine = text.IndexOf('\n');
        if (firstNewLine < 0)
            return false;

        string inner = text[(firstNewLine + 1)..^3];
        try
        {
            // Same options as GetResponseAsync<T>, so names and enums read the same way
            result = JsonSerializer.Deserialize<T>(inner, AIJsonUtilities.DefaultOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        strippedFence = result is not null;
        return result is not null;
    }
}
