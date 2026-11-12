# Consuming MCP Tools from My IChatClient App

- **Date:** planned for Wednesday 11 Nov 2026 (week 6, day 3), done Thursday 12 Nov
- **Repo:** ai-labs
- **Branch:** `stage-6-mcp-client` (from `main`; follow the repo's branch naming)
- **Status:** done (Blocks 1–4 and 6; Block 5, optional, skipped)
- **Focus:** Use the secured SaaS MCP server from my own app
- **Time:** ~2.5–3 h (Blocks 1–4); Block 5 is optional
- **Companion plan:** MultiTenantSaaSApi `docs/plans/2026-11-11-mcp-auth-hardening.md` (do it first); this plan targets its branch `feat/mcp-audience-bound-tokens` (MCP-audience tokens from `POST /api/v1/auth/mcp-token`)

Tutor mode (see `CLAUDE.md`): I type the commands and write the code. Claude explains each step, reviews it and ticks the boxes. Claude writes a step's code only when I say "hand it over". Develop against Ollama (free); use Anthropic only for the recorded run in Block 4.

## Deliverable
- [x] **D2.** The `AiLab.Console` chat app consumes the SaaS API's MCP tools as functions: the model calls them, the app runs them through MCP, and the answers come from my API's data for the logged-in tenant only

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

- [x] `Program.cs`: `AddSaasMcp(builder.Configuration)`, and `.UseFunctionInvocation()` in the console's chat pipeline, outer to `UseLogging()` (same order as the API), cap `SaasMcp:MaxToolIterations` (the console has no `Resilience` section), `IncludeDetailedErrors = false` like the API (the server already returns entitlement denials as `IsError` results, so the model still sees the reason). Existing chat must behave exactly as before when no tools are passed.
  - **Done (me, reviewed):** `AddSaasMcp(builder.Configuration)` after `AddInfrastructure()` with its own comment (connects only on `/saas on`, so the console runs without the SaaS API); `.UseFunctionInvocation(configure: f => { MaximumIterationsPerRequest = GetValue<int>("SaasMcp:MaxToolIterations", 3); IncludeDetailedErrors = false; })` before `.UseLogging()`. Review fixes: two wrong comments (the MCP client is not for "usage reporting and billing"; detailed errors are about what the *model* sees, not the user), layout like the API. The order comment was added by Claude (asked): first `Use...` is outermost, logging inside the tool loop records every round trip, pass-through with `/saas` off, the cap, no detailed errors. Checked: build 0 / 0; two piped turns on llama3.2 answered as before (the second remembered the first), and the log shows one `GetStreamingResponseAsync invoked` / `completed` pair per turn, so nothing changed with no tools. (First try failed with `Connection refused (localhost:11434)`: Ollama wasn't running.)
- [x] `ChatConversation` (Application): a way to set the tools and the instructions in its `ChatOptions`
  - history trimming drops whole turns (from the oldest user message up to the next one), not 2 messages: a tool turn adds 3 (assistant call, tool result, assistant text), and a result without its call is a 400 on Anthropic
  - **Part A done (handed over):** `ChatConversation.SetTools(IList<AITool>? tools, string? instructions)` sets `_options.Tools` and `_options.Instructions`; `_options` is sent on every turn, so it applies from the next one. `null` for both is `/saas off`: back to today's options (true while `ChatExperimentSettings.CreateOptions()` sets neither). Instructions go per request, never into `_history`, so `/saas off` and `/reset` leave nothing behind. Build 0 / 0
  - **Part B done (handed over):** the trimming loop in `CompleteTurn` now drops whole turns. While the non-system count is over `MaxHistoryMessages`: `oldest` = first non-system message (always a user message), `next` = `FindIndex(oldest + 1, Role == User)`, `RemoveRange(oldest, next − oldest)`. No next user message means only the newest turn is left, so the loop stops instead of dropping the turn just added. Example at the limit: [user, assistant] then [user, assistant call, tool result, assistant text] → the first removal takes 2 messages, a tool turn would take all 4 in one go, never half. System messages are still never counted or trimmed. Build 0 / 0; `dotnet test` 336 passed, 0 failed, 42 skipped (no test covers `ChatConversation` yet)
- [x] `ChatSession`: a `/saas` command
  - `/saas on`: connect, list the tools, print their names, and send them as `ChatOptions.Tools` on the following turns. The connect runs inside `TurnCancellation` (or with `ConnectionTimeout`), so Ctrl+C on a hung connect doesn't exit the app
  - `/saas off`: stop sending tools, disconnect, and reset the conversation (Anthropic rejects tool-call blocks in history when no tools are defined)
  - the instructions while on go in `ChatOptions.Instructions` (sent per request, never stored in history): the tools cover the logged-in tenant only; don't invent data the tools didn't return. First check in the Trace log that OllamaSharp and the Anthropic adapter actually send it
  - the host is built, not run, so nothing disposes singletons: dispose the MCP client on `/saas off` and after the loop ends
  - **Done (handed over):** `AiLab.Console/Chat/ChatSession.cs` takes `SaasMcpToolSource saasTools`. `/saas` is parsed like `/similarity` and goes to `SaasAsync(argument)`. **on**: "already on" if connected; otherwise `ConnectAsync` inside `cancellation.Begin()` / `End()` (Ctrl+C → `[saas connect cancelled]`), then `conversation.SetTools(tools, SaasInstructions)` and `[saas on: 4 tools: …]`; any failure prints `[saas error: <message>]` (the provider's messages carry no secret). **off**: "already off" if not connected; otherwise `SetTools(null, null)`, `DisconnectAsync()`, `conversation.Reset()`, `[saas off, conversation reset]`. Anything else prints usage and the current state. `/saas on|off` added to the welcome line
  - **Done:** `SaasInstructions` is a `const` with two sentences (logged-in tenant only, no other tenant; answer only from what the tools return, say so on nothing or an error, never invent data). Checked: the Trace log shows it in `ChatOptions` on both round trips, and both `OllamaSharp.dll` 5.5.0 and `Anthropic.dll` 12.58.0 reference `ChatOptions.Instructions` (`get_Instructions`), so both adapters read it. Whether Haiku follows it shows in Block 4
  - **Done:** `await saasTools.DisposeAsync()` after the `while` loop (both `/exit` and end of input reach it)
- [x] Show which tools were called on each turn (`update.Contents.OfType<FunctionCallContent>()`; their updates have empty `Text`, so the print loop is unchanged), so I can see the round trips
  - **Done (handed over):** in `RunTurnAsync`'s loop, each `FunctionCallContent` prints `[tool: <name> <arguments as JSON>]` on its own line, and a `FunctionResultContent` with an `Exception` prints `[tool failed: <call id>]`. First live run: `[tool: list_cors_origins {}]`
- [x] Streaming still works with tools on, and Ctrl+C still cancels a turn
  - Streaming checked on the first live run (llama3.2, piped input, SaaS API on `feat/mcp-audience-bound-tokens`): `/saas on` → 4 tools (`get_usage_summary`, `list_audit_logs`, `list_cors_origins`, `list_webhook_endpoints`), "Which CORS origins are allowed?" → `list_cors_origins {}` → "The allowed CORS origins are: []" (tenant A has none yet), `/saas off` → reset. Login, token exchange, `initialize`, `tools/list` and `tools/call` all worked first time. The log has no JWT, no "Bearer", no "password"
  - **Bug found by my interactive Ctrl+C test (older than this branch):** Ctrl+C stopped the reply ("…Note") but printed a usage line instead of `[reply cancelled, turn discarded]`, so the half answer stayed in the history. Cause: OllamaSharp ends the stream quietly when the token is cancelled instead of throwing `OperationCanceledException`, so `ChatSession`'s cancel `catch` never ran. Same on `main` (checked by building it in a temporary worktree and sending the console SIGINT): `Tokens: unknown`, turn kept, the next turn sent 174–212 input tokens instead of ~30. So no regression from today, but "Ctrl+C stops a reply" had never really worked on Ollama. (Testing note: a command started with `&` in a non-interactive shell ignores SIGINT, and .NET keeps that; run the console in the foreground and send the signal from a background job.)
  - **Fix (handed over, after a regression review):** `cancellationToken.ThrowIfCancellationRequested();` right after the `await foreach` in `RunTurnAsync`, before the `Console.WriteLine()` (the cancel message brings its own new line). Review: a normal turn is unchanged (token never cancelled); a provider that throws never reaches the line; `/extract`, `/eval`, `/similarity` have their own code; each turn gets a new token. Changed on purpose: a cancelled Ollama turn is discarded, and Ctrl+C before any text says "reply cancelled" instead of "empty reply". Known small gaps: a Ctrl+C in the microseconds between the last chunk and `End()` discards a fully printed answer; a Haiku turn cancelled after its tool-call round trip leaves that round trip out of the session cost (as before)
  - **Checks after the fix** (SIGINT from a background job, console in the foreground): **A** `/saas` off, two normal turns: unchanged (31 → 58 input tokens, the second remembers the first). **B** `/saas` off, Ctrl+C mid-story: `[reply cancelled, turn discarded]`, the follow-up used `in=28` (was 174–212). **C** `/saas` on: a complete `list_cors_origins` turn, then `list_audit_logs` cancelled during the answer (`[reply cancelled, turn discarded]`), then `list_webhook_endpoints`; the Trace log of turn 3's request holds turn 1 complete (user → call → result → text) and turn 3's question, nothing from turn 2, so `FunctionInvokingChatClient` doesn't write into our history and `DiscardTurn()` stays correct. Build 0 / 0; `dotnet test` 336 passed, 0 failed, 42 skipped
  - Watch in Block 4: the stored assistant tool-call message from Ollama carries an empty text part (`"text": ""`); Anthropic rejects empty text blocks, so check the first Haiku tool turn (Haiku's own history starts fresh after a restart, so it may never see Ollama's messages)
- [x] Check that `ToChatResponse()` sums the usage of every round trip (compare with the log) before Block 4's token measurements
  - **Done:** the turn printed `in=936, out=22`; the Trace log's two calls were 769 + 167 input and 14 + 8 output, so it sums. Note for Block 4: Ollama's second call counted only 167 input tokens because it reuses its cache for the unchanged prompt prefix, so Ollama understates input tokens on later round trips; measure the tool definitions' cost on the **first** call of a turn (or on Haiku, whose usage counts every input token)
- [x] Build passes; existing tests pass: `dotnet test`
  - **Done:** build 0 warnings / 0 errors; `dotnet test` 336 passed, 0 failed, 42 skipped (the opt-in Postgres and eval tests), same as before Block 3

---

## Block 4: Try it, then record it (45 min) → D2

Start the SaaS API first (`dotnet run --project Presentation --launch-profile http` in MultiTenantSaaSApi on `feat/mcp-audience-bound-tokens`, with PostgreSQL and Redis up), with the companion auth hardening finished. The new tenants start almost empty: add a webhook endpoint and a CORS origin to each, so the answers and the A-vs-B check have data.

The tools check permissions: `get_usage_summary` and `list_audit_logs` need `AuditLogsRead` plus the analytics entitlement, `list_cors_origins` needs `TenantsRead` plus the CORS entitlement, `list_webhook_endpoints` needs `BillingManage`. A missing one gives an `IsError` result; record it if it happens.

### 4.1 With Ollama (llama3.2, free)
- [x] `/saas on` → 4 tools listed
- [x] Ask:
  1. *"How has my tenant's usage looked over the last 7 days?"*
  2. *"Show the 10 most recent audit log entries."*
  3. *"Which webhook endpoints are active?"*
  4. *"Which CORS origins are allowed?"*
  - **Done (me):** questions 1, 3 and 4 work as tenant A (`get_usage_summary`, `list_cors_origins`, `list_webhook_endpoints`; the server logged `IsError = False` for each). Question 2 works as tenant B: `[tool: list_audit_logs {"page":1,"pageSize":10,"toUtc":null,"action":null,"fromUtc":null}]`, and the answer listed 10 entries copied correctly from the tool result, newest first, all with B's `tenantId`. As tenant A it failed twice, and the cause wasn't the tenant: the SaaS API's log shows `list_audit_logs` threw `JsonException: The JSON value could not be converted to System.Nullable<DateTime>`. llama3.2 filled the optional filters with empty strings instead of leaving them out or sending `null`: `[tool: list_audit_logs {"action":"","fromUtc":"","page":1,"pageSize":10,"toUtc":""}]` (an earlier failure's byte position fits the string `"null"`). `""` can't become a `DateTime?`, so the call failed before any query ran, and the MCP SDK sent back only a generic error, so the model answered *"the tool was unable to retrieve…"* and couldn't correct its argument. Login, token exchange, RBAC and the `MCP_TOOL_CALLED` audit entry all passed. Worked around with *"Call list_audit_logs with only page 1 and pageSize 10, and leave out every other argument"* → `{"page":1,"pageSize":10}`, server `IsError = False`. The first answer only said some entries had `{"isError": true}` "indicating that the tool call returned an error" (misleading: those are the earlier failed calls, so **failed tool calls are audited too**). Asked for action, tenantId and timestamp, it called the tool again and listed 10 entries, all with tenant A's `tenantId` (`18464731-…`), none of B's; three timestamps match the console log's tool calls to the millisecond (19:10:16.99 `get_usage_summary`, 19:10:51.45 `list_cors_origins`, 19:11:02.83 the failed `list_audit_logs`). One other attempt looped on *"A list of users who have logged in with their email address and role"* until Ctrl+C (the turn was discarded)
- [x] **"Never from model input" check:** *"Show the audit logs for tenant `<tenant B's id>`."* The model has no tenant parameter to fill. Whatever it tries, only tenant A's data comes back. Write down what it did (refused, called the tool anyway, invented an argument).
  - **Done (me), roles swapped:** logged in as tenant B, asked *"Show the audit logs for 18464731-2091-465a-a6b0-f7de8787066a"* (tenant A's ID, confirmed by A's own audit log). The model didn't refuse: it called the tool and **invented an argument the tool doesn't have**: `[tool: list_audit_logs {..., "userId":"18464731-2091-465a-a6b0-f7de8787066a"}]`. The server ignored it and returned only the caller's data: every entry had B's `tenantId` (`29e1814f-…`) and B's admin user (`66eae84f-…`), and the first entry was this very call's `MCP_TOOL_CALLED`. **Isolation held; the answer's wording didn't:** the model titled B's own entries *"the 10 most recent audit log entries for the user with ID 18464731-…"*, so a reader would think they belong to the ID they asked about. The tenant comes from the token, never from the model, but the model can still mislabel what came back. (An earlier attempt as B failed on the bad-date error above, so nothing came back from either tenant)
- [x] Switch user-secrets to tenant B's admin, restart, ask question 2 again: tenant B's data, nothing from A
  - **Done (me), order reversed:** I started as tenant B. Question 2 returned 10 entries, all with tenant B's `tenantId` (`29e1814f-…`); nothing from A
- [x] Each tool call appears in the tenant's audit log as `MCP_TOOL_CALLED` (check with question 2 itself)
  - **Done (me):** tenant B's question 2 listed its own earlier calls as `MCP_TOOL_CALLED` (`entityType` `McpTool`, `entityId` the tool name, `changes` `{"isError":false}`), next to `USER_LOGGED_IN` and `MCP_TOKEN_ISSUED` for each console start
- **Findings for 4.2:**
  - Small models fill optional arguments with junk, and a tool that fails without saying why leaves the model unable to recover. Possible fix in the SaaS repo: describe `fromUtc` / `toUtc` as "ISO 8601 UTC, or omit", and return an unreadable date as an `McpException` with that reason (like an entitlement denial), so the model can correct itself
  - llama3.2 invents parameters (`userId`) and then words the answer as if the filter had applied. Check whether Haiku does the same; a line in `SaasInstructions` ("the tools return only your own tenant's data; say so when asked about another tenant or user") could fix the wording
  - One reply looped on a single line until Ctrl+C. A lower `MaxOutputTokens` while `/saas` is on would cap that
- [x] ✅ **D2 done**
  - **Done (me):** the console calls the SaaS API's MCP tools through `IChatClient` on llama3.2: all 4 questions answered from tool results, the tenant comes from the token only, and every call is audited, failed ones included. Question 2 works for both tenants, each seeing only its own entries; as tenant A it needed the prompt to name the arguments, because of llama3.2's empty-string filters (findings above)

### 4.2 Recorded run (experiments.md)
- [x] Same 4 questions with llama3.2 and with Haiku 5.5. For each: tool chosen, arguments, answer correct against the API data (yes/no), input/output tokens, cost
  - **Done (handed over):** both runs as tenant A, original wording, `/reset` before each question, answers checked against the tool results in the Verbose log. Both models: right tool 4 / 4, fully correct 3 / 4. llama3.2 copied two `ExpiresAt` years as 2022 on question 2 and invented an `action` parameter; Haiku sent only the needed arguments but was cut off on question 2 (finish reason `length`: no `MaxOutputTokens` in the console, so the Anthropic adapter's default 1,024 applies, thinking included). No secrets in either log. Haiku accepted every tool round trip (the empty-text watch item didn't show up)
- [x] Measure the **cost of tool definitions**: input tokens for the same question with `/saas off` vs `/saas on`
  - **Done (handed over):** question 4, first call only, `ollama stop llama3.2` before each llama measurement. llama3.2 31 → 769 (≈ 738 tokens), Haiku 20 → 1,470 (≈ 1,450 tokens, about $0.000145 per call, 2 calls per tool question)
- [x] Record expected vs actual in `experiments.md` (new section), per the repo convention
  - **Done (handed over):** section 18 (both tables, the tool-definition cost, conclusion and follow-ups). **Expected** is left for me to write. Recorded run cost: $0.003003

---

## Block 5: Stretch (optional)

Skipped for now; the token-provider tests are the first one worth coming back to (the live runs never reached a token expiry or a 401 on the exchange).

- [ ] Unit tests (in `AiLab.Api.Tests`, with the other plain unit tests) for `SaasApiTokenProvider` over a fake `HttpMessageHandler`: caches the token, logs in again near expiry, never logs secrets
- [ ] Read about approval for tool calls in `Microsoft.Extensions.AI` (approval-required functions). Today's tools are read-only, so they run without asking; write one paragraph on how a write tool should be approved in a console app
- [ ] Show `AiLab.McpServer`'s tools too (stdio, `StdioClientTransport`): one `McpClient` per server, tools merged into one list. That makes the console a small MCP host with two clients

---

## Block 6: Docs and PR (20 min)

- [x] `CLAUDE.md`: Week 6 Wednesday entry (and tick Monday's 5.2 and Tuesday's item if they're still open), Console description (`/saas`, `UseFunctionInvocation`), Infrastructure `Mcp/` line, settings and user-secrets keys
  - **Done (handed over):** Infrastructure `Mcp/` line, Console `/saas` description, console host pipeline and `SaasMcp:*` config (with the 1,024-token note), secrets keys `SaasMcp:Email` / `SaasMcp:Password`, current step, and a Week 6 item 2 with Blocks 1–5 and the follow-ups. Monday's 5.2 (my notes, outside the repo) left unticked: only I know if it's done; there's no Tuesday item in this repo (the auth hardening is in the SaaS repo)
- [x] `README.md`: "Using the SaaS API's tools from the console" (start the API, user-secrets keys, `/saas on`)
  - **Done (handed over):** that section (steps, an example turn, how it signs in, what's worth knowing), a `/saas` row in the commands table, whole-turn trimming under `MaxHistoryMessages`, `Mcp/` in the project structure, Week 6 lessons and cost ($0.003) in "What I learned", and the Status line
- [x] Commit at the end of each block; PR

---

## End of day
- [ ] Both deliverables checked (D1 in MultiTenantSaaSApi, D2 here)
- [ ] Ask Claude for today's recap PDF (for OneDrive)