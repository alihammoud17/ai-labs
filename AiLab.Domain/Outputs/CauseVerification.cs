using AiLab.Domain.Entities;
using AiLab.Domain.Enums;
using System.ComponentModel;

namespace AiLab.Domain.Outputs;

[Description("What the server checked about the probable cause. Written by the server, never by the model.")]
public record CauseVerification(
    [property: Description("grounded: cites only versions the tool returned in this request (real evidence, not proof it explains the problem). unknown: the model says the cause is unknown. unverified: cites a version the tool didn't return, or names a cause without a version.")]
    CauseStatus CauseStatus,

    [property: Description("One sentence on why the status was given.")]
    string Reason,

    [property: Description("The change-log entries the cause relies on, copied from what the tool returned. Empty unless the status is grounded.")]
    IReadOnlyList<Evidence> Evidence,

    [property: Description("True if the model called GetRecentChanges in this request.")]
    bool ToolCalled,

    [property: Description("True if a GetRecentChanges call failed in this request.")]
    bool ToolFailed);

[Description("One change-log entry, as GetRecentChanges returned it.")]
public record Evidence(string Component, string Version, DateTimeOffset ChangedAt, string Description)
{
    public static Evidence From(ChangeEntry c) => new(c.Component, c.Version, c.ChangedAt, c.Description);
}
