# Stage 3: tool calling in an ASP.NET Core API

- **Date:** 2026-10-22
- **Branch:** stage-3-tool-calling
- **Goal:** A minimal API, `POST /analyze-ticket`, that turns a support ticket into a summary, a probable cause and test cases. The model calls one tool (a fake change log) through the function-invocation middleware. Timeouts and retries are set so one failure can't become many billed calls.
- **Status:** in-progress
- **Approval:** approved in plan mode

Claude writes the code for this stage (the user's request overrides tutor mode) and explains each change briefly. Steps marked **(you)** are the user's decisions.

## Deliverables
1. `POST /analyze-ticket` returning a summary, a probable cause and test cases
2. One tool backed by a fake repository, invoked automatically by the model
3. DI registration of `IChatClient`
4. Timeouts and retries configured

## Steps

### Part 0: setup
- [x] **0.1** Save this plan to `docs/plans/2026-10-22-stage-3-tool-calling.md`, create branch `stage-3-tool-calling`.
- [x] **0.2** `CLAUDE.md`: add "Stage 3: tool calling in ASP.NET Core" with the four deliverables as unchecked items, and set the current step. Commit.

### Part 1: project structure and DI (deliverable 3)
- [x] **1.1** New class library `AiLab.Core` (net10.0), added to `AiLab.slnx`. Packages: `Microsoft.Extensions.AI`, `Anthropic`, `OllamaSharp` (same versions as the console).
- [x] **1.2** Move into it:
  - `ChatClientFactory` → `AiLab.Core/Providers/`
  - `ModelPrices` → `AiLab.Core/Pricing/`
  - `JobParser`'s fence logic → `AiLab.Core/Parsing/StructuredReply.cs`: `TryParse<T>(ChatResponse<T>, out T?, out bool strippedFence)`. Same rules as today (whole-reply fence only, `AIJsonUtilities.DefaultOptions`). `JobParser` is deleted and its two call sites in `ChatSession` call `StructuredReply.TryParse<JobPosting>`.
- [x] **1.3** `ChatClientFactory.Create` gets optional parameters: an `HttpClient` for Ollama, the Ollama URL, and the Anthropic attempt timeout and max retries. Called with only the current three arguments it does exactly what it does today, so the console is unchanged.
- [x] **1.4** `AiLab.Console` references `AiLab.Core`; its moved files and now-unused package references are removed. Check: `dotnet build`, then `/extract v2 inputs/job-1.txt` and one chat turn on Ollama look as before.
- [x] **1.5** New `AiLab.Api` (minimal API, `Microsoft.NET.Sdk.Web`) referencing `AiLab.Core`, added to the solution. Same `UserSecretsId` as the console, so the Anthropic key is shared and not entered twice. `launchSettings.json` with one `http` profile, Development environment.
- [x] **1.6** `Program.cs`:
  ```csharp
  builder.Services.AddChatClient(sp => ChatClientFactory.Create(...))   // Chat:Provider, Chat:Model, Anthropic:ApiKey
      .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = ...)
      .UseLogging();
  ```
  **Why this order:** in `ChatClientBuilder`, the first `Use...` is the outermost layer. A request goes: function invocation → logging → provider. Function invocation runs the tool loop: it calls the inner client, sees a tool call, runs the tool, calls the inner client again. Because logging sits inside that loop, each of those inner calls is logged separately, including the tool-call steps. The other way round (logging outside) would log one call per HTTP request and hide the round trips.
- [x] **1.7** Serilog to a daily file in `AiLab.Api/bin/Debug/net10.0/logs/api-.log`, same setup as the console (`AddSerilog`, `ReadFrom.Configuration`, `WriteTo.File`). No console sink. Levels in `appsettings.json`: Default `Warning`, `Microsoft.Extensions.AI` `Debug`, `AiLab` `Information`.
- [x] **1.8** Commit.

✅ Done when both projects build, the console behaves as before, and the API starts with the chat client resolved from DI.

### Part 2: fake data (you review this)
- [ ] **2.1** `AiLab.Api/ChangeLog/`: `ChangeEntry(string Component, DateTimeOffset ChangedAt, string Version, string Description)`, `IChangeLogRepository` with `IReadOnlyList<ChangeEntry> GetByComponent(string component)` and `IReadOnlyList<string> GetComponents()`, and `InMemoryChangeLogRepository` (singleton). Matching is case-insensitive.
- [ ] **2.2** Seed about 8 changes over billing, invoices, auth and tenants (multi-tenant SaaS). Exactly one clearly explains ticket A; the rest is unrelated noise. One more change explains ticket C (whose component isn't given).
- [ ] **2.3** `AiLab.Api/analyze-ticket.http` with three requests:
  - **A:** matches the explaining change, component given
  - **B:** matches no change, component given
  - **C:** matches a change, no component in the request
- [ ] **2.4 (you)** **STOP.** I show you the seeded changes and the three tickets. You edit them. No model call before this.
- [ ] **2.5** Commit after your edits.

### Part 3: the tool (deliverable 2)
- [ ] **3.1** `TicketTools(IChangeLogRepository, ILogger<TicketTools>)` with one method `GetRecentChanges(string component)`. `[Description]` on the method ("Gets the recent deployed changes for one component of the SaaS. Call it before naming a probable cause.") and on the parameter (lists the known components).
- [ ] **3.2** It returns a string: one line per change (date, version, description), newest first. Unknown component → `"Unknown component 'x'. Known components: auth, billing, invoices, tenants."`, not an empty list, so the model can retry with a real name.
- [ ] **3.3** It logs `GetRecentChanges called with component {Component}` at Information each time it runs.
- [ ] **3.4** Exposed with `AIFunctionFactory.Create(tools.GetRecentChanges)` and passed in `ChatOptions.Tools`. Commit.

### Part 4: the endpoint (deliverable 1)
- [ ] **4.1** `AnalyzeTicketRequest(string Title, string Description, string? Component)`.
- [ ] **4.2** `TicketAnalysis(string Summary, string ProbableCause, List<string> TestCases)` with `[Description]` rules on each property, including: "The probable cause must name a specific change from the change log if one is relevant; otherwise say the cause is unknown. Never invent a change."
- [ ] **4.3** `TicketAnalyzer(IChatClient, TicketTools)`: builds a system message (role, "use the tool before deciding the cause") and a user message (title, description, component if given), then calls `GetResponseAsync<TicketAnalysis>(messages, options, useJsonSchemaResponseFormat: false, ct)` with `MaxOutputTokens = 2000` and the tool. Parses with `StructuredReply.TryParse`.
- [ ] **4.4** Diagnostics, returned next to the analysis in the response:
  - tool calls: name and arguments from every `FunctionCallContent` in `response.Messages`
  - round trips: the number of assistant messages in `response.Messages`
  - tokens in/out from `response.Usage` (the middleware sums usage over the loop), cost from `ModelPrices.Cost`
  - whether the reply was fenced
- [ ] **4.5** Endpoint `MapPost("/analyze-ticket", ...)` only validates and calls the service.
  - 400 (ValidationProblem) when Title or Description is empty
  - 502 `{ "error": "The model's reply could not be read as a ticket analysis." }` when parsing fails
  - 502 `{ "error": "The model provider is unavailable." }` when the provider call fails after retries
  - `AddProblemDetails` + `UseExceptionHandler` so anything else is a short 500 problem, never a stack trace (also in Development)
- [ ] **4.6** The endpoint's `CancellationToken` is passed to the service, `GetResponseAsync`, and on into the tool call (`AIFunction` gets it from the middleware).
- [ ] **4.7** Test A, B and C on Ollama and show you the results. Commit.

### Part 5: timeouts and retries (deliverable 4)
- [ ] **5.1** `appsettings.json` section:
  ```json
  "Resilience": {
    "AttemptTimeoutSeconds": 60,
    "MaxRetries": 2,
    "RequestTimeoutSeconds": 240,
    "MaxToolIterations": 3
  },
  "Chat": { "Provider": "ollama", "Model": "llama3.2", "OllamaUrl": "http://localhost:11434" }
  ```
- [ ] **5.2** Ollama: a named `HttpClient` from `IHttpClientFactory` with `AddStandardResilienceHandler`. Attempt timeout 60 s, 2 retries (3 attempts), exponential backoff with jitter, the handler's total timeout 200 s, circuit breaker sampling 120 s (the handler requires at least 2 × attempt timeout). `HttpClient.Timeout` set to infinite, so the handler owns all timers (the default 100 s would cut in first). Passed to `new OllamaApiClient(httpClient, model)` via the factory.
- [ ] **5.3** Anthropic: no resilience handler. The SDK already retries (connection errors, timeouts, 408, 409, 429, 5xx; default 2 retries) and has a per-attempt `Timeout` (default 10 minutes). I set `Timeout = 60 s` and `MaxRetries = 2` from the same config section. **Only one layer retries per provider:** Polly for Ollama, the SDK for Anthropic. Adding Polly on top of the SDK would make one failure up to 3 × 3 = 9 billed calls.
- [ ] **5.4** `UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = 3)` from config.
- [ ] **5.5** `AddRequestTimeouts` + `UseRequestTimeouts`, and `.WithRequestTimeout(policy)` on the endpoint: 240 s, status 504, body `{ "error": "The request timed out." }`. **Numbers:** attempt timeout × attempts = 60 × 3 = 180 s, plus backoff (about 2 + 4 s with jitter) ≈ 186 s, under the handler's 200 s and the endpoint's 240 s.
- [ ] **5.6** Proof (a): run with `Resilience__RequestTimeoutSeconds=1`, send ticket A, show the 504 body and no stack trace in the log.
- [ ] **5.7** Proof (b): run with `Chat__OllamaUrl=http://localhost:11999`, send ticket A, show the Polly retry lines with their delays in the log and the final 502.
- [ ] **5.8** Both overrides are env vars on one process, so there is nothing to restore in `appsettings.json`; I check `git diff` shows no change. Commit.

### Part 6: the recorded runs
- [ ] **6.1 (you)** **STOP before any Haiku call.** You write the Expected line. I add it as-is to a new section 7 of `experiments.md` and commit it before any run.
- [ ] **6.2** Ollama: A, B, C once each. Save the response bodies.
- [ ] **6.3** Haiku: A, B, C once each, in one API process started with `Chat__Provider=anthropic Chat__Model=claude-haiku-5-5`. Expected well under $0.01. I add up the cost after each request and stop and tell you if Haiku spending in this stage goes above $0.03. Then the process is stopped; `appsettings.json` stays on Ollama the whole time.

### Part 7: write it up
- [ ] **7.1** `experiments.md` section 7, "Tool calling", plain first-person tone:
  - the Expected line
  - one table per model: ticket, tool called (yes/no and component), probable cause (right / unknown / invented), round trips, tokens in/out, cost
  - tokens per request compared with section 5's extraction (tool round trips re-send the conversation)
  - the timeout and retry checks from Part 5
  - a short conclusion
- [ ] **7.2** Commit. Update this plan's Changes log and Status, `CLAUDE.md` (stage 3 items ticked, stack notes for `AiLab.Core` and `AiLab.Api`), and `README.md` (what the API does, how to run it, "What I learned"). Commit.

## Tests
All manual (no test project), Ollama unless stated, from the repo root:
- **Part 1 (console regression):** `dotnet build AiLab.slnx` succeeds; `dotnet run --project AiLab.Console` → `/extract v2 inputs/job-1.txt` prints the same layout and score as before; one chat turn prints usage.
- **Part 1 (API):** `dotnet run --project AiLab.Api` starts; the log file is created; nothing is logged to the console beyond the host's startup lines.
- **Part 4 (endpoint, via `analyze-ticket.http` or curl):**
  - A, B, C return 200 with analysis and diagnostics (tool calls, round trips, tokens, cost)
  - the tool's log line appears once per tool call, with the component
  - empty Title or Description → 400
  - an unknown component in the request → the tool's "known components" message reaches the model (seen in the Verbose log or a retry call)
- **Part 5:** (a) 1 s request timeout → 504 with the short body; (b) wrong Ollama port → retry lines with growing delays in the log, then 502 with the short body. `git diff appsettings.json` empty afterwards.
- **Part 6:** Haiku total in this stage ≤ $0.03.

## Decisions & trade-offs
- **Middleware order:** function invocation outer, logging inner, so each model round trip is a separate log entry (see 1.6).
- **Tool returns a string,** in both the found and unknown cases. One return type keeps the schema simple, and the model reads text anyway.
- **Diagnostics in the response body,** not only the log: easier to compare the three tickets and copy into `experiments.md`. Fine for a lab, not for production.
- **Round trips = assistant messages in `response.Messages`.** This counts model calls inside the loop without a custom middleware.
- **One retry layer per provider** (5.3). Polly can't see the SDK's own retries, and the SDK's retries respect Anthropic's `retry-after` / `x-should-retry` headers, so the SDK is the better owner there.
- **Retrying POST to Ollama** is allowed (the standard handler retries all methods by default). Ollama is local and free, and a generation has no side effects.
- **Overrides via env vars** (`Chat__OllamaUrl`, `Resilience__RequestTimeoutSeconds`) instead of editing `appsettings.json` for the Part 5 proofs, so nothing has to be restored. `Chat:OllamaUrl` is new config for this.
- **Request timeout caveat:** ASP.NET Core's request timeouts are skipped when a debugger is attached, so the proofs run with `dotnet run`, not F5.
- **Per-call budget vs per-request budget.** The 240 s request timeout covers attempts × attempt timeout for one model call. With up to 3 tool iterations, a worst case of many slow calls is cut off by the request timeout on purpose: it's the outer budget.
- **Shared `UserSecretsId`** between console and API, so the key lives in one place.
- **Generic parser named `StructuredReply`,** and `JobParser` removed rather than kept as a wrapper, so there's one implementation.

## Alternatives considered
- **Logging outside function invocation:** one log entry per request, round trips hidden. Rejected (brief asks for every round trip).
- **Polly on the Anthropic `HttpClient` with SDK `MaxRetries = 0`:** one retry policy for both providers, but loses the SDK's header-aware retries and the SDK warns its custom `HttpClient` timeout can conflict. Rejected.
- **Returning a list of `ChangeEntry` from the tool:** structured, but needs a different shape for the unknown-component message. Rejected.

## Out of scope
- Automated tests and a test project.
- Streaming responses from the API.
- Auth, rate limiting, or multi-tenant isolation on the API.
- A real change-log store.
- Resilience in the console app (it keeps today's behaviour).
- Changing the extraction prompts or `JobPosting`.

## Changes
- **2026-10-22:** Parts 0 and 1 done.
  - `AiLab.Core` holds `ChatClientFactory` (`AiLab.Core.Providers`), `ModelPrices` (`AiLab.Core.Pricing`) and `StructuredReply.TryParse<T>` (`AiLab.Core.Parsing`). The files were moved with `git mv`. `JobParser` is gone; `ChatSession` calls `StructuredReply.TryParse(response, ...)` and `T` is inferred from the `ChatResponse<JobPosting>`.
  - `ChatClientFactory.Create` has the optional Ollama `HttpClient`, Ollama URL, Anthropic timeout and Anthropic max retries parameters already. Only Part 5 passes them; the console and the API pass the three original arguments for now.
  - `AiLab.Api`: `dotnet new web`, `https` profile and `appsettings.Development.json` removed, `launchBrowser` off. The content root is the output folder (like the console), so `dotnet run --project AiLab.Api` from the repo root finds `appsettings.json`. The `IChatClient` is resolved once at startup, so missing config fails at start. `UseFunctionInvocation()` has no iteration cap yet (step 5.4).
  - Added `Microsoft.Hosting.Lifetime: Information` to the Serilog overrides, so the listening URL is in the log file.
  - Checks: the solution builds with 0 warnings. The API starts on Ollama and on Anthropic (so the shared user-secret loads, with no model call), writes only `Logs: ...` to the console, and logs to `AiLab.Api/bin/Debug/net10.0/logs/`. Console on Ollama: one chat turn prints usage. `/extract v1 inputs/job-1.txt` prints the same layout and score as before (3 of 5, 9 of 12). The other two runs failed in ways Llama already showed in Stage 2: v2 sent `"workMode": "null"` as a string, and one v1 run echoed the schema.
