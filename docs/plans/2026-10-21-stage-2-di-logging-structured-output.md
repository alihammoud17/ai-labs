# Stage 2: DI, logging middleware, typed output, prompt v1 vs v2

- **Date:** 2026-10-21
- **Branch:** stage-2-di-logging-structured-output
- **Goal:** The same chat code runs on two providers through DI. Every model call is logged. Job posts are extracted into a typed C# record, and a one-line prompt is scored against a five-part prompt.
- **Status:** in-progress
- **Approval:** approved in plan mode

Steps marked **(you)** are hands-on work only you can do.

## Steps

### Deliverable 1: same code on two providers (≈1 hour)
- [x] **1.1** Add `Microsoft.Extensions.Hosting`. Replace `ConfigurationBuilder` with `Host.CreateApplicationBuilder(args)`. It only loads user-secrets in Development, so set `DOTNET_ENVIRONMENT=Development` in `Properties/launchSettings.json`. Without it, the Anthropic key is silently missing.
- [x] **1.2** Register the client with `builder.Services.AddChatClient(sp => ...)`. Read the provider and model from config, then call the existing `ChatClientFactory`. Build the host and get the client with `GetRequiredService<IChatClient>()`.
- [x] **1.3** Move the chat loop into a `ChatSession` class. It takes `IChatClient` (and later `ILogger`) in its constructor. Register it and resolve it from DI.
- [x] **Think about:** where the pricing model ID comes from now. `ChatSession` needs it for `ModelPrices.Cost` but shouldn't know the provider. Options are `ChatResponse.ModelId`, `ChatClientMetadata.DefaultModelId`, or passing it through options.

✅ Done when changing only `appsettings.json` switches Llama ↔ Claude, with no `if (provider == ...)` outside the registration.

### Deliverable 2: `UseLogging` middleware (≈45 min)
- [x] **2.1** Chain `.UseLogging()` onto the `ChatClientBuilder` that `AddChatClient(...)` returns. It picks up `ILoggerFactory` from DI.
- [x] **2.2** Set the log level for the `Microsoft.Extensions.AI` category under `Logging:LogLevel` in `appsettings.json`. `Debug` shows basic call info and `Trace` shows full message content.
- [x] **2.3** Deal with the noise of logs mixed into streamed output. Either keep the level at Debug or log to a file. Pick one and note why.
- [x] **2.4** Run one turn and read the log. For example, check whether `/reset` kept the system message.
- [x] **Think about:** middleware order. Should logging go before or after a future `.UseDistributedCache()`, and what does each order log on a cache hit? Read the "pipelines" section on Microsoft Learn.

✅ Done when every model call produces a log entry and the full request is readable at Trace.

### Deliverable 3: typed C# output (≈1 hour)
- [ ] **3.1 (you)** Design a job-posting record: title, company, location, a remote flag, required skills, and years of experience (nullable).
- [ ] **3.2** Use `client.GetResponseAsync<T>(...)`. Read `Result`, or use `TryGetResult` to avoid an exception when parsing fails.
- [ ] **3.3** Add an `/extract` command that reads a pasted job post or a file path and prints the record's fields. It doesn't stream.
- [ ] **3.4** Test on both providers. Check three things:
  - Does `TryGetResult` fail?
  - Are values invented for missing fields?
  - If a provider rejects the request, use the overload's `useJsonSchemaResponseFormat` parameter.

  Record what had to change.
- [ ] **Think about:** what typed output fixes compared with experiment 4, and what it doesn't fix. Remember the Niger answer.

✅ Done when a job post goes in and a typed record comes out, on both Llama and Haiku.

### Deliverable 4: prompt v1 vs five-part prompt v2 (≈1.5 hours)
- [ ] **4.1 (you)** Save five real job posts in `inputs/`:
  - a clear one
  - a messy one
  - one in French
  - one with no salary or experience info
  - one that's really two roles in one post
- [ ] **4.2 (you)** Write the correct ("golden") answer for each post by hand, before running anything.
- [ ] **4.3** Write prompt v1 in `prompts/extract-v1.txt`, a single line such as "Extract the job details from this posting."
- [ ] **4.4** Write prompt v2 in `prompts/extract-v2.txt` in five parts:
  - role
  - task
  - context
  - rules and edge cases: null and never guess, two roles in one post, the language for values
  - one example (few-shot)

  Both prompts load from files, so they change without recompiling.
- [ ] **4.5** Run both prompts on all five inputs on Haiku, and on Llama if there's time. For each run, record:
  - whether it parsed
  - fields matched against the golden answer
  - input and output tokens
  - cost
- [ ] **4.6** Write section 5 of `experiments.md`: an Expected line before running, the score table, and a conclusion. Was v2 better, by how much, and was the extra cost worth it?

✅ Done when section 5 has a scored table for both prompts on all five inputs, plus a conclusion.

