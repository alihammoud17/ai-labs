using AiLab.Domain.Entities;
using AiLab.Domain.Interfaces;

namespace AiLab.Api.ChangeLog;

// Fake change log for the tool. Ticket A is explained by the billing change of 2026-10-19,
// ticket C by the invoices change of 2026-10-21. The other six are noise.
public class InMemoryChangeLogRepository : IChangeLogRepository
{
    private static readonly TimeSpan Beirut = TimeSpan.FromHours(3);

    // Components whose lookup throws, to test a change-log outage: ChangeLog:FailComponents in config,
    // e.g. ChangeLog__FailComponents__0=billing. Empty by default.
    private readonly HashSet<string> _failComponents;

    public InMemoryChangeLogRepository() => _failComponents = [];

    public InMemoryChangeLogRepository(IConfiguration config) =>
        _failComponents = new(config.GetSection("ChangeLog:FailComponents").Get<string[]>() ?? [], StringComparer.OrdinalIgnoreCase);

    private static readonly List<ChangeEntry> Changes =
    [
        // billing
        new("billing", new DateTimeOffset(2026, 10, 19, 9, 15, 0, Beirut), "4.18.0",
            "Proration now rounds up to the nearest cent instead of banker's rounding. Plan-change preview not updated yet, follow-up to come."),
        new("billing", new DateTimeOffset(2026, 10, 12, 14, 30, 0, Beirut), "4.16.2",
            "Log Stripe webhook retries."),

        // invoices
        new("invoices", new DateTimeOffset(2026, 10, 21, 18, 0, 0, Beirut), "4.19.0",
            "Per-tenant invoice numbering (INV-<TENANTCODE>-0001), replaces the global sequence."),
        new("invoices", new DateTimeOffset(2026, 10, 14, 11, 0, 0, Beirut), "4.17.0",
            "Show the tenant's VAT number in the invoice PDF footer."),

        // auth
        new("auth", new DateTimeOffset(2026, 10, 15, 10, 45, 0, Beirut), "4.17.1",
            "Admin sessions now expire after 8h instead of 12h."),
        new("auth", new DateTimeOffset(2026, 10, 9, 16, 20, 0, Beirut), "4.16.0",
            "Optional TOTP 2FA for tenant admins."),

        // tenants
        new("tenants", new DateTimeOffset(2026, 10, 16, 13, 0, 0, Beirut), "4.17.2",
            "Allow hyphens in tenant subdomains."),
        new("tenants", new DateTimeOffset(2026, 10, 8, 9, 0, 0, Beirut), "4.15.3",
            "Tenant deletion is now a soft delete, data purged after 30 days."),
    ];

    public IReadOnlyList<ChangeEntry> GetByComponent(string component)
    {
        if (_failComponents.Contains(component.Trim()))
            throw new InvalidOperationException($"Simulated change-log outage for '{component}'.");

        return Changes.Where(c => string.Equals(c.Component, component.Trim(), StringComparison.OrdinalIgnoreCase))
                      .OrderByDescending(c => c.ChangedAt)
                      .ToList();
    }

    public IReadOnlyList<string> GetComponents() =>
        Changes.Select(c => c.Component).Distinct().Order().ToList();
}
