# Stage 2: DI, logging middleware, typed output, prompt v1 vs v2

- **Date:** 2026-10-21
- **Branch:** stage-2-di-logging-structured-output
- **Goal:** The same chat code runs on two providers through DI. Every model call is logged. Job posts are extracted into a typed C# record, and a one-line prompt is scored against a five-part prompt.
- **Status:** planned
- **Approval:** approved in plan mode

Steps marked **(you)** are hands-on work only you can do.

## Steps

### Deliverable 1: same code on two providers (≈1 hour)
- [ ] **1.1** Add `Microsoft.Extensions.Hosting`. Replace `ConfigurationBuilder` with `Host.CreateApplicationBuilder(args)`. It only loads user-secrets in Development, so set `DOTNET_ENVIRONMENT=Development` in `Properties/launchSettings.json`. Without it, the Anthropic key is silently missing.
- [ ] **1.2** Register the client with `builder.Services.AddChatClient(sp => ...)`. Read the provider and model from config, then call the existing `ChatClientFactory`. Build the host and get the client with `GetRequiredService<IChatClient>()`.
- [ ] **1.3** Move the chat loop into a `ChatSession` class. It takes `IChatClient` (and later `ILogger`) in its constructor. Register it and resolve it from DI.
- [ ] **Think about:** where the pricing model ID comes from now. `ChatSession` needs it for `ModelPrices.Cost` but shouldn't know the provider. Options are `ChatResponse.ModelId`, `ChatClientMetadata.DefaultModelId`, or passing it through options.

✅ Done when changing only `appsettings.json` switches Llama ↔ Claude, with no `if (provider == ...)` outside the registration.

### Deliverable 2: `UseLogging` middleware (≈45 min)
- [ ] **2.1** Chain `.UseLogging()` onto the `ChatClientBuilder` that `AddChatClient(...)` returns. It picks up `ILoggerFactory` from DI.
- [ ] **2.2** Set the log level for the `Microsoft.Extensions.AI` category under `Logging:LogLevel` in `appsettings.json`. `Debug` shows basic call info and `Trace` shows full message content.
- [ ] **2.3** Deal with the noise of logs mixed into streamed output. Either keep the level at Debug or log to a file. Pick one and note why.
- [ ] **2.4** Run one turn and read the log. For example, check whether `/reset` kept the system message.
- [ ] **Think about:** middleware order. Should logging go before or after a future `.UseDistributedCache()`, and what does each order log on a cache hit? Read the "pipelines" section on Microsoft Learn.

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
