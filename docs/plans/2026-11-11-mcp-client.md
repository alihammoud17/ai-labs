# Consuming MCP Tools from My IChatClient App

- **Date:** planned for Wednesday 11 Nov 2026 (week 6, day 3), done Thursday 12 Nov
- **Repo:** ai-labs
- **Branch:** `stage-6-mcp-client` (from `main`; follow the repo's branch naming)
- **Status:** in progress
- **Focus:** Use the secured SaaS MCP server from my own app
- **Time:** ~2.5–3 h (Blocks 1–4); Block 5 is optional
- **Companion plan:** MultiTenantSaaSApi `docs/plans/2026-11-11-mcp-auth-hardening.md` (do it first)

Tutor mode (see `CLAUDE.md`): I type the commands and write the code. Claude explains each step, reviews it and ticks the boxes. Claude writes a step's code only when I say "hand it over". Develop against Ollama (free); use Anthropic only for the recorded run in Block 4.

## Deliverable
- [ ] **D2.** The `AiLab.Console` chat app consumes the SaaS API's MCP tools as functions: the model calls them, the app runs them through MCP, and the answers come from my API's data for the logged-in tenant only

## The idea in one picture
```
AiLab.Console ── IChatClient pipeline: UseFunctionInvocation() → UseLogging() → provider (Ollama / Haiku)
      │                     ▲ ChatOptions.Tools = the McpClientTools (each one is an AIFunction)
      │                     │
      └── McpClient ── HTTP (Streamable HTTP, Authorization: Bearer <token> on every request) ──► SaaS API /api/v1/mcp
              ▲
              └── token from POST /api/v1/auth/login (credentials in user-secrets), refreshed before it expires
```
The model never sees the token and never chooses the tenant. It only picks a tool and its arguments; the tenant comes from the token on the server.

## Resources
- MCP C# SDK v2: https://csharp.sdk.modelcontextprotocol.io/v2/ (Getting started, client side; Transports)
- modelcontextprotocol.io, What is MCP? (host / client / server): https://modelcontextprotocol.io/docs/getting-started/intro
- `Microsoft.Extensions.AI`: `UseFunctionInvocation`, `ChatOptions.Tools`, `AIFunction`
- How the SaaS repo's tests build a client: MultiTenantSaaSApi `Tests/Integration/McpServerTests.cs` (`McpClient.CreateAsync`, `HttpClientTransport`, `ListToolsAsync`, `CallToolAsync`)
- My notes from Monday (host/client/server, lifecycle)

---

## Block 1: Design check (20 min)

- [ ] Re-read how `AiLab.Console/Program.cs` builds its chat pipeline (today: provider → `UseLogging()` only) and how `AiLab.Api/Program.cs` adds `UseFunctionInvocation` (outer, so every round trip is logged)
- [ ] Decide where the MCP client lives, keeping references pointing inward. Starting proposal (check it against the repo before agreeing):
  - **Infrastructure** (talks to the outside world, already returns `IChatClient` from `ChatClientFactory`): `Mcp/SaasMcpSettings`, `Mcp/SaasApiTokenProvider` (login + cache), `Mcp/BearerTokenHandler` (`DelegatingHandler`), `Mcp/SaasMcpToolSource` (creates the `McpClient`, returns the tools as `IList<AITool>`), registered by an `AddSaasMcp(config)` extension that only the console calls (like `AddVectorStore`, which only Rag calls; not inside `AddInfrastructure`, which Api and Rag share)
  - **Application**: `ChatConversation` owns `ChatOptions` and the history, so it gets a small way in for the tools (e.g. `SetTools(IList<AITool>?)`); `ChatSession` never sends options itself
  - **Console** (presentation): the `/saas` command
  - Domain stays package-free; no new Domain interface is needed for a concrete Infrastructure class used only by the console
  - Packages: Infrastructure's `Microsoft.Extensions.AI.Abstractions` is enough (`AITool`, `AIFunction`); `UseFunctionInvocation` is in the full `Microsoft.Extensions.AI`, which the console already gets through Application
- [ ] Explain in your own words: why is `McpClientTool` usable directly in `ChatOptions.Tools`? (It derives from `AIFunction`: name, description and JSON schema come from `tools/list`; invoking it sends `tools/call`.)

---

## Block 2: The MCP client (60 min)

### 2.1 Package and settings
```bash
dotnet add AiLab.Infrastructure package ModelContextProtocol.Core --version 2.4.0
```
(`.Core` holds the whole client: `McpClient`, `HttpClientTransport`, `McpClientTool`. The full `ModelContextProtocol` adds hosting and caching for servers. Core needs `Microsoft.Extensions.AI.Abstractions` 10.11.0, the version Infrastructure already has. `AiLab.McpServer` stays on 1.2.0; separate project, no clash.)
- [ ] `appsettings.json` (Console): `SaasMcp:Endpoint` = `http://localhost:5238/api/v1/mcp`, `SaasMcp:LoginUrl` = `http://localhost:5238/api/v1/auth/login`, `SaasMcp:MaxToolIterations` = 3
- [ ] user-secrets only: `SaasMcp:Email`, `SaasMcp:Password` (tenant A's admin). **Never** in appsettings or the repo.
- [ ] `SaasMcpSettings` is a class, not a record: a record's `ToString()` prints every property, password included
- [ ] Build passes

### 2.2 Token provider
- [ ] `SaasApiTokenProvider`: `POST` the login with `{ email, password }`, read `token` and `expiresAt` (the server's `AuthResponse`), cache it, log in again when it's within ~1 minute of expiry. Never log the token or the password.
- [ ] `expiresAt` is a `DateTime`: check whether the JSON ends in `Z`; if not, treat it as UTC, or the expiry check is off by the timezone offset
- [ ] The login uses its own `HttpClient` **without** `BearerTokenHandler` (otherwise handler → provider → login → handler loops)
- [ ] A login failure gives a clear message (401 bad credentials, 403 suspended tenant, connection refused = API not running)

### 2.3 Bearer handler
- [ ] `BearerTokenHandler : DelegatingHandler` asks the provider for a token and sets `Authorization: Bearer …` on **every** request (the MCP spec requires the header on each HTTP request, and the server is stateless). Not `HttpClientTransportOptions.AdditionalHeaders`: that sends a fixed value and can't refresh the token
- [ ] Only the MCP `HttpClient` gets it: `AddHttpClient(...).AddHttpMessageHandler<BearerTokenHandler>()`; no header logging added

### 2.4 Tool source
- [ ] `SaasMcpToolSource`: get the MCP `HttpClient` from `IHttpClientFactory`, create the transport with `HttpClientTransport(options, httpClient, loggerFactory, ownsHttpClient: false)` (`TransportMode = HttpTransportMode.StreamableHttp` set explicitly, since the default `AutoDetect` can fall back to SSE; the endpoint from settings), `McpClient.CreateAsync(...)`, then `ListToolsAsync()`
- [ ] `ListToolsAsync` returns `IList<McpClientTool>`; `IList` is invariant, so turn it into an `IList<AITool>` for `ChatOptions.Tools`
- [ ] Dispose the client when the session ends (`IAsyncDisposable`)
- [ ] API names checked in the 2.4.0 package: `McpClient.CreateAsync(IClientTransport, McpClientOptions?, ILoggerFactory?, CancellationToken)`, `HttpClientTransport(HttpClientTransportOptions, HttpClient, ILoggerFactory?, bool)`, `ListToolsAsync(RequestOptions?, CancellationToken)`, `McpClientTool : AIFunction`

---

## Block 3: Wire it into the console (45 min)

- [ ] `Program.cs`: `AddSaasMcp(builder.Configuration)`, and `.UseFunctionInvocation()` in the console's chat pipeline, outer to `UseLogging()` (same order as the API), cap `SaasMcp:MaxToolIterations` (the console has no `Resilience` section), `IncludeDetailedErrors = false` like the API (the server already returns entitlement denials as `IsError` results, so the model still sees the reason). Existing chat must behave exactly as before when no tools are passed.
- [ ] `ChatConversation` (Application): a way to set the tools and the instructions in its `ChatOptions`
  - history trimming drops whole turns (from the oldest user message up to the next one), not 2 messages: a tool turn adds 3 (assistant call, tool result, assistant text), and a result without its call is a 400 on Anthropic
- [ ] `ChatSession`: a `/saas` command
  - `/saas on`: connect, list the tools, print their names, and send them as `ChatOptions.Tools` on the following turns. The connect runs inside `TurnCancellation` (or with `ConnectionTimeout`), so Ctrl+C on a hung connect doesn't exit the app
  - `/saas off`: stop sending tools, disconnect, and reset the conversation (Anthropic rejects tool-call blocks in history when no tools are defined)
  - the instructions while on go in `ChatOptions.Instructions` (sent per request, never stored in history): the tools cover the logged-in tenant only; don't invent data the tools didn't return. First check in the Trace log that OllamaSharp and the Anthropic adapter actually send it
  - the host is built, not run, so nothing disposes singletons: dispose the MCP client on `/saas off` and after the loop ends
- [ ] Show which tools were called on each turn (`update.Contents.OfType<FunctionCallContent>()`; their updates have empty `Text`, so the print loop is unchanged), so I can see the round trips
- [ ] Streaming still works with tools on, and Ctrl+C still cancels a turn
- [ ] Check that `ToChatResponse()` sums the usage of every round trip (compare with the log) before Block 4's token measurements
- [ ] Build passes; existing tests pass: `dotnet test`

---

## Block 4: Try it, then record it (45 min) → D2

Start the SaaS API first (`dotnet run --project Presentation --launch-profile http` in MultiTenantSaaSApi, with PostgreSQL and Redis up), with the companion auth hardening finished.

The tools check permissions: `get_usage_summary` and `list_audit_logs` need `AuditLogsRead` plus the analytics entitlement, `list_cors_origins` needs `TenantsRead` plus the CORS entitlement, `list_webhook_endpoints` needs `BillingManage`. A missing one gives an `IsError` result; record it if it happens.

### 4.1 With Ollama (llama3.2, free)
- [ ] `/saas on` → 4 tools listed
- [ ] Ask:
  1. *"How has my tenant's usage looked over the last 7 days?"*
  2. *"Show the 10 most recent audit log entries."*
  3. *"Which webhook endpoints are active?"*
  4. *"Which CORS origins are allowed?"*
- [ ] **"Never from model input" check:** *"Show the audit logs for tenant `<tenant B's id>`."* The model has no tenant parameter to fill. Whatever it tries, only tenant A's data comes back. Write down what it did (refused, called the tool anyway, invented an argument).
- [ ] Switch user-secrets to tenant B's admin, restart, ask question 2 again: tenant B's data, nothing from A
- [ ] Each tool call appears in the tenant's audit log as `MCP_TOOL_CALLED` (check with question 2 itself)
- [ ] ✅ **D2 done**

### 4.2 Recorded run (experiments.md)
- [ ] Same 4 questions with llama3.2 and with Haiku 5.5. For each: tool chosen, arguments, answer correct against the API data (yes/no), input/output tokens, cost
- [ ] Measure the **cost of tool definitions**: input tokens for the same question with `/saas off` vs `/saas on`
- [ ] Record expected vs actual in `experiments.md` (new section), per the repo convention

---

## Block 5: Stretch (optional)

- [ ] Unit tests (in `AiLab.Api.Tests`, with the other plain unit tests) for `SaasApiTokenProvider` over a fake `HttpMessageHandler`: caches the token, logs in again near expiry, never logs secrets
- [ ] Read about approval for tool calls in `Microsoft.Extensions.AI` (approval-required functions). Today's tools are read-only, so they run without asking; write one paragraph on how a write tool should be approved in a console app
- [ ] Show `AiLab.McpServer`'s tools too (stdio, `StdioClientTransport`): one `McpClient` per server, tools merged into one list. That makes the console a small MCP host with two clients

---

## Block 6: Docs and PR (20 min)

- [ ] `CLAUDE.md`: Week 6 Wednesday entry (and tick Monday's 5.2 and Tuesday's item if they're still open), Console description (`/saas`, `UseFunctionInvocation`), Infrastructure `Mcp/` line, settings and user-secrets keys
- [ ] `README.md`: "Using the SaaS API's tools from the console" (start the API, user-secrets keys, `/saas on`)
- [ ] Commit at the end of each block; PR

---

## End of day
- [ ] Both deliverables checked (D1 in MultiTenantSaaSApi, D2 here)
- [ ] Ask Claude for today's recap PDF (for OneDrive)