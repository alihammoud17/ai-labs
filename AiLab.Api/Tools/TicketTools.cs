using AiLab.Api.ChangeLog;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace AiLab.Api.Tools;

// The tool the model can call. The function-invocation middleware runs it when the model asks for it.
public class TicketTools(IChangeLogRepository changeLog, ILogger<TicketTools> logger)
{
    // The [Description]s become the tool's description and parameter schema, which is all the model sees of this method
    [Description("Gets the recent deployed changes for one component of the SaaS, newest first. Call it before naming a probable cause.")]
    public string GetRecentChanges(
        [Description("The component to look up: one of auth, billing, invoices, tenants.")] string component)
    {
        logger.LogInformation("GetRecentChanges called with component {Component}", component);

        IReadOnlyList<ChangeEntry> changes = changeLog.GetByComponent(component);

        // An empty list would read as "nothing changed"; naming the real components lets the model retry with one
        if (changes.Count == 0)
            return $"Unknown component '{component}'. Known components: {string.Join(", ", changeLog.GetComponents())}.";

        return string.Join("\n", changes.Select(c => $"{c.ChangedAt:yyyy-MM-dd HH:mm zzz} v{c.Version}: {c.Description}"));
    }

    // Wraps the method as an AIFunction (name, description, JSON schema). The analyzer puts it in ChatOptions.Tools.
    public AIFunction AsAIFunction() => AIFunctionFactory.Create(GetRecentChanges);
}
