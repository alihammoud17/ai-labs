# ai-lab

A .NET 10 solution (a console chat app and an ASP.NET Core API, in Clean Architecture layers) for learning AI integration hands-on. Part of a ~12-week self-study plan to become an AI-enabled .NET specialist.

## About me

- .NET backend developer, 5 years of experience.
- Strong in C#, ASP.NET Core and SaaS architecture; new to LLM integration.
- Already studied: tokens, context windows, inference, sampling (temperature, top-p, top-k), hallucinations, reasoning models.

## Stack

- Solution `AiLab` (`AiLab.slnx`), .NET 10, Clean Architecture (references point inward only), six projects:
  - `AiLab.Domain`: no packages, no references. `DTOs/`, `Entities/`, `Outputs/`, `Responses/`, `Enums/`, `Constants/`, `Exceptions/`, `Settings/` (`ResilienceSettings`), `Interfaces/` (`ITicketAnalyzer`, `ITicketEval`, `IChangeLogRepository`, `ITestTicketStore`, `IJobFileStore`, `IEvalRunStore`, `IProviderErrorClassifier`).
  - `AiLab.Application` (→ Domain): flat `Services/` with all business logic, `AddApplication()`. Includes `StructuredReply.TryParse<T>` (typed parse with the ```` ```json ```` fence fallback), `ModelPrices`, `SessionCostTracker`, `ChatConversation`, `ChatExperimentSettings`.
  - `AiLab.Infrastructure` (→ Domain): provider SDKs, Polly, Serilog, files. `AddInfrastructure(ResilienceSettings? resilience = null)`, `Providers/ChatClientFactory`, `ProviderErrorClassifier`, `Repositories/InMemoryChangeLogRepository`, `Files/` (`TestTicketFiles`, `JobFileStore`, `FileEvalRunStore`), `Logging/FileLogging`.
  - `AiLab.Console` (→ Application, Infrastructure, Domain): the chat app (stages 1–2). `Chat/ChatSession` (loop, streaming, Ctrl+C), `Chat/JobCommands` (`/extract`, `/eval` parsing and output).
  - `AiLab.Api` (→ Application, Infrastructure, Domain): minimal API (stage 3), endpoints in `Endpoints/`, error handler in `Middleware/`, OpenAPI setup in `OpenApi/`, `POST /analyze-ticket`, on http://localhost:5028. Swagger UI at `/swagger` in Development. Test tickets A–D in `AiLab.Api/TestTickets/` (one JSON body each, plus `expected.json`); Swagger and `analyze-ticket.http` read them from there.
  - `AiLab.Api.Tests`: xUnit v3 integration tests, `WebApplicationFactory<Program>` with a `ScriptedChatClient` registered under the keyed service `"provider"`, so the real function-invocation and logging middleware run above the fake. `dotnet test` runs offline (no network, Ollama or key).
- `Microsoft.Extensions.AI`: all app code talks only to `IChatClient`.
- Providers are created in `AiLab.Infrastructure/Providers/ChatClientFactory.cs`, called only by the keyed `"provider"` singleton that `AddInfrastructure` registers:
  - `"ollama"` → `OllamaSharp` (`OllamaApiClient`), local and free, model `llama3.2`.
  - `"anthropic"` → official `Anthropic` NuGet (v10+), `.AsIChatClient(model)`, model `claude-haiku-5-5`.
- Console host (`AiLab.Console/Program.cs`): `AddInfrastructure()` (no resilience: plain clients, SDK defaults), `AddFileLogging(..., "ai")`, `AddApplication()`, then `AddChatClient(keyed provider).UseLogging()`. The host is built, not run, so Ctrl+C stays with the chat loop.
- API (`AiLab.Api/Program.cs`): `AddInfrastructure(resilience)`, `AddFileLogging(..., "api")`, `AddApplication()`. The keyed provider client is wrapped by `AddChatClient(...).UseFunctionInvocation(cap, IncludeDetailedErrors = false).UseLogging()`. Function invocation is outer, so every round trip is logged. `TicketAnalyzer` calls `GetResponseAsync<TicketAnalysis>` with the `TicketTools.GetRecentChanges` tool over the fake `InMemoryChangeLogRepository`. The tool's component list and the system prompt's are built from `IChangeLogRepository.GetComponents()`. The system prompt has today's date (`TimeProvider`).
- Defensive UX: the response is `notice` (fixed server text), `aiAnalysis` (the model's fields, including `causeVersion`), `verification` and `diagnostics`.
  - `verification` comes from `CauseCheck` over the tool results of the same request, which `TicketTools.ParseChanges` reads back. It holds `causeStatus` (grounded / unknown / unverified), a reason, `evidence`, `toolCalled` and `toolFailed`.
  - Grounded means "cites a version the tool returned", not "correct".
  - An unreadable or empty reply gets one repair call (tools defined, `ToolMode.None`).
  - Errors are `{ error, code }`: 400 validation problem / `invalid_request`, 422 `not_a_ticket` / `model_declined`, 502 `provider_unavailable` / `tool_failed` / `reply_cut_off` / `reply_unreadable`, 504 `timeout`, 500 `internal_error`.
  - `ChangeLog:FailComponents` makes the fake repository throw for testing. One tool failure gives a 200 with `toolFailed`.
  - Failure table: `docs/failure-table.md` (Detected by and Handled by columns).
- Ticket eval: `POST /dev/eval-tickets?runs=3&maxCost=0.03` (Development only), scores each reply against `TestTickets/expected.json` from the model's `causeVersion` (expected → right, other → invented, none with an "unknown" cause → unknown; the cause text only as a fallback) and shows `causeStatus`. Raw replies in `eval-runs/*_tickets_*`.
- API resilience (`Resilience` section of `AiLab.Api/appsettings.json`): one retry layer per provider. Polly's standard handler on the Ollama `HttpClient`, the SDK's own `Timeout`/`MaxRetries` for Anthropic. The SDK honours `ANTHROPIC_BASE_URL`, so it can be pointed at a fake local server for free retry tests. `MaxToolIterations` 3 allows up to 4 model calls (the last without tools). Request timeout 240 s via the request timeouts middleware (skipped when a debugger is attached).
- Provider and model: `Chat:Provider` / `Chat:Model` in each project's `appsettings.json` (the API also has `Chat:OllamaUrl`). Keep it on Ollama; for a one-off Anthropic run, override with the env vars `Chat__Provider` / `Chat__Model`.
- Logging: Serilog to a daily file in each project's `bin/Debug/net10.0/logs/` (`ai-` for the console, `api-` for the API). Levels under `Serilog:MinimumLevel`; `Verbose` shows full requests and per-call usage.
- Typed extraction: `JobPosting` (Domain), `JobExtractor`, `JobExtraction`, `JobScorer` (Application; parsing via `StructuredReply`), `/extract [v1|v2] <path>`. Inputs and golden answers in `inputs/`, prompts in `prompts/`. Run the app from the repo root.
- Repeated scored runs: `/eval <v1|v2> [runs]` (default 3, `JobEval`) over every `inputs/job-N.txt` with a golden answer; raw replies in the repo's `eval-runs/` (git-ignored) for offline re-scoring.
- Chat experiment switches (`ChatOptions`, `JsonSystemMessage`, `MaxHistoryMessages`): `AiLab.Application/Services/ChatExperimentSettings.cs`.
- Secrets: `dotnet user-secrets`, key `Anthropic:ApiKey`. The console and API share one `UserSecretsId`. Never put keys in code or appsettings. They're only loaded in Development (`Properties/launchSettings.json` sets it).
- Haiku 5.5 quirks found so far: only the default temperature (1.0) is accepted; it thinks by default and the thinking counts toward `MaxOutputTokens`; it ignored the native JSON-schema response format, so `JobExtractor` uses `useJsonSchemaResponseFormat: false`, and it may fence JSON in ```` ```json ```` (it fenced every ticket analysis in stage 3 until the prompt said "no code fences").

## Prices (per 1M tokens, checked 2026-10-19)

| Model | Input | Output |
|---|---|---|
| claude-haiku-5-5 | $0.10 | $0.50 |
| claude-sonnet-5-5 | $2.00 | $10.00 |
| llama3.2 (Ollama) | $0 | $0 |

## Stage 1 (done): chat loop + ChatOptions experiments

1. [x] Chat loop with history (`List<ChatMessage>`, `/exit`)
2. [x] Streaming with `GetStreamingResponseAsync`
3. [x] Usage → cost per turn and session total (add the Anthropic adapter before this step)
4. [x] ChatOptions experiments, recorded in `experiments.md`:
   - Temperature 0 / 0.5 / 1.0, same prompt 3x each
   - Tiny `MaxOutputTokens` → check `FinishReason`
   - Format-forcing system message (JSON only), count parse failures

## Stage 2 (done): DI, logging, typed output, prompt evaluation

Merged into `main` (PR #1). Plan: `docs/plans/2026-10-21-stage-2-di-logging-structured-output.md`.

1. [x] Same code on 2 providers: Generic Host + `AddChatClient`, chat loop moved into `ChatSession`
2. [x] `UseLogging` middleware, log levels in `appsettings.json`
3. [x] Typed output with `GetResponseAsync<T>`, `/extract` for job posts
4. [x] Prompt v1 vs five-part v2 on 3 real job posts, scored against golden answers → section 5 of `experiments.md`

Loose ends (done, plan: `docs/plans/2026-10-22-stage-2-loose-ends-eval.md`):

- [x] Two edited test posts: `job-4` (no work mode, no years: "never guess") and `job-5` (two roles: "extract the first role")
- [x] Repeated runs with `/eval`, 3 per prompt on both models → section 6 of `experiments.md` (Haiku: fields gap real but one rule, skills gap noise)

## Stage 3 (done): tool calling in ASP.NET Core

Plan: `docs/plans/2026-10-22-stage-3-tool-calling.md`. Built on branch `stage-3-tool-calling`, merged into `main`. For this stage Claude writes the code and explains each change (overrides tutor mode).

1. [x] `POST /analyze-ticket` returning a summary, a probable cause and test cases
2. [x] One tool backed by a fake repository, invoked automatically by the model
3. [x] DI registration of `IChatClient`
4. [x] Timeouts and retries configured

Results: section 7 of `experiments.md` (both models used the tool correctly; Haiku said "unknown" on B and D, Llama invented a cause).

Loose ends (done, plan: `docs/plans/2026-10-23-stage-3-loose-ends.md`):

- [x] Branch wording, tool component list from the repository, test tickets in one place
- [x] Ticket eval and two prompt lines (no fences; unknown unless a change explains the symptom) → section 8 of `experiments.md` (Haiku 12/12 and no fences; Llama inventions 6/6 → 4/6)
- [x] Anthropic retries and attempt timeout proven against a fake local server

Failure table: `docs/failure-table.md`, 30 potential production failures of `POST /analyze-ticket` with how each is detected today. Half are detected by nothing, mainly bad answers returned as 200 (made-up cause, missing fields), no auth, prompt injection, and Ollama silently cutting long prompts.

## Defensive UX (done): grounding check, clean failures, integration tests

Plan: `docs/plans/2026-10-23-defensive-ux-failure-handling.md`. Claude wrote the code (overrides tutor mode).

- [x] Test harness: keyed provider client, `AiLab.Api.Tests` with a scripted fake
- [x] Grounding check (attribution): `causeVersion`, `causeStatus`, server-built `evidence`
- [x] Set expectations and efficient dismissal: notice, `aiAnalysis` vs `verification`, plain Swagger text, error codes
- [x] Familiar formats: bug-report summary, Given/When/Then test cases
- [x] Cut-off replies, meaning checks, today's date, one JSON repair, 422 for non-tickets, tool failures flagged
- [x] Scorer order fixed, 43 tests pass offline

Results: section 9 of `experiments.md`. On Llama: A unverified (text copied without its version), C grounded and right, B and D grounded on real but unrelated changes (the check's known limit). Llama never filled `notATicketReason`; a prompt line for it caused a false 422 on B and was reverted. Haiku check (3 runs, $0.009): 12/12 correct, A and C grounded, B and D unknown, no false alarms, both non-tickets 422, no real ticket refused. The eval scorer now reads `causeVersion` first (text only as a fallback). Failure-table rows handled: 10, 13, 14, 15, 18, 20; partly 12, 16, 23.

## Clean Architecture restructure (done)

Plan: `docs/plans/2026-10-23-clean-architecture-restructure.md`. Claude wrote the code. Structural only: no route, prompt or response changes.

- [x] `AiLab.Core` split into `AiLab.Domain`, `AiLab.Application` and `AiLab.Infrastructure`; the Api and Console are hosts only
- [x] `ChatSession` split into `ChatConversation`, `SessionCostTracker`, `ChatExperimentSettings`, `JobExtraction`, `JobEval` (Application) and `ChatSession`, `JobCommands` (Console)
- [x] Checked against Ollama transcripts from before the move (`eval-runs/restructure-baseline/`, git-ignored); the OpenAPI document is byte-identical; 43 tests pass

**Current step:** Stage 4 (RAG) — week of 2026-10-26.

## Stage 4: embeddings, pgvector, first RAG

Tutor mode applies: I write chunking, retrieval and prompt assembly myself.
Claude may write Docker, DI wiring and test scaffolding.

1. [ ] Mon: similarity matrix with IEmbeddingGenerator (nomic-embed-text via Ollama)
2. [ ] Tue: AiLab.Rag (a separate project referencing Domain, Application and Infrastructure) + pgvector via Microsoft.Extensions.VectorData, top-5 query with scores
3. [ ] Wed: ingest data/corpus (fixed-size vs heading chunker), metadata, content hash to skip unchanged chunks
4. [ ] Thu: POST /ask with numbered sources, typed answer, citation check against retrieved IDs, length limit, rate limiter
5. [ ] Fri: 10 questions, failure log (retrieval miss / wrong chunk / generation error / should have refused) → experiments.md section 10

Watch: Ollama context size (silent truncation), log prompt tokens per /ask.

## How to help me (tutor mode)

- I'm learning. Don't write the full solution unless I explicitly ask for it.
- Explain the concept and why it matters, then give hints: API names, docs to read, what to check.
- Review my code and point at problems rather than rewriting it.
- Develop and debug against Ollama to keep costs at zero; use Anthropic only for recorded experiment runs.
- Keep explanations short and plain.

## Conventions

- Commit after each completed step.
- Record every experiment result in `experiments.md` (expected vs actual, tokens, cost).
