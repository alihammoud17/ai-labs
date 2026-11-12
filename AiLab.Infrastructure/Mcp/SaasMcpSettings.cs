namespace AiLab.Infrastructure.Mcp;

// The SaaS API's MCP server and its login (SaasMcp:* in the console's appsettings.json).
// Email and Password come only from user-secrets, never from appsettings or the repo.
// A class, not a record: a record's generated ToString() prints every property, so a log line
// or the debugger's summary would show the password.
public class SaasMcpSettings
{
    public const string SectionName = "SaasMcp";

    // Streamable HTTP endpoint, e.g. http://localhost:5238/api/v1/mcp
    public string Endpoint { get; set; } = "";

    // POST { email, password } → { token, expiresAt, ... }
    public string LoginUrl { get; set; } = "";

    // POST with the API token as Bearer, no body → { token, expiresAt }: the MCP-audience token (15 min),
    // the only kind the MCP route accepts
    public string McpTokenUrl { get; set; } = "";

    // Tenant A's admin, from user-secrets
    public string Email { get; set; } = "";

    // From user-secrets; never logged
    public string Password { get; set; } = "";

    // Model calls per turn, the first included, so a confused model can't loop (like the API's MaxToolIterations)
    public int MaxToolIterations { get; set; } = 3;
}
