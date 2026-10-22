# Stage 3: tool calling in an ASP.NET Core API

- **Date:** 2026-10-22
- **Branch:** stage-3-tool-calling
- **Goal:** A minimal API, `POST /analyze-ticket`, that turns a support ticket into a summary, a probable cause and test cases. The model calls one tool (a fake change log) through the function-invocation middleware. Timeouts and retries are set so one failure can't become many billed calls.
- **Status:** done
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
- [x] **2.1** `AiLab.Api/ChangeLog/`: `ChangeEntry(string Component, DateTimeOffset ChangedAt, string Version, string Description)`, `IChangeLogRepository` with `IReadOnlyList<ChangeEntry> GetByComponent(string component)` and `IReadOnlyList<string> GetComponents()`, and `InMemoryChangeLogRepository` (singleton). Matching is case-insensitive.
- [x] **2.2** Seed about 8 changes over billing, invoices, auth and tenants (multi-tenant SaaS). Exactly one clearly explains ticket A; the rest is unrelated noise. One more change explains ticket C (whose component isn't given).
- [x] **2.3** `AiLab.Api/analyze-ticket.http` with three requests:
  - **A:** matches the explaining change, component given
  - **B:** matches no change, component given
  - **C:** matches a change, no component in the request
- [x] **2.4 (you)** **STOP.** I show you the seeded changes and the three tickets. You edit them. No model call before this.
- [x] **2.5** Commit after your edits.

### Part 3: the tool (deliverable 2)
- [x] **3.1** `TicketTools(IChangeLogRepository, ILogger<TicketTools>)` with one method `GetRecentChanges(string component)`. `[Description]` on the method ("Gets the recent deployed changes for one component of the SaaS. Call it before naming a probable cause.") and on the parameter (lists the known components).
- [x] **3.2** It returns a string: one line per change (date, version, description), newest first. Unknown component → `"Unknown component 'x'. Known components: auth, billing, invoices, tenants."`, not an empty list, so the model can retry with a real name.
- [x] **3.3** It logs `GetRecentChanges called with component {Component}` at Information each time it runs.
- [x] **3.4** Exposed with `AIFunctionFactory.Create(tools.GetRecentChanges)` and passed in `ChatOptions.Tools`. Commit.

### Part 4: the endpoint (deliverable 1)
- [x] **4.1** `AnalyzeTicketRequest(string Title, string Description, string? Component)`.
- [x] **4.2** `TicketAnalysis(string Summary, string ProbableCause, List<string> TestCases)` with `[Description]` rules on each property, including: "The probable cause must name a specific change from the change log if one is relevant; otherwise say the cause is unknown. Never invent a change."
- [x] **4.3** `TicketAnalyzer(IChatClient, TicketTools)`: builds a system message (role, "use the tool before deciding the cause") and a user message (title, description, component if given), then calls `GetResponseAsync<TicketAnalysis>(messages, options, useJsonSchemaResponseFormat: false, ct)` with `MaxOutputTokens = 2000` and the tool. Parses with `StructuredReply.TryParse`.
- [x] **4.4** Diagnostics, returned next to the analysis in the response:
  - tool calls: name and arguments from every `FunctionCallContent` in `response.Messages`
  - round trips: the number of assistant messages in `response.Messages`
  - tokens in/out from `response.Usage` (the middleware sums usage over the loop), cost from `ModelPrices.Cost`
  - whether the reply was fenced
- [x] **4.5** Endpoint `MapPost("/analyze-ticket", ...)` only validates and calls the service.
  - 400 (ValidationProblem) when Title or Description is empty
  - 502 `{ "error": "The model's reply could not be read as a ticket analysis." }` when parsing fails
  - 502 `{ "error": "The model provider is unavailable." }` when the provider call fails after retries
  - `AddProblemDetails` + `UseExceptionHandler` so anything else is a short 500 problem, never a stack trace (also in Development)
- [x] **4.6** The endpoint's `CancellationToken` is passed to the service, `GetResponseAsync`, and on into the tool call (`AIFunction` gets it from the middleware).
- [x] **4.7** Test A, B and C on Ollama and show you the results. Commit.

### Part 5: timeouts and retries (deliverable 4)
- [x] **5.1** `appsettings.json` section:
  ```json
  "Resilience": {
    "AttemptTimeoutSeconds": 60,
    "MaxRetries": 2,
    "RequestTimeoutSeconds": 240,
    "MaxToolIterations": 3
  },
  "Chat": { "Provider": "ollama", "Model": "llama3.2", "OllamaUrl": "http://localhost:11434" }
  ```
- [x] **5.2** Ollama: a named `HttpClient` from `IHttpClientFactory` with `AddStandardResilienceHandler`. Attempt timeout 60 s, 2 retries (3 attempts), exponential backoff with jitter, the handler's total timeout 200 s, circuit breaker sampling 120 s (the handler requires at least 2 × attempt timeout). `HttpClient.Timeout` set to infinite, so the handler owns all timers (the default 100 s would cut in first). Passed to `new OllamaApiClient(httpClient, model)` via the factory.
- [x] **5.3** Anthropic: no resilience handler. The SDK already retries (connection errors, timeouts, 408, 409, 429, 5xx; default 2 retries) and has a per-attempt `Timeout` (default 10 minutes). I set `Timeout = 60 s` and `MaxRetries = 2` from the same config section. **Only one layer retries per provider:** Polly for Ollama, the SDK for Anthropic. Adding Polly on top of the SDK would make one failure up to 3 × 3 = 9 billed calls.
- [x] **5.4** `UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = 3)` from config.
- [x] **5.5** `AddRequestTimeouts` + `UseRequestTimeouts`, and `.WithRequestTimeout(policy)` on the endpoint: 240 s, status 504, body `{ "error": "The request timed out." }`. **Numbers:** attempt timeout × attempts = 60 × 3 = 180 s, plus backoff (about 2 + 4 s with jitter) ≈ 186 s, under the handler's 200 s and the endpoint's 240 s.
- [x] **5.6** Proof (a): run with `Resilience__RequestTimeoutSeconds=1`, send ticket A, show the 504 body and no stack trace in the log.
- [x] **5.7** Proof (b): run with `Chat__OllamaUrl=http://localhost:11999`, send ticket A, show the Polly retry lines with their delays in the log and the final 502.
- [x] **5.8** Both overrides are env vars on one process, so there is nothing to restore in `appsettings.json`; I check `git diff` shows no change. Commit.

### Part 6: the recorded runs
- [x] **6.1 (you)** **STOP before any Haiku call.** You write the Expected line. I add it as-is to a new section 7 of `experiments.md` and commit it before any run.
- [x] **6.2** Ollama: A, B, C, D once each. Save the response bodies.
- [x] **6.3** Haiku: A, B, C, D once each, in one API process started with `Chat__Provider=anthropic Chat__Model=claude-haiku-5-5`. Expected well under $0.01. I add up the cost after each request and stop and tell you if Haiku spending in this stage goes above $0.03. Then the process is stopped; `appsettings.json` stays on Ollama the whole time.

### Part 7: write it up
- [x] **7.1** `experiments.md` section 7, "Tool calling", plain first-person tone:
  - the Expected line
  - one table per model: ticket, tool called (yes/no and component), probable cause (right / unknown / invented), round trips, tokens in/out, cost
  - tokens per request compared with section 5's extraction (tool round trips re-send the conversation)
  - the timeout and retry checks from Part 5
  - a short conclusion
- [x] **7.2** Commit. Update this plan's Changes log and Status, `CLAUDE.md` (stage 3 items ticked, stack notes for `AiLab.Core` and `AiLab.Api`), and `README.md` (what the API does, how to run it, "What I learned"). Commit.

## Tests
All manual (no test project), Ollama unless stated, from the repo root:
- **Part 1 (console regression):** `dotnet build AiLab.slnx` succeeds; `dotnet run --project AiLab.Console` → `/extract v2 inputs/job-1.txt` prints the same layout and score as before; one chat turn prints usage.
- **Part 1 (API):** `dotnet run --project AiLab.Api` starts; the log file is created; nothing is logged to the console beyond the host's startup lines.
- **Part 4 (endpoint, via `analyze-ticket.http` or curl):**
  - A, B, C return 200 with analysis and diagnostics (tool calls, round trips, tokens, cost)
  - the tool's log line appears once per tool call, with the component
  - empty Title or Description → 400
  - an unknown component in the request → the tool's "known components" message reaches the model (seen in the Verbose log or a retry call)
  - *(added in Part 4)* malformed JSON and an empty body → 400 problem, not 500
  - *(added in Part 4)* client disconnects mid-request (`curl -m 0.5`) → the log shows `GetResponseAsync canceled.` and no error
  - *(added in Part 4)* both 502 paths, with a fake `IChatClient` under the real function-invocation middleware (throwaway `dotnet run check.cs` in the scratchpad, not committed): a model that keeps calling the tool with no arguments → "could not use the change-log tool"; a reply that isn't JSON → "could not be read as a ticket analysis"
- **Part 5:** (a) 1 s request timeout → 504 with the short body; (b) wrong Ollama port → retry lines with growing delays in the log, then 502 with the short body. `git diff appsettings.json` empty afterwards.
  - *(added in Part 5)* the iteration cap, with a fake client that keeps making valid tool calls (throwaway `check.cs`): it stops after 3 tool round trips plus one final call without tools, then gives the parse 502
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
- **2026-10-22:** Part 2 done. Eight changes, two per component: billing 4.18.0 (proration rounds up, preview not updated) explains A, invoices 4.19.0 (per-tenant numbering) explains C, the other six are noise. At the user's request the descriptions were rewritten as short changelog lines and the tickets as customer messages. Ticket C's title ("numbering is messed up") no longer names invoices, so the model has to pick the component from the description. Approved by the user.
- **2026-10-22:** Part 3 done. `AiLab.Api/Tools/TicketTools.cs`, registered as a singleton. `AsAIFunction()` wraps `GetRecentChanges` with `AIFunctionFactory.Create`; putting it in `ChatOptions.Tools` happens in `TicketAnalyzer` (Part 4), since that's where the options live. The parameter's `[Description]` lists the four components as a constant, because attribute values can't be read from the repository; if a component is added, that text has to change too. Checked without a model by a throwaway `dotnet run check.cs` script (scratchpad, not committed): the generated schema is `{"component": string, required}` with both descriptions; `billing` and `"  INVOICES "` return their two changes newest first; `payments` returns the known-components message; each call logs `GetRecentChanges called with component ...`.
- **2026-10-22:** Part 4 done.
  - Files in `AiLab.Api/Tickets/`: `AnalyzeTicketRequest` (with `Validate()`), `TicketAnalysis`, `AnalyzeTicketResponse` (analysis + diagnostics + `ErrorResponse`), `ModelCallException`, `TicketAnalyzer`. `Program.cs` maps `POST /analyze-ticket`, which validates, calls the service and turns `ModelCallException` into a 502.
  - **Bug found, bad body gave 500:** in Development, minimal APIs throw `BadHttpRequestException` for malformed JSON or a missing body, and `UseExceptionHandler` made that a 500. Fixed with `ExceptionHandlerOptions.StatusCodeSelector`, which keeps the exception's 400. Test added.
  - **Bug found, tool loop gave 500:** with an unknown component (`payments`), Llama once called the tool correctly, then again and again with no arguments. With no iteration cap yet (default 40) it ran about 10 model calls until 3 binding failures in a row; then the middleware rethrew the `ArgumentException`, which became a 500. The analyzer now turns `ArgumentException` from the loop into a 502 "could not use the change-log tool". A catch-all was tried first and rejected: the fake-client test showed it reporting a bug of ours as a model problem. Part 5.4's cap of 3 will also cut the loop short. In two more runs, Llama answered "cause unknown" after a single call. Test added (fake client).
  - The summary log line first used `{@ToolCalls}`, which Serilog destructured into `JsonElement` internals; it now logs `GetRecentChanges({"component":"billing"})` as text.
  - **Ollama results (two runs each, same outcome):** A → billing, names v4.18.0 (right). B → auth, names v4.17.1 "admin sessions expire after 8h" (invented: that change is unrelated). C → picks invoices itself, names v4.19.0 (right). Each request is 2 round trips and about 970–1015 tokens in, 120–150 out.
- **2026-10-22:** Part 5 done.
  - **Config:** `Resilience` section (60 s attempt, 2 retries, 240 s request, 3 tool iterations) bound to `ResilienceSettings`, plus `Chat:OllamaUrl`. `Microsoft.Extensions.Http.Resilience` 10.11.0.
  - **Ollama:** named `HttpClient` "ollama" with `AddStandardResilienceHandler`. Settings: attempt 60 s, 2 retries, total = attempts × attempt timeout + 20 s margin for backoff = 200 s, circuit breaker sampling 120 s, `HttpClient.Timeout` infinite. **Anthropic:** SDK `Timeout = 60 s`, `MaxRetries = 2`, no Polly. One retry layer per provider.
  - **Polly's own give-ups** throw `ExecutionRejectedException` (total timeout, open circuit), not `HttpRequestException`, so the analyzer counts them as provider failures (502).
  - **Startup warning** (not a crash) when `RequestTimeoutSeconds` ≤ attempt timeout × attempts, so the 1 s proof can still start.
  - **Iteration cap, corrected:** `MaximumIterationsPerRequest = 3` allows 3 model calls with tools, then the middleware makes one last call *without* tools to force a text answer. So the worst case is 4 model calls, not 3 (seen with a fake client). Numbers: one model call is at most 3 × 60 s + backoff ≈ 186 s (Polly cuts it at 200 s). The 240 s request timeout covers that, but not 4 such calls (≈ 744 s); it's the outer budget and cuts them off on purpose.
  - **Proof (a)** `Resilience__RequestTimeoutSeconds=1`: 504 `{"error":"The request timed out."}` after 1.02 s, during the second round trip. The log shows the startup warning, `GetResponseAsync canceled.` and the middleware's `Timeout exception handled.`
  - **Proof (b)** `Chat__OllamaUrl=http://localhost:11999`: attempts 0, 1, 2 with `OnRetry` waits of 0.44 s and 3.33 s (2 s exponential base, jittered), then 502 `{"error":"The model provider is unavailable."}` after 3.8 s. One `GetResponseAsync` only, so nothing outside Polly retried.
  - **Nothing to restore:** both overrides were env vars on one process; `appsettings.json` only has this part's planned additions.
  - **Not tested at runtime:** the Anthropic SDK settings (that would need a failing billed call). The client builds with them at startup.
- **2026-10-22:** Added at the user's request (not in the approved plan): OpenAPI and Swagger UI for the API, in Development only.
  - **Packages:** `Microsoft.AspNetCore.OpenApi` 10.0.13 builds the document at `/openapi/v1.json` (OpenAPI 3.1). `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1 shows it at `/swagger`.
  - **What's documented:** document info; on the endpoint, name, tag, summary, description and the response types (200, 400, 500, 502, 504), declared with `Produces`, because the handler returns `IResult`. The request and response records carry `[Description]`s; the ones on `AnalyzeTicketRequest`, `AnalyzeTicketResponse`, `AnalysisDiagnostics`, `ToolCall` and `ErrorResponse` only document the API, while `TicketAnalysis`'s are also the model's rules.
  - **Examples:** `OpenApi/AnalyzeTicketExamples.cs`, an operation transformer. Request examples A, B and C are copied from `analyze-ticket.http`, so the two have to be kept in sync. Response examples: a real Ollama reply to A (200), plus 400, 502 and 504.
  - **Fixes the document showed:** `Component` now defaults to `null`, otherwise the generator listed it as required. `NumberHandling.Strict` stops every number showing as "integer or string"; no request field is a number. `WithRequestTimeout` moved to the end of the endpoint chain, because it returns `IEndpointConventionBuilder`, which has no `Produces`.
  - **Checks:** `/swagger/index.html` and `/openapi/v1.json` return 200. Only `title` and `description` are required. The diagnostics types are plain. An empty title still gives 400, ticket C (no component) still gives 200 with `invoices` → v4.19.0, and the 1 s request timeout still gives 504.
- **2026-10-22:** Ticket D added at the user's request, from a request they sent through Swagger UI: "I can't sign-in" / "I am trying to sign-in to the application but I can't. It's always failing", component `auth`. Neither auth change (8h admin sessions, optional TOTP 2FA) explains it, so the right answer is "unknown". Llama blamed v4.17.1, the same invention as on ticket B. D is in `analyze-ticket.http` and the Swagger examples. Part 6 now runs A–D on both models: 4 Haiku requests instead of 3, still well under the $0.03 limit.
- **2026-10-22:** Part 6 done.
  - **Expected line:** the user's line was added to section 7 as written and committed (`0d0adc6`) before any Haiku call.
  - **Runs:** A–D once each per model, one API process per model, started by a script that checks port 5028 first and stops only its own PID. Haiku used the env vars `Chat__Provider=anthropic Chat__Model=claude-haiku-5-5`, so `appsettings.json` never changed. Replies are saved in `eval-runs/20261022-210646_tool-calling_llama3.2/` and `eval-runs/20261022-210710_tool-calling_claude-haiku-5-5/` (git-ignored).
  - **Llama:** A right, but the cause repeats the change's text without its version. B invented (v4.17.1). C right (v4.19.0). D invented (v4.17.1). 2 round trips each; 903–1015 tokens in, 114–192 out.
  - **Haiku:** A right (v4.18.0, and explains the preview mismatch). B unknown. C right (v4.19.0, and notes the missing counter migration). D unknown: it names TOTP 2FA as a possibility to check but doesn't claim it. Every reply was fenced. 2 round trips each; 1871–2048 tokens in, 599–810 out.
  - **Haiku cost:** $0.002246 for 4 requests, far below the $0.03 limit.
- **2026-10-22:** Part 7 done.
  - **`experiments.md` section 7:** setup, a table per model (A–D), a token comparison with section 5, the timeout and retry checks, observations and a conclusion. All three parts of the Expected line held.
  - **Per-call token split:** I ran ticket A once more on Llama with `Verbose` logging (free; no extra Haiku call). It showed 515 + 501 input tokens. The second call is smaller because Llama 3.2's Ollama template inserts the tool definitions only when the last message is from the user. That template also tells the model to "respond with a JSON for a function call".
  - **Docs:** `CLAUDE.md` (three-project layout, API, resilience, Stage 3 done) and `README.md` (a "The ticket API" section with run instructions, a curl example and its real response, the errors and resilience tables, project structure, Stage 3 "What I learned", total spend about $0.029, status) are updated. Status: done.
