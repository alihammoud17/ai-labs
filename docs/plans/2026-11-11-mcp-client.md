# Consuming MCP Tools from My IChatClient App

- **Date:** planned for Wednesday 11 Nov 2026 (week 6, day 3), done Thursday 12 Nov
- **Repo:** ai-labs
- **Branch:** `stage-6-mcp-client` (from `main`; follow the repo's branch naming)
- **Status:** in progress
- **Focus:** Use the secured SaaS MCP server from my own app
- **Time:** ~2.5–3 h (Blocks 1–4); Block 5 is optional
- **Companion plan:** MultiTenantSaaSApi `docs/plans/2026-11-11-mcp-auth-hardening.md` (do it first); this plan targets its branch `feat/mcp-audience-bound-tokens` (MCP-audience tokens from `POST /api/v1/auth/mcp-token`)

Tutor mode (see `CLAUDE.md`): I type the commands and write the code. Claude explains each step, reviews it and ticks the boxes. Claude writes a step's code only when I say "hand it over". Develop against Ollama (free); use Anthropic only for the recorded run in Block 4.

## Deliverable
- [ ] **D2.** The `AiLab.Console` chat app consumes the SaaS API's MCP tools as functions: the model calls them, the app runs them through MCP, and the answers come from my API's data for the logged-in tenant only

## The idea in one picture
```
AiLab.Console ── IChatClient pipeline: UseFunctionInvocation() → UseLogging() → provider (Ollama / Haiku)
      │                     ▲ ChatOptions.Tools = the McpClientTools (each one is an AIFunction)
      │                     │
      └── McpClient ── HTTP (Streamable HTTP, Authorization: Bearer <MCP token> on every request) ──► SaaS API /api/v1/mcp
              ▲
              └── MCP token (audience MultiTenantSaasApi/mcp, 15 min) from POST /api/v1/auth/mcp-token,
                  sent with the API token (60 min) from POST /api/v1/auth/login (credentials in user-secrets);
                  each refreshed before it expires
```
The MCP route accepts only MCP-audience tokens (`McpBearer` scheme), so a login token gets 401 there, and the token that reaches the MCP server can't be replayed against the rest of the API. The model never sees either token and never chooses the tenant. It only picks a tool and its arguments; the tenant comes from the token on the server.

## Resources
- MCP C# SDK v2: https://csharp.sdk.modelcontextprotocol.io/v2/ (Getting started, client side; Transports)
- modelcontextprotocol.io, What is MCP? (host / client / server): https://modelcontextprotocol.io/docs/getting-started/intro
- `Microsoft.Extensions.AI`: `UseFunctionInvocation`, `ChatOptions.Tools`, `AIFunction`
- How the SaaS repo's tests build a client: MultiTenantSaaSApi `Tests/Integration/McpServerTests.cs` (`McpClient.CreateAsync`, `HttpClientTransport`, `ListToolsAsync`, `CallToolAsync`)
- My notes from Monday (host/client/server, lifecycle)

---

## Block 1: Design check (20 min)

- [x] Re-read how `AiLab.Console/Program.cs` builds its chat pipeline (today: provider → `UseLogging()` only) and how `AiLab.Api/Program.cs` adds `UseFunctionInvocation` (outer, so every round trip is logged)
  - **Done:** console = keyed provider → `.UseLogging()` only, so a tool call from the model would never run. API = `.UseFunctionInvocation(...)` → `.UseLogging()` → provider: the first `Use...` is outermost, so logging sits inside the tool loop and records every model call, tool steps included; cap `MaxToolIterations`, `IncludeDetailedErrors = false`. The console copies that order, with the cap from `SaasMcp:MaxToolIterations` (no `Resilience` section there) and tools sent only while `/saas` is on
- [x] Decide where the MCP client lives, keeping references pointing inward. Starting proposal (check it against the repo before agreeing):
  - **Infrastructure** (talks to the outside world, already returns `IChatClient` from `ChatClientFactory`): `Mcp/SaasMcpSettings`, `Mcp/SaasApiTokenProvider` (login + cache), `Mcp/BearerTokenHandler` (`DelegatingHandler`), `Mcp/SaasMcpToolSource` (creates the `McpClient`, returns the tools as `IList<AITool>`), registered by an `AddSaasMcp(config)` extension that only the console calls (like `AddVectorStore`, which only Rag calls; not inside `AddInfrastructure`, which Api and Rag share)
  - **Application**: `ChatConversation` owns `ChatOptions` and the history, so it gets a small way in for the tools (e.g. `SetTools(IList<AITool>?)`); `ChatSession` never sends options itself
  - **Console** (presentation): the `/saas` command
  - Domain stays package-free; no new Domain interface is needed for a concrete Infrastructure class used only by the console
  - Packages: Infrastructure's `Microsoft.Extensions.AI.Abstractions` is enough (`AITool`, `AIFunction`); `UseFunctionInvocation` is in the full `Microsoft.Extensions.AI`, which the console already gets through Application
  - **Done:** the starting proposal was checked against the repo (both hosts, `ChatSession`, `ChatConversation`, Infrastructure's DI and packages, the SDK 2.4.0 package, the SaaS server's tools and login contract) and the differences found were approved and folded into this plan: `.Core` package, `AddSaasMcp` only in the console, tools through `ChatConversation` (the biggest change: it owns `ChatOptions`, not `ChatSession`), whole-turn trimming, reset on `/saas off`, `ChatOptions.Instructions`, disposal, two `HttpClient`s. References still point inward: Console → Infrastructure / Application → Domain
- [x] Explain in your own words: why is `McpClientTool` usable directly in `ChatOptions.Tools`? (It derives from `AIFunction`: name, description and JSON schema come from `tools/list`; invoking it sends `tools/call`.)
  - **Done (my answer):** it derives from `AIFunction`, so `FunctionInvokingChatClient` calls it like any function. Its name, description and schema come from `tools/list`, sent once at connect (on the server they come from `TenantReadTools`' attributes); invoking it sends `tools/call` over HTTP, through `BearerTokenHandler`, which adds the token

---

## Block 2: The MCP client (60 min)

### 2.1 Package and settings
```bash
dotnet add AiLab.Infrastructure package ModelContextProtocol.Core --version 2.4.0
```
(`.Core` holds the whole client: `McpClient`, `HttpClientTransport`, `McpClientTool`. The full `ModelContextProtocol` adds hosting and caching for servers. Core needs `Microsoft.Extensions.AI.Abstractions` 10.11.0, the version Infrastructure already has. `AiLab.McpServer` stays on 1.2.0; separate project, no clash.)
- [x] `appsettings.json` (Console): `SaasMcp:Endpoint` = `http://localhost:5238/api/v1/mcp`, `SaasMcp:LoginUrl` = `http://localhost:5238/api/v1/auth/login`, `SaasMcp:MaxToolIterations` = 3
  - **Done (handed over):** the package line added to `AiLab.Infrastructure.csproj` (restored with no version conflict); a `SaasMcp` section in `AiLab.Console/appsettings.json` with the three values, no credentials
- [x] user-secrets only: `SaasMcp:Email`, `SaasMcp:Password` (tenant A's admin). **Never** in appsettings or the repo.
  - **Done (me):** set with `dotnet user-secrets set ... --project AiLab.Console`, the password read with `read -s` so it stays out of the shell history; checked by key name only (`user-secrets list | cut -d= -f1`), values never printed
- [x] `SaasMcpSettings` is a class, not a record: a record's `ToString()` prints every property, password included
  - **Done (handed over):** `AiLab.Infrastructure/Mcp/SaasMcpSettings.cs`, plain get/set properties (`Endpoint`, `LoginUrl`, `McpTokenUrl`, `Email`, `Password`, `MaxToolIterations` default 3), `SectionName = "SaasMcp"` for `AddSaasMcp` to bind; comments say which values come from user-secrets
- [x] Build passes
  - **Done:** `dotnet build`, 0 warnings, 0 errors
- [x] `appsettings.json` (Console): `SaasMcp:McpTokenUrl` = `http://localhost:5238/api/v1/auth/mcp-token`, and the matching property on `SaasMcpSettings` (added after the SaaS API moved to MCP-audience tokens)
  - **Done (handed over):** the key in `appsettings.json` and the property with a comment (POST with the API token as Bearer, no body → `{ token, expiresAt }`); build 0 / 0
- Dev credentials: the SaaS API's password reset and email verification only log that a token was queued (the token is never delivered), so tenants A and B were registered with `POST /api/v1/auth/register` (passwords I chose) and verified with an `UPDATE` of `EmailVerifiedAt` in the local dev database. New tenants are on `plan-free`, which has the analytics and CORS entitlements

### 2.2 Token provider (two steps)
- [x] `SaasApiTokenProvider` gets the **MCP** token in two calls:
  1. `POST` `LoginUrl` with `{ email, password }` → `token` and `expiresAt` (the server's `AuthResponse`): the API token, 60 min
  2. `POST` `McpTokenUrl` with `Authorization: Bearer <API token>` and no body → `{ token, expiresAt }`: the MCP token, 15 min
  - **Done (handed over):** `AiLab.Infrastructure/Mcp/SaasApiTokenProvider.cs`, one public method `GetMcpTokenAsync(CancellationToken)`. Login with `PostAsJsonAsync(LoginUrl, new { email, password })`; the exchange with an `HttpRequestMessage` carrying `AuthenticationHeaderValue("Bearer", apiToken)`; both replies read with `ReadFromJsonAsync` into one private `TokenReply` (`Token`, `ExpiresAt`; login's extra fields ignored). Dependencies: `IHttpClientFactory`, `IOptions<SaasMcpSettings>`, `TimeProvider`, `ILogger`
- [x] Caches both. Within ~1 minute of the MCP token's expiry it asks for a new one with the cached API token; it logs in again only when the API token is near expiry too (or `mcp-token` returns 401). Never log a token or the password.
  - **Done (handed over):** two cached `(Token, ExpiresAt)` holders; fresh = `TimeProvider.GetUtcNow() < expiresAt − 1 min` (`TimeProvider`, so Block 5's tests can move the clock). Checked once without the lock, then again inside a `SemaphoreSlim(1, 1)`, so two callers at once don't both refresh. A 401 on the exchange drops the API token, logs in once more and retries; a second 401 throws instead of looping. Logs only "valid until {time}"; the token holders are classes, not records, so `ToString()` can't print a token
- [x] Both `expiresAt` values are `DateTime`s: check whether the JSON ends in `Z`; if not, treat them as UTC, or the expiry check is off by the timezone offset
  - **Done (handed over):** the server builds it from `DateTime.UtcNow` (`JwtService`), so it comes with a `Z` and parses as UTC; a value with no zone (`Kind == Unspecified`) is set to UTC, never read as local time
- [x] Both calls use their own `HttpClient` **without** `BearerTokenHandler` (otherwise handler → provider → login → handler loops); the provider sets the API token on the `mcp-token` request itself
  - **Done (handed over):** the named client `"saas-auth"` (`SaasApiTokenProvider.HttpClientName`), registered without a handler in `AddSaasMcp` (2.4). Bug found in review and fixed: the exchange lambda wasn't `async`, so `using var request` disposed the request while it was still being sent
- [x] Failures give a clear message: login 401 bad credentials, 403 suspended tenant (also possible on `mcp-token`), connection refused = API not running
  - **Done (handed over):** status checked before the body is read; each failure is an `InvalidOperationException` naming the step: login 401 wrong `SaasMcp:Email` / `Password`, 403 tenant suspended (login and exchange), 429 rate limited, other codes with the URL, an `HttpRequestException` → "could not reach the SaaS API (…). Is it running on the URL in SaasMcp:*?", missing user-secrets → names both keys. No token, password or body in any message. Build 0 / 0; not run against the live API yet (first run: `/saas on` in Block 3)

### 2.3 Bearer handler
- [x] `BearerTokenHandler : DelegatingHandler` asks the provider for the **MCP** token and sets `Authorization: Bearer …` on **every** request (the MCP spec requires the header on each HTTP request, and the server is stateless). Not `HttpClientTransportOptions.AdditionalHeaders`: that sends a fixed value and can't refresh the token
  - **Done (handed over):** `AiLab.Infrastructure/Mcp/BearerTokenHandler.cs`, a primary-constructor class over `SaasApiTokenProvider`. `SendAsync` awaits `GetMcpTokenAsync(cancellationToken)`, sets `request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)`, then `base.SendAsync`. No caching of its own: the provider decides when to refresh, so a chat longer than the MCP token's 15 minutes keeps working. The cancellation token flows through, so Ctrl+C also stops a token refresh
- [x] Only the MCP `HttpClient` gets it: `AddHttpClient(...).AddHttpMessageHandler<BearerTokenHandler>()`; no header logging added
  - **Done (handed over):** `AiLab.Infrastructure/Mcp/SaasMcpServices.cs`, `AddSaasMcp(IServiceCollection, IConfiguration)`, to be called only by the console (Block 3), like `AddVectorStore` only by Rag. It binds `SaasMcpSettings` from `SaasMcp:*` (`Configure<T>`, read through `IOptions<T>`), registers `SaasApiTokenProvider` as a singleton (one token cache for the app), `"saas-auth"` (login and exchange, no handler) and `"saas-mcp"` (`SaasMcpServices.McpHttpClientName`, with `BearerTokenHandler`, registered transient as `AddHttpMessageHandler<T>` expects). Both clients use `RedactLoggedHeaders(_ => true)`: `IHttpClientFactory` logs headers at Trace, so this keeps the `Authorization` value out of the log even if logging is turned up. Build 0 / 0

### 2.4 Tool source
- [x] `SaasMcpToolSource`: get the MCP `HttpClient` from `IHttpClientFactory`, create the transport with `HttpClientTransport(options, httpClient, loggerFactory, ownsHttpClient: false)` (`TransportMode = HttpTransportMode.StreamableHttp` set explicitly, since the default `AutoDetect` can fall back to SSE; the endpoint from settings), `McpClient.CreateAsync(...)`, then `ListToolsAsync()`
  - **Done (handed over):** `AiLab.Infrastructure/Mcp/SaasMcpToolSource.cs`, a sealed singleton (registered in `AddSaasMcp`) over `IHttpClientFactory`, `IOptions<SaasMcpSettings>`, `ILoggerFactory`. `ConnectAsync(CancellationToken)` checks `SaasMcp:Endpoint` is an absolute URL (clear message if not), takes the `"saas-mcp"` client (so `BearerTokenHandler` is on it), builds the transport exactly as above with `Name = "saas"`, then `McpClient.CreateAsync(transport, new McpClientOptions { ClientInfo = new Implementation { Name = "AiLab.Console", Version = "1.0.0" } }, loggerFactory, ct)`. `initialize` is the first request through the handler, so a bad login or a stopped API fails right there. Already connected → returns the same tools, no second connection. Logs the endpoint and tool count, never a token
- [x] `ListToolsAsync` returns `IList<McpClientTool>`; `IList` is invariant, so turn it into an `IList<AITool>` for `ChatOptions.Tools`
  - **Done (handed over):** `_tools = [.. tools]`, a collection expression into the `IList<AITool>` field (each element is still the `McpClientTool`). If listing fails after the connection opened, the client is disposed before the exception goes on, so nothing stays open
- [x] Dispose the client when the session ends (`IAsyncDisposable`)
  - **Done (handed over):** `DisconnectAsync()` clears the client and tools, then disposes the `McpClient` (safe to call twice); `DisposeAsync()` calls it. `IsConnected` tells the console whether `/saas` is on. The tools only work while connected, so the console must stop sending them on disconnect (Block 3)
- [x] API names checked in the 2.4.0 package: `McpClient.CreateAsync(IClientTransport, McpClientOptions?, ILoggerFactory?, CancellationToken)`, `HttpClientTransport(HttpClientTransportOptions, HttpClient, ILoggerFactory?, bool)`, `ListToolsAsync(RequestOptions?, CancellationToken)`, `McpClientTool : AIFunction`
  - **Done:** checked in the package's XML docs (`~/.nuget/packages/modelcontextprotocol.core/2.4.0`), plus `McpClientOptions.ClientInfo` and `Implementation.Name` / `Version`; the code compiles against them (build 0 / 0). `dotnet test`: 336 passed, 0 failed, 42 skipped (the opt-in Postgres and eval tests). Not run against the live API yet: first run is `/saas on` in Block 3

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

Start the SaaS API first (`dotnet run --project Presentation --launch-profile http` in MultiTenantSaaSApi on `feat/mcp-audience-bound-tokens`, with PostgreSQL and Redis up), with the companion auth hardening finished. The new tenants start almost empty: add a webhook endpoint and a CORS origin to each, so the answers and the A-vs-B check have data.

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