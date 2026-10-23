using AiLab.Api.ChangeLog;
using AiLab.Api.Tools;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AiLab.Api.Grounding;

// What GetRecentChanges actually returned in one request, read from the tool loop's messages
public record ToolEvidence(bool ToolCalled, bool ToolFailed, IReadOnlyList<ChangeEntry> Returned)
{
    public static ToolEvidence From(IEnumerable<ChatMessage> messages)
    {
        List<AIContent> contents = messages.SelectMany(m => m.Contents).ToList();

        // Call id → component, so each result is matched to the component the model asked for
        Dictionary<string, string> components = contents.OfType<FunctionCallContent>()
            .Where(c => c.Name == nameof(TicketTools.GetRecentChanges))
            .ToDictionary(c => c.CallId, c => c.Arguments?.TryGetValue("component", out object? value) == true ? value?.ToString()?.Trim() ?? "" : "");

        bool failed = false;
        List<ChangeEntry> returned = [];
        foreach (FunctionResultContent result in contents.OfType<FunctionResultContent>())
        {
            if (!components.TryGetValue(result.CallId, out string? component))
                continue;

            // The middleware records the tool's exception here; the model itself only got a generic error message
            if (result.Exception is not null)
            {
                failed = true;
                continue;
            }

            // The "Unknown component" message parses to no changes, so it adds no evidence
            if (ResultText(result.Result) is { } text)
                returned.AddRange(TicketTools.ParseChanges(component, text));
        }

        return new ToolEvidence(components.Count > 0, failed, returned.DistinctBy(c => c.Version).ToList());
    }

    // The tool returns a string; the middleware may keep it as is or as a JSON string
    private static string? ResultText(object? result) => result switch
    {
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        _ => null,
    };
}
