using AiLab.Domain.Entities;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AiLab.Api.Tools;

// The tool the model can call. The function-invocation middleware runs it when the model asks for it.
public class TicketTools(IChangeLogRepository changeLog, ILogger<TicketTools> logger)
{
    // The [Description]s become the tool's description and parameter schema, which is all the model sees of this method
    [Description("Gets the recent deployed changes for one component of the SaaS, newest first. Call it before naming a probable cause.")]
    // The parameter's description is set in AsAIFunction, from the repository's components
    public string GetRecentChanges(string component)
    {
        logger.LogInformation("GetRecentChanges called with component {Component}", component);

        IReadOnlyList<ChangeEntry> changes;
        try
        {
            changes = changeLog.GetByComponent(component);
        }
        catch (Exception ex)
        {
            // Logged here with the details; the model only gets the middleware's generic error (IncludeDetailedErrors is off)
            logger.LogError(ex, "GetRecentChanges failed for component {Component}", component);
            throw new ChangeLogUnavailableException(ex);
        }

        // An empty list would read as "nothing changed"; naming the real components lets the model retry with one
        if (changes.Count == 0)
            return $"{UnknownComponentPrefix} '{component}'. Known components: {string.Join(", ", changeLog.GetComponents())}.";

        return string.Join("\n", changes.Select(FormatChange));
    }

    public const string UnknownComponentPrefix = "Unknown component";

    // One line per change; ParseChanges reads it back, so keep the two together
    private const string DateFormat = "yyyy-MM-dd HH:mm zzz";
    private static readonly Regex ChangeLine = new(@"^(?<at>\d{4}-\d{2}-\d{2} \d{2}:\d{2} [+-]\d{2}:\d{2}) v(?<version>\S+): (?<description>.*)$", RegexOptions.Multiline);

    private static string FormatChange(ChangeEntry c) =>
        $"{c.ChangedAt.ToString(DateFormat, CultureInfo.InvariantCulture)} v{c.Version}: {c.Description}";

    // The changes in one tool result, exactly as the model saw them. The grounding check builds its evidence from
    // these, not from a new repository query. The "Unknown component" message has no change lines, so it gives none.
    public static IReadOnlyList<ChangeEntry> ParseChanges(string component, string toolResult) =>
        ChangeLine.Matches(toolResult.ReplaceLineEndings("\n"))
            .Select(m => new ChangeEntry(component,
                DateTimeOffset.ParseExact(m.Groups["at"].Value, DateFormat, CultureInfo.InvariantCulture),
                m.Groups["version"].Value,
                m.Groups["description"].Value.TrimEnd()))
            .ToList();

    // Wraps the method as an AIFunction (name, description, JSON schema). The analyzer puts it in ChatOptions.Tools.
    // The component list comes from the repository, so a new component shows up in the schema the model sees.
    public AIFunction AsAIFunction() => AIFunctionFactory.Create(GetRecentChanges, new AIFunctionFactoryOptions
    {
        JsonSchemaCreateOptions = new AIJsonSchemaCreateOptions
        {
            ParameterDescriptionProvider = parameter => parameter.Name == "component"
                ? $"The component to look up: one of {string.Join(", ", changeLog.GetComponents())}."
                : null,
        },
    });
}
