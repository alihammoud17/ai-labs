namespace AiLab.Api.ChangeLog;

// One deployed change to a component of the SaaS
public record ChangeEntry(string Component, DateTimeOffset ChangedAt, string Version, string Description);
