using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AiLab.Console.Workflow;

// What the workflow's sorting step (step 1) returns: which path the ticket takes. The model fills it; the code then
// checks the component against the change log (TicketWorkflow.SortAsync) and decides the lookups itself.
// Two separate fields because no area is a change-log component: a webhooks ticket has an area but maybe no component.
// The [Description]s go into the JSON schema the model sees.
public sealed record TicketRoute(
    [property: Description("The change-log component the ticket is about, exactly as listed, or null if none fits.")]
    string? Component,
    [property: Description("The area of the tenant's own data that can show the problem.")]
    TicketArea? Area);

// An enum, not a string: the schema the model sees lists the allowed values ("enum": [...]) instead of describing
// them in prose. In the smoke test llama3.2 answered area "allowed origins" for a CORS question when it was a string.
// A value outside the list makes the whole reply unreadable, and the workflow falls back to unknown.
[JsonConverter(typeof(JsonStringEnumConverter<TicketArea>))]
public enum TicketArea
{
    // Webhook deliveries or endpoints → list_webhook_endpoints
    [JsonStringEnumMemberName("webhooks")] Webhooks,

    // Browser CORS errors or allowed origins → list_cors_origins
    [JsonStringEnumMemberName("cors")] Cors,

    // API usage or traffic → get_usage_summary
    [JsonStringEnumMemberName("usage")] Usage,

    // None of these: no SaaS lookup
    [JsonStringEnumMemberName("unknown")] Unknown,
}

public static class TicketAreas
{
    // The SaaS MCP tool for each area, with the arguments the code sends (the model chooses none of them).
    // Usage covers 7 days, since the tickets say "this week"; the other tools take no arguments.
    public static (string Tool, IReadOnlyDictionary<string, object?> Arguments)? LookupFor(TicketArea area) => area switch
    {
        TicketArea.Webhooks => ("list_webhook_endpoints", new Dictionary<string, object?>()),
        TicketArea.Cors => ("list_cors_origins", new Dictionary<string, object?>()),
        TicketArea.Usage => ("get_usage_summary", new Dictionary<string, object?> { ["days"] = 7 }),
        _ => null,
    };

    // The lowercase name the model sees and the console prints
    public static string Name(TicketArea area) => area.ToString().ToLowerInvariant();
}