## Tests
All manual (E2E, `dotnet run --project AiLab.Console`; switch provider and model in `AiLab.Console/appsettings.json`):
- **D1:** chats with Ollama, then with Anthropic/Haiku after changing only `appsettings.json`. `/reset`, `/exit`, Ctrl+C, the cost lines and the JSON check all behave as before. Running it from the repo root and from `AiLab.Console/` both find `appsettings.json`.
- **D2:** at Debug, each model call logs one entry. At Trace, the full message list is visible, including the system message after `/reset`.
- **D3:** `/extract` on one post prints a populated record on Llama and on Haiku. A malformed result is handled through `TryGetResult` without crashing.
- **D4:** section 5 has a scored row for each prompt × input (10 on Haiku), with parse result, matched fields, tokens and cost.

## Decisions & trade-offs
- `ChatClientFactory` stays the only place with provider logic, and registration calls it.
- Set the content root to `AppContext.BaseDirectory` through `HostApplicationBuilderSettings`. The host's default is the current directory, so `appsettings.json` would otherwise depend on where the app is started.
- Build the host and resolve services, but don't call `host.RunAsync()`. Its console lifetime would take over Ctrl+C, which currently cancels only the streaming turn.
- `/extract` is non-streaming and doesn't touch chat history.
- Develop on Ollama. Haiku is used only for the recorded runs (D1 smoke test, D3.4, D4.5); the expected spend is well under $0.01.

## Alternatives considered
- Pricing model ID: `ChatResponse.ModelId` vs `ChatClientMetadata.DefaultModelId` vs an options object. Decide after checking what each provider returns.
- Log noise: Debug on the console vs Trace to a file. Decide in 2.3.

## Out of scope
- Caching middleware (only the ordering question from D2).
- Automated tests and automated scoring. Scoring against the golden answers is manual.

## Changes
- **2026-10-21:** 1.1–1.3 done. `Program.cs` builds the host with `ContentRootPath = AppContext.BaseDirectory` (no manual `AddJsonFile`/`AddUserSecrets`), registers the client with `AddChatClient` and `ChatSession` as a singleton, and runs `ChatSession.RunAsync()` without `host.RunAsync()`. The loop lives in `Chat/ChatSession.cs`, which takes only `IChatClient`. Pricing model ID: `client.GetService<ChatClientMetadata>().DefaultModelId`, so the session reads no config. Both providers report the configured name (`llama3.2`, `claude-haiku-5-5`), which matches the price table keys. Tested on Llama: chat, history, `/reset` and cost lines work; started from the repo root and from `AiLab.Console/`; Anthropic starts with the key from user-secrets under the Development profile and fails with "Anthropic:ApiKey is missing" under Production. Still open: one real Haiku turn (D1 smoke test) and a manual Ctrl+C check.
- **2026-10-21:** Deliverable 2 done. `.UseLogging()` is chained onto `AddChatClient(...)`. `appsettings.json` sets `Logging:LogLevel` to `Warning` by default and `Debug` for `Microsoft.Extensions.AI`.
  - **2.3 noise:** the console logger writes on its own thread, so at Debug the entries land in the middle of the streamed reply (`AI: dbug: ... invoked. Hello...`). At Trace, one log entry is written per streamed chunk (192 log lines for two short turns). Choice: send all console logs to stderr (`Logging:Console:LogToStandardErrorThreshold = Trace`). Run with `2> ai.log` and the chat stays clean while the full log goes to a file, with no extra package. Debug stays the default level, so every call still logs something.
  - **2.4:** at Trace (`Logging__LogLevel__Microsoft.Extensions.AI=Trace`), each call logs the full message list, the options and the client metadata. After `/reset`, the next request held only the new user message. The JSON system message was off (`null`) in this run, so its survival wasn't re-checked here. The streamed updates also show `ChatResponse.ModelId` = `llama3.2` on Ollama, the same as `DefaultModelId`.
  - **Middleware order:** the first `Use...` call on the builder is the outermost layer. `.UseLogging().UseDistributedCache()` puts logging outside the cache, so cache hits are logged too and look like model calls. `.UseDistributedCache().UseLogging()` puts logging inside the cache, so only real calls to the model are logged and a cache hit logs nothing. Choose based on what the log is for: everything the app asked, or what actually reached the provider.
- **2026-10-21:** 2.3 changed: logs now go to a file instead of stderr. Even on stderr, the `dbug` entries still showed up inside the chat unless you remembered `2> ai.log`. Added Serilog (`Serilog.Extensions.Hosting`, `Serilog.Settings.Configuration`, `Serilog.Sinks.File`), registered with `builder.Services.AddSerilog(...)`, which replaces the console logger.
  - Logs roll daily to `logs/ai-YYYYMMDD.log` next to the app (`bin/Debug/net10.0/logs/`, already ignored by git). The path doesn't depend on where the app is started from, and the app prints the log folder at startup.
  - Levels moved from `Logging:LogLevel` to `Serilog:MinimumLevel` in `appsettings.json`: `Default` is `Warning`, with an override of `Debug` for `Microsoft.Extensions.AI`.
  - For full requests, set `Serilog__MinimumLevel__Override__Microsoft.Extensions.AI=Verbose`. Serilog's `Verbose` is the same as `Trace`.
  - Tested on Llama: the console shows only the chat. The file gets one `[DBG] invoked` and one `[DBG] completed` per call at Debug, and the full message list at Verbose.
