# Clean Architecture restructure of ai-labs

- **Date:** 2026-10-23
- **Branch:** `clean-architecture-restructure` (from `main`)
- **Goal:** Move every file into the Domain / Application / Infrastructure / Presentation layers you use in MultiTenantSaaSApi. Structural refactor only: no route, prompt, response-shape or behaviour changes, apart from the agreed exceptions under Decisions & trade-offs.
- **Status:** in-progress
- **Approval:** approved in plan mode (2026-10-23)

## Context

The code is hard to read because it isn't organised the way you work. Today:
- `AiLab.Core` mixes provider adapters with parsing and pricing logic.
- `ChatSession.cs` (506 lines) mixes the UI loop, file IO, eval logic and cost tracking.
- The Api project holds business logic, IO and the fake repository side by side.

Verified against MultiTenantSaaSApi (GitHub tree, 2026-10-23): `Domain/{Entities, DTOs, Interfaces, Outputs, Responses, Authorization(constants), Identity}`, `Application/Services/` flat, `Infrastructure/{Data, Repositories}`, `Presentation/{Controllers, Middleware, …}`. Where it differs from your written rules, this plan follows the written rules. Two differences:
- Its `Application.csproj` references `Infrastructure` and uses `ApplicationDbContext` directly.
- It has no `AddApplication` or `AddInfrastructure`; everything is registered inline in `Program.cs`.

## Steps

Do this on branch `clean-architecture-restructure`. After **every** step, `dotnet build` must show 0 warnings (the baseline) and `dotnet test` must show **43/43**. Commit after each step (repo convention). Use `git mv` for every moved file; for a split file, `git mv` the largest part and create the others. Before starting the Api, check port 5028 (you may be running it).

- [x] **0. Baseline.** Record `dotnet build AiLab.slnx` and `dotnet test` (43). On Ollama, save a console transcript to the scratchpad: `/extract v2 inputs/job-1.txt`, `/eval v1 1`, one chat turn, `/reset`, Ctrl+C during a reply. Also POST ticket C to the Api.
  - Done 2026-10-23 on `clean-architecture-restructure`:
    - Build: 0 warnings. Tests: 43/43.
    - Copies of the transcripts and the driver script are in `eval-runs/restructure-baseline/` (git-ignored). Rerun with `bash eval-runs/restructure-baseline/console-transcript.sh AiLab.Console/bin/Debug/net10.0/AiLab.Console.dll <out>` from the repo root.
    - The Api returned 200 for ticket C, with Swagger examples A–D. The log has "GetRecentChanges called …", "Ticket analyzed …" and "Cause …" at Information.
    - Ctrl+C on Ollama ends the stream without an `OperationCanceledException`, so it prints `Tokens: unknown`, not `[reply cancelled, turn discarded]`. That's current behaviour and has to be kept.
- [x] **1. Empty projects.** `dotnet new classlib -f net10.0` for `AiLab.Domain`, `AiLab.Application` and `AiLab.Infrastructure`; delete `Class1.cs`; `dotnet sln AiLab.slnx add …`; add the references (C) and packages (D). Nothing uses them yet.
- [x] **2. Domain.** Move or split every Domain row from A. `AiLab.Api` and `AiLab.Console` reference `AiLab.Domain`.
  - Using updates in `AiLab.Api`: `Program`, `TicketAnalyzer`, `TicketAnalysisRules`, `TicketTools`, `CauseCheck`, `ToolEvidence`, `TicketEval`, `TicketScorer`, `InMemoryChangeLogRepository`.
  - In `AiLab.Console`: `ChatSession`, `JobExtractor`, `JobScorer`.
  - In the tests: `AnalyzeTicketTests` (`AiLab.Domain.Entities`, `.Responses`), `TicketToolsTests` (`.Entities`), `TicketScorerTests` (`.Enums`).
- [x] **3. Infrastructure plus host infrastructure wiring.**
  - `git mv` `ChatClientFactory` (Core), `InMemoryChangeLogRepository` (add the `Microsoft.Extensions.Configuration` using) and `TestTicketFiles` (made an instance class with `ReadExpected`).
  - Add the new ports (F) and their adapters, `FileLogging` and `AddInfrastructure`. `FileEvalRunStore` looks for `"AiLab.slnx"`, as `TicketEval` does today.
  - In the Api: `TicketAnalyzer` takes `IProviderErrorClassifier`; `TicketEval` takes `ITestTicketStore` and `IEvalRunStore`; `AnalyzeTicketExamples` takes `ITestTicketStore`.
  - Both `Program.cs` files switch to `AddInfrastructure` and `AddFileLogging`. The console switches to `AddChatClient(keyed).UseLogging()`.
  - Remove Anthropic and OllamaSharp from Core, Serilog from both hosts, and Http.Resilience from the Api. The hosts reference Infrastructure.
  - Extra check: start the Api on Ollama, POST ticket C, confirm the log file `api-*.log` and the Swagger examples. `POST /dev/eval-tickets?runs=1` writes to `<repo>/eval-runs/…_tickets_llama3.2`.
- [x] **4a. Application, ticket side.**
  - `git mv` `TicketAnalyzer`, `TicketAnalysisRules`, `TicketTools`, `ToolEvidence`, `CauseCheck`, `Versions`, `TicketEval` and `TicketScorer` (from the Api), and `StructuredReply` and `ModelPrices` (from Core), all into `Application/Services`.
  - Add `ITicketAnalyzer`, `ITicketEval` and `AddApplication`. The endpoints resolve the interfaces.
  - Add `using Microsoft.Extensions.Logging;` in `TicketAnalyzer`, `TicketTools` and `TicketEval`.
  - The Console references Application (for `StructuredReply` and `ModelPrices`).
  - `AiLab.Core` is now empty: remove it from the slnx and `git rm` it.
  - Test usings: `AiLab.Application.Services`. `AiLab.Api.Tests.csproj` gets explicit references to Application, Infrastructure and Domain (C).
  - Extra check: the Api log still shows "GetRecentChanges called …" and "Cause grounded …" at Information.
  - Done 2026-10-23:
    - Build: 0 warnings. Tests: 43/43.
    - The Information lines are still logged under the new `AiLab.Application.Services.*` categories; the `"AiLab"` override covers them.
    - On Ollama, ticket C returned a false 422 once in 6 requests: Llama set `notATicketReason` to "This is a support ticket requesting help with a problem." That comes from the unchanged rule that any non-empty reason means "not a ticket", not from the move. The other 5 requests returned 200.
- [ ] **4b. Application, console side.**
  - `git mv` `JobExtractor` and `JobScorer` into Application, then split `JobScorer`.
  - Add `SessionCostTracker`, `ChatConversation`, `ChatExperimentSettings`, `JobExtraction`, `JobEval` and the Domain outputs.
  - Reduce `ChatSession` to UI, and add `JobCommands` and `TurnCancellation`.
  - Extra check: rerun the step-0 transcript on Ollama. Every line format must be the same, except the agreed absolute "replies saved to …" path. The numbers can differ (model variance).
- [ ] **5. Api presentation.** Move the endpoints, error handler and OpenAPI document out of `Program.cs` into `Endpoints/`, `Middleware/` and `OpenApi/` (A).
- [ ] **6. Docs** (H).

## Analysis (verified 2026-10-23)

### Projects (all `net10.0`, Nullable and ImplicitUsings on)

| Project | SDK | References | Packages |
|---|---|---|---|
| `AiLab.Core` | classlib | none | Anthropic 12.58.0, Microsoft.Extensions.AI 10.11.0, OllamaSharp 5.5.0 |
| `AiLab.Console` | Exe | Core | M.E.Configuration.Json 10.0.13, M.E.Configuration.UserSecrets 10.0.13, M.E.Hosting 10.0.13, Serilog.Extensions.Hosting 10.0.0, Serilog.Settings.Configuration 10.0.1, Serilog.Sinks.File 7.0.0. UserSecretsId `081e3468-5d37-4a03-8004-d2e99d582578` |
| `AiLab.Api` | Web | Core | Microsoft.AspNetCore.OpenApi 10.0.13, M.E.Http.Resilience 10.11.0, Serilog.Extensions.Hosting / Settings.Configuration / Sinks.File (same versions), Swashbuckle.AspNetCore.SwaggerUI 10.3.1. Same UserSecretsId. `TestTickets\*.json` copied to output |
| `AiLab.Api.Tests` | Exe (xUnit v3) | Api | Microsoft.AspNetCore.Mvc.Testing 10.0.4, Microsoft.NET.Test.Sdk 18.3.0, xunit.v3 3.2.2, xunit.runner.visualstudio 3.1.5 |

**Test count: 43, not 37.** There are 17 `[Fact]`s in `AnalyzeTicketTests`, 21 `[InlineData]` cases in `TicketScorerTests` and 4 + 1 in `TicketToolsTests`. The week-2 review also says "43 of 43 pass". The gate in this plan is **43**, and the "37 tests" in CLAUDE.md is stale.

### Mixed responsibilities found
- **`ChatSession.cs`** does all of these in one class:
  - UI: loop, printing, Ctrl+C.
  - History: add, trim, reset, discard.
  - Cost: `_sessionTotal`, `AddToSessionTotal`.
  - File IO: prompts, inputs, `Directory.GetFiles`, eval-runs writes.
  - Eval logic: discovery, run loop, stop rules, pass aggregation in `PrintEvalTotals`.
  - The JSON-mode validity check.
  - The experiment knobs.
- **`JobScorer.cs`** has scoring logic, file IO (`LoadExpected`) and console printing (`PrintScore`).
- **Api `Program.cs`** inlines:
  - the `/analyze-ticket` endpoint with the outcome→status mapping (`ModelCallException`→502, `TicketRejectedException`→422);
  - the `/dev/eval-tickets` endpoint;
  - the exception handler (error-body mapping);
  - the OpenAPI document transformer;
  - the Polly setup;
  - the keyed provider factory;
  - the Serilog setup.
- **`TicketAnalyzer`** builds the prompt, calls the model, repairs, reads the reply and builds diagnostics. That's one cohesive use case and the rules are already in `TicketAnalysisRules`, so it is **not split further**. The one real violation: `IsProviderFailure` names `OllamaException`, `AnthropicException` and Polly's `ExecutionRejectedException`, so business logic depends on the provider SDKs.
- **`TicketEval`** does file IO (`TestTicketFiles.ReadAll`, `File.ReadAllText(expected.json)`, `RepoRoot()` looking for `AiLab.slnx`, `Directory.CreateDirectory`, `File.WriteAllTextAsync`).
- **Duplicated between the hosts:**
  - provider config reads (`Chat:Provider` and `Chat:Model` with the same throw messages);
  - the Serilog file-sink setup (only the prefix differs);
  - the "Logs:" line;
  - the content-root setup;
  - cost pricing (`ModelPrices.Cost` in `ChatSession` and in `TicketAnalyzer.Diagnostics`; already shared through Core, no duplicate code);
  - two eval implementations, job and ticket. They are different domains and stay separate; only the run-folder IO is shared.

### Current wiring
- **Console:** `Host.CreateApplicationBuilder(ContentRootPath = AppContext.BaseDirectory)`, then `AddChatClient(sp => ChatClientFactory.Create(provider, model, key)).UseLogging()`. No Polly, and the Anthropic SDK defaults apply (10 min per attempt, 2 retries). Serilog writes `BaseDirectory/logs/ai-.log` daily. `JobExtractor` and `ChatSession` are singletons. The host is built, not run.
- **Api:**
  - `ResilienceSettings` is bound from `Resilience`.
  - Named `HttpClient("ollama")` with `Timeout = Infinite`, plus `AddStandardResilienceHandler`: attempt timeout, retries, total = AllAttempts + 20 s, circuit-breaker sampling = 2 × attempt.
  - The keyed singleton `"provider"` calls `ChatClientFactory.Create(..., ollamaHttpClient only if ollama, ollamaUrl, anthropicAttemptTimeout, anthropicMaxRetries)`.
  - `AddChatClient(keyed).UseFunctionInvocation(cap, IncludeDetailedErrors=false).UseLogging()`.
  - Request timeout policy `analyze-ticket` (504 + `ErrorResponse`).
  - Serilog writes `api-.log`.
  - Singletons: `TimeProvider.System`, `IChangeLogRepository`, `TicketTools`, `TicketAnalyzer`, `TicketEval`.
  - JSON options: strict numbers, camelCase enums.
  - OpenAPI plus Swagger UI (dev only).
  - The client is resolved eagerly, and a warning is logged if the request timeout ≤ AllAttempts.

## A. File mapping

Kinds: DTO = record/DTO, IF = interface, BL = business logic, IO = provider/IO access, CFG = configuration/options, HOST = host/UI, EP = endpoint, TEST = test helper. "split" marks a file whose types go to more than one place; the largest part keeps the `git mv` history.

**AiLab.Core** (the project is deleted after step 4a)

| Current | Target | Namespace | Kind | Why |
|---|---|---|---|---|
| `Providers/ChatClientFactory.cs` | `AiLab.Infrastructure/Providers/ChatClientFactory.cs` | `AiLab.Infrastructure.Providers` | IO | Creates SDK clients; the only code that knows the providers |
| `Pricing/ModelPrices.cs` (`ModelPrice` + `ModelPrices`) | `AiLab.Application/Services/ModelPrices.cs` | `AiLab.Application.Services` | BL | Cost calculation. `ModelPrice` stays in the same file: it's the table's row type with no other user |
| `Parsing/StructuredReply.cs` | `AiLab.Application/Services/StructuredReply.cs` | `AiLab.Application.Services` | BL | Pure parsing over `ChatResponse<T>` |
| `AiLab.Core.csproj` | deleted (`git rm`) | n/a | n/a | Empty after the moves |

**AiLab.Api**

| Current | Target | Namespace | Kind | Why |
|---|---|---|---|---|
| `ChangeLog/ChangeEntry.cs` | `AiLab.Domain/Entities/ChangeEntry.cs` | `AiLab.Domain.Entities` | DTO | Entity |
| `ChangeLog/IChangeLogRepository.cs` | `AiLab.Domain/Interfaces/IChangeLogRepository.cs` | `AiLab.Domain.Interfaces` | IF | Port; moves unchanged |
| `ChangeLog/ChangeLogUnavailableException.cs` | `AiLab.Domain/Exceptions/ChangeLogUnavailableException.cs` | `AiLab.Domain.Exceptions` | DTO | Shared exception type |
| `ChangeLog/InMemoryChangeLogRepository.cs` | `AiLab.Infrastructure/Repositories/InMemoryChangeLogRepository.cs` | `AiLab.Infrastructure.Repositories` | IO | Repository. Needs an explicit `using Microsoft.Extensions.Configuration;` (no Web implicit usings) |
| `Tickets/AnalyzeTicketRequest.cs` | `AiLab.Domain/DTOs/AnalyzeTicketRequest.cs` | `AiLab.Domain.DTOs` | DTO | Request DTO; `Validate()` stays on it, unchanged |
| `Tickets/AnalyzeTicketResponse.cs` **split** | `AiLab.Domain/Responses/AnalyzeTicketResponse.cs` (`AnalyzeTicketResponse`, `AnalysisDiagnostics`, `ToolCall`) + `AiLab.Domain/Responses/ErrorResponse.cs` | `AiLab.Domain.Responses` | DTO | Response models. `ErrorResponse` gets its own file because every error path uses it |
| `Tickets/TicketAnalysis.cs` | `AiLab.Domain/Outputs/TicketAnalysis.cs` | `AiLab.Domain.Outputs` | DTO | The model's typed output; its `[Description]`s are the schema. Attributes and text are unchanged |
| `Tickets/ModelCallException.cs` **split** | `Domain/Exceptions/ModelCallException.cs`, `Domain/Exceptions/TicketRejectedException.cs`, `Domain/Constants/ErrorCodes.cs` | `AiLab.Domain.Exceptions`, `AiLab.Domain.Constants` | DTO / const | One type per file. This also covers the file split in review finding L13 |
| `Tickets/TicketAnalysisRules.cs` | `AiLab.Application/Services/TicketAnalysisRules.cs` | `AiLab.Application.Services` | BL | Meaning checks |
| `Tickets/TicketAnalyzer.cs` **split** | `Application/Services/TicketAnalyzer.cs` (implements `ITicketAnalyzer`) + `Domain/Enums/RunOutcome.cs` + `Domain/Outputs/TicketRun.cs` | `AiLab.Application.Services`, `.Domain.Enums`, `.Domain.Outputs` | BL | Use case. **Change:** `IsProviderFailure` becomes `errors.IsProviderFailure(ex) \|\| ex is OperationCanceledException && !cancellationToken.IsCancellationRequested`, with `IProviderErrorClassifier` injected. That's equivalent because none of the four SDK types is an `OperationCanceledException`. The usings `Anthropic.Exceptions`, `OllamaSharp.Models.Exceptions` and `Polly` are removed; `using Microsoft.Extensions.Logging;` is added |
| `Tools/TicketTools.cs` | `AiLab.Application/Services/TicketTools.cs` | `AiLab.Application.Services` | BL | The tool's logic and parser over a port. Stays concrete: the tests call `new TicketTools(repo, NullLogger)` and the static `ParseChanges`. Add `using Microsoft.Extensions.Logging;` |
| `Grounding/CauseCheck.cs` **split** | `Application/Services/CauseCheck.cs` + `Domain/Outputs/CauseVerification.cs` (`CauseVerification`, `Evidence`) + `Domain/Enums/CauseStatus.cs` | as named | BL / DTO / enum | Rule logic vs the response records |
| `Grounding/ToolEvidence.cs` | `AiLab.Application/Services/ToolEvidence.cs` | `AiLab.Application.Services` | BL | Reads tool results out of `ChatMessage`s |
| `Grounding/Versions.cs` | `AiLab.Application/Services/Versions.cs` | `AiLab.Application.Services` | BL | Version regex rules |
| `Evaluation/TicketEval.cs` | `AiLab.Application/Services/TicketEval.cs` (implements `ITicketEval`) | `AiLab.Application.Services` | BL | **Change:** reads tickets and expected answers from `ITestTicketStore`; creates the folder and writes files through `IEvalRunStore`. `RepoRoot()` moves to Infrastructure. Report text and file names are unchanged. Add `using Microsoft.Extensions.Logging;` |
| `Evaluation/TicketScorer.cs` **split** | `Application/Services/TicketScorer.cs` + `Domain/Enums/Verdict.cs` | as named | BL / enum | |
| `Evaluation/TestTicketFiles.cs` | `AiLab.Infrastructure/Files/TestTicketFiles.cs` | `AiLab.Infrastructure.Files` | IO | Becomes an instance class implementing `ITestTicketStore`. Gains `ReadExpected()`, taken from `TicketEval`. `Folder` is still `AppContext.BaseDirectory/TestTickets` |
| `OpenApi/AnalyzeTicketExamples.cs` | `AiLab.Api/OpenApi/AnalyzeTicketExamples.cs` | `AiLab.Api.OpenApi` | HOST | `Apply(operation, ITestTicketStore tickets, ct)`; the transformer passes `context.ApplicationServices.GetRequiredService<ITestTicketStore>()`. Example JSON is unchanged |
| `ResilienceSettings.cs` | `AiLab.Domain/Settings/ResilienceSettings.cs` | `AiLab.Domain.Settings` | CFG | Used by Infrastructure (Polly, SDK) and the Api host (cap, request timeout). Domain is the only place both can see without the host depending on Infrastructure types |
| `Program.cs` **split** | `AiLab.Api/Program.cs` (composition) + `Endpoints/AnalyzeTicketEndpoint.cs` (`MapAnalyzeTicket()`: route, validation, exception→status, OpenAPI metadata, `.WithRequestTimeout`) + `Endpoints/DevEndpoints.cs` (`MapDevEndpoints()`: `/dev/eval-tickets`) + `Middleware/ErrorResponses.cs` (`UseErrorResponses()`: the `UseExceptionHandler` block) + `OpenApi/OpenApiDocument.cs` (`AddTicketApiDocument()`: the document transformer) | `AiLab.Api`, `.Api.Endpoints`, `.Api.Middleware`, `.Api.OpenApi` | HOST / EP | Program becomes wiring only. Polly, the keyed provider and Serilog go to Infrastructure (E). Route text, descriptions and status codes are copied as they are |
| `Properties/launchSettings.json` | `AiLab.Api/Properties/launchSettings.json` | n/a | CFG | Profile `http` unchanged |
| `appsettings.json` | `AiLab.Api/appsettings.json` | n/a | CFG | Unchanged. The `"AiLab"` Serilog override still matches the new `AiLab.Application.*` and `AiLab.Infrastructure.*` logger categories |
| `TestTickets/*.json`, `analyze-ticket.http` | `AiLab.Api/` (same relative places) | n/a | data | `.http` uses `./TestTickets/...` and keeps working. The csproj keeps `CopyToOutputDirectory` |
| `AiLab.Api.csproj` | `AiLab.Api/AiLab.Api.csproj` | n/a | n/a | References and packages: see C and D |

**AiLab.Console**

| Current | Target | Namespace | Kind | Why |
|---|---|---|---|---|
| `Extraction/JobPosting.cs` **split** | `Domain/DTOs/JobPosting.cs` + `Domain/Enums/WorkMode.cs` | `AiLab.Domain.DTOs`, `.Domain.Enums` | DTO / enum | `[Description]` and `[JsonConverter]` are BCL attributes, so no packages are needed |
| `Extraction/JobExtractor.cs` | `AiLab.Application/Services/JobExtractor.cs` | `AiLab.Application.Services` | BL | Unchanged |
| `Extraction/JobScorer.cs` **split** | `Application/Services/JobScorer.cs` (`ExpectedPathFor`, `Score`, `CompareField`) + `Domain/Outputs/JobScore.cs` (`JobScore`, `FieldResult`) + `LoadExpected` → `Infrastructure/Files/JobFileStore.cs` + `PrintScore`/`Show` → `Console/Chat/JobCommands.cs` | as named | BL / DTO / IO / HOST | Separates scoring, IO and printing. The `using Console = System.Console` alias leaves the Application file |
| `Chat/ChatSession.cs` **split** (see below) | `Console/Chat/ChatSession.cs`, `Console/Chat/JobCommands.cs`, `Console/Chat/TurnCancellation.cs`, `Application/Services/{ChatConversation, ChatExperimentSettings, SessionCostTracker, JobExtraction, JobEval}.cs`, `Domain/Outputs/{ChatTurnResult, JobExtractionOutputs, JobEvalOutputs}.cs` | as named | HOST / BL / DTO | |
| `Program.cs` | `AiLab.Console/Program.cs` | top-level | HOST | Wiring only (E) |
| `appsettings.json`, `Properties/launchSettings.json` | `AiLab.Console/…` | n/a | CFG | Unchanged |

**How ChatSession is split.** Output text and its order stay identical.
- **`ChatExperimentSettings`** (Application, static): `MaxHistoryMessages = 20`, `JsonSystemMessage` (null, with the commented string kept) and `CreateOptions()`, which returns the `ChatOptions` with the commented `Temperature` and `MaxOutputTokens` lines. All existing comments are kept.
- **`SessionCostTracker`** (Application, singleton): takes `IChatClient`; `Model` comes from `ChatClientMetadata`. It has `Total` and `decimal? Add(ChatResponse)`, the old `AddToSessionTotal` body. One shared total, as today.
- **`ChatConversation`** (Application, singleton): takes `IChatClient` and `SessionCostTracker`. It owns `_history`, seeded with the system message when `JsonSystemMessage` is set.
  - `Reset()`.
  - `StartTurn(input, ct)`: adds the user message and returns `client.GetStreamingResponseAsync(_history, options, ct)`.
  - `DiscardTurn()`.
  - `CompleteTurn(updates) → ChatTurnResult`:
    1. `ToChatResponse`;
    2. cost, only when tokens are known;
    3. cut-off flag;
    4. empty → discard;
    5. JSON check when JSON mode is on;
    6. `AddMessages`;
    7. trim loop.
  - The same steps in the same order as `RunTurnAsync`.
- **`ChatTurnResult`** (Domain): `InputTokens?`, `OutputTokens?`, `FinishReason` (string), `TurnCost?`, `SessionTotal`, `Model`, `CutOff`, `Empty`, `JsonValid?`, `JsonError?`. `FinishReason` is a string so Domain doesn't need Microsoft.Extensions.AI; it prints the same.
- **`JobExtraction`** (Application): the non-UI parts of `ExtractAsync`, mirroring today's flow so exceptions surface at the same points.
  - `LoadAsync(version, path)` → `JobExtractionInput`, which holds either the prompt and posting text, or a "prompt not found" or "file not found" full path.
  - `Evaluate(response, path)` → `JobExtractionResult`: the parsed job, `StrippedFence`, `CutOff`, the expected path, the loaded golden answer and its error.
  - The console still wraps only the model call in try/catch, as today.
- **`JobEval`** (Application): `EvalAsync` minus printing.
  - `PrepareAsync(version)` → `JobEvalPlan`: the missing-prompt path, the skipped files with errors, the instruction, the inputs, the model, and a "no inputs in <full path>" marker. It creates the run folder only when there are inputs, as today.
  - `RunAsync(plan, runs, onRun, ct)` → `JobEvalResult`: the runs and `StoppedBecause`, with the same cancel and error rules (`catch (Exception) when (ct.IsCancellationRequested)`).
  - `Summarize(result)` → `JobEvalTotals`: the pass aggregation from `PrintEvalTotals`.
  - The records live in `Domain/Outputs/JobEvalOutputs.cs`, including the old private `EvalRun`, now `JobEvalRun`, which gains `StrippedFence` and `CutOff`.
- **Console `JobCommands`**: parses the `/extract` and `/eval` arguments and prints every line exactly as today: usage, the "[…]" messages, fields, `PrintScore`, `PrintEvalRun` and the totals text.
- **Console `TurnCancellation`** (singleton): `Begin()` returns a token, `End()` disposes it, and `CancelIfActive()` is called by the `CancelKeyPress` handler. It's created and ended at the same points as `_turnCts` today.
- **Console `ChatSession`**: the welcome line, the loop, `/exit`, `/reset` (calls `conversation.Reset()`), the dispatch to `JobCommands`, and the chat-turn printing ("AI: ", streamed text, the discard messages, the usage line, the cut-off, empty and JSON lines).

**AiLab.Api.Tests** (stays where it is)

| File | Change |
|---|---|
| `AiLab.Api.Tests.csproj` | References: see C. Packages unchanged |
| `Fakes/ApiFactory.cs`, `Fakes/ScriptedChatClient.cs` | No change. `Program.ProviderClientKey` is still used and still exists |
| `AnalyzeTicketTests.cs` | Usings: `AiLab.Domain.Entities`, `AiLab.Domain.Responses`, `AiLab.Infrastructure.Repositories` (replacing `AiLab.Api.ChangeLog` and `AiLab.Api.Tickets`) |
| `TicketScorerTests.cs` | `AiLab.Application.Services`, `AiLab.Domain.Enums` |
| `TicketToolsTests.cs` | `AiLab.Application.Services`, `AiLab.Domain.Entities`, `AiLab.Infrastructure.Repositories` |

**New files** (not listed above): `Domain/Interfaces/{ITicketAnalyzer, ITicketEval, IProviderErrorClassifier, ITestTicketStore, IJobFileStore, IEvalRunStore}.cs`, `Domain/Constants/ChatClientKeys.cs`, `Infrastructure/Providers/ProviderErrorClassifier.cs`, `Infrastructure/Files/{JobFileStore, FileEvalRunStore}.cs`, `Infrastructure/Logging/FileLogging.cs`, `Infrastructure/DependencyInjection.cs`, `Application/DependencyInjection.cs`.

**Repo level:** `AiLab.slnx` gets the three new projects and loses `AiLab.Core`. `inputs/`, `prompts/`, `docs/`, `experiments.md` and `.gitignore` don't move.

## B. Folder trees

```
AiLab.Domain/                          (no packages, no references)
  Constants/   ChatClientKeys.cs  ErrorCodes.cs
  DTOs/        AnalyzeTicketRequest.cs  JobPosting.cs
  Entities/    ChangeEntry.cs
  Enums/       CauseStatus.cs  RunOutcome.cs  Verdict.cs  WorkMode.cs
  Exceptions/  ChangeLogUnavailableException.cs  ModelCallException.cs  TicketRejectedException.cs
  Interfaces/  IChangeLogRepository.cs  IEvalRunStore.cs  IJobFileStore.cs  IProviderErrorClassifier.cs
               ITestTicketStore.cs  ITicketAnalyzer.cs  ITicketEval.cs
  Outputs/     CauseVerification.cs  ChatTurnResult.cs  JobEvalOutputs.cs  JobExtractionOutputs.cs
               JobScore.cs  TicketAnalysis.cs  TicketRun.cs
  Responses/   AnalyzeTicketResponse.cs  ErrorResponse.cs
  Settings/    ResilienceSettings.cs
  AiLab.Domain.csproj

AiLab.Application/
  Services/    CauseCheck.cs  ChatConversation.cs  ChatExperimentSettings.cs  JobEval.cs  JobExtraction.cs
               JobExtractor.cs  JobScorer.cs  ModelPrices.cs  SessionCostTracker.cs  StructuredReply.cs
               TicketAnalysisRules.cs  TicketAnalyzer.cs  TicketEval.cs  TicketScorer.cs  TicketTools.cs
               ToolEvidence.cs  Versions.cs
  DependencyInjection.cs                (AddApplication)
  AiLab.Application.csproj

AiLab.Infrastructure/
  Files/        FileEvalRunStore.cs  JobFileStore.cs  TestTicketFiles.cs
  Logging/      FileLogging.cs
  Providers/    ChatClientFactory.cs  ProviderErrorClassifier.cs
  Repositories/ InMemoryChangeLogRepository.cs
  DependencyInjection.cs                (AddInfrastructure)
  AiLab.Infrastructure.csproj

AiLab.Api/
  Endpoints/   AnalyzeTicketEndpoint.cs  DevEndpoints.cs
  Middleware/  ErrorResponses.cs
  OpenApi/     AnalyzeTicketExamples.cs  OpenApiDocument.cs
  Properties/  launchSettings.json
  TestTickets/ expected.json  ticket-a.json  ticket-b.json  ticket-c.json  ticket-d.json
  analyze-ticket.http  appsettings.json  Program.cs  AiLab.Api.csproj

AiLab.Console/
  Chat/        ChatSession.cs  JobCommands.cs  TurnCancellation.cs
  Properties/  launchSettings.json
  appsettings.json  Program.cs  AiLab.Console.csproj

AiLab.Api.Tests/
  Fakes/       ApiFactory.cs  ScriptedChatClient.cs
  AnalyzeTicketTests.cs  TicketScorerTests.cs  TicketToolsTests.cs  AiLab.Api.Tests.csproj
```

## C. Project references

```
AiLab.Domain          → (none)
AiLab.Application     → Domain
AiLab.Infrastructure  → Domain                       (Application not needed: it implements Domain ports only)
AiLab.Api             → Application, Infrastructure, Domain
AiLab.Console         → Application, Infrastructure, Domain
AiLab.Api.Tests       → Api, Application, Infrastructure, Domain
```

Notes:
- The hosts use Infrastructure only for `AddInfrastructure`, `AddFileLogging` and `FileLogging.Folder` in `Program.cs`.
- Domain is referenced explicitly by the hosts and tests, like your `Presentation.csproj` and `Tests.csproj` do.
- The tests use `TicketTools`, `TicketScorer` and `InMemoryChangeLogRepository` directly, so their references are explicit rather than transitive.

**Violations today and how each is removed:**
1. `Api → Core` and `Console → Core`: Core mixes Infrastructure (factory, SDK packages) with Application (parsing, pricing), and both hosts get the provider SDKs transitively. Core is split and then deleted.
2. `TicketAnalyzer` (logic) names SDK and Polly exceptions. Replaced by the `IProviderErrorClassifier` port.
3. `TicketEval` (logic) does file IO. Replaced by `ITestTicketStore` and `IEvalRunStore`.
4. `ChatSession` and `JobScorer` (UI and logic) do file IO. Replaced by `IJobFileStore` and `IEvalRunStore`.
5. `InMemoryChangeLogRepository` (infrastructure) lives in the host. Moved to Infrastructure.

## D. NuGet changes

| Project | Packages |
|---|---|
| Domain | **none.** Only BCL attributes are used (`System.ComponentModel`, `System.ComponentModel.DataAnnotations`, `System.Text.Json.Serialization`) |
| Application | **Microsoft.Extensions.AI 10.11.0** (moved from Core), **Microsoft.Extensions.Logging.Abstractions 10.0.13**, **Microsoft.Extensions.DependencyInjection.Abstractions 10.0.13** (added; explicit, although M.E.AI brings them) |
| Infrastructure | **Anthropic 12.58.0**, **OllamaSharp 5.5.0** (from Core); **Microsoft.Extensions.AI.Abstractions 10.11.0** (added: `IChatClient`, `AIJsonUtilities` for `LoadExpected`); **Microsoft.Extensions.Http.Resilience 10.11.0** (from Api); **Serilog.Extensions.Hosting 10.0.0**, **Serilog.Settings.Configuration 10.0.1**, **Serilog.Sinks.File 7.0.0** (from both hosts); **Microsoft.Extensions.Configuration.Binder 10.0.13** (added: `Get<string[]>()` in the repository; Web gets it from the shared framework) |
| Api | Keeps Microsoft.AspNetCore.OpenApi 10.0.13 and Swashbuckle.AspNetCore.SwaggerUI 10.3.1. **Removes** Http.Resilience and the three Serilog packages |
| Console | Keeps M.E.Hosting, Configuration.Json and Configuration.UserSecrets 10.0.13. **Removes** the three Serilog packages |
| Api.Tests | No change |

All the added versions are already in the local NuGet cache.

**Where Microsoft.Extensions.AI lives.** `IChatClient` is the provider-neutral port, so Application depends on it directly; there is no wrapper. Application needs the full **Microsoft.Extensions.AI** package, not just Abstractions, because `GetResponseAsync<T>` and `ChatResponse<T>` are defined there. Infrastructure needs only **Abstractions**, because it creates `IChatClient`s. Domain has none, which is why `ChatTurnResult.FinishReason` is a string and the console services have no Domain interfaces. A custom wrapper would only rename `IChatClient` and would hide the middleware pipeline the tests rely on.

## E. Dependency injection

**`AiLab.Application/DependencyInjection.cs`**
```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddSingleton<TicketTools>();
    services.AddSingleton<ITicketAnalyzer, TicketAnalyzer>();
    services.AddSingleton<ITicketEval, TicketEval>();
    services.AddSingleton<SessionCostTracker>();
    services.AddSingleton<ChatConversation>();
    services.AddSingleton<JobExtractor>();
    services.AddSingleton<JobExtraction>();
    services.AddSingleton<JobEval>();
    return services;
}
```
All are singletons, as today. Singletons are built on first resolve, so each host only constructs what it uses.

**`AiLab.Infrastructure/DependencyInjection.cs`**
```csharp
// resilience null (console): plain OllamaApiClient and the Anthropic SDK defaults, exactly as the console runs today.
// resilience set (Api): Polly on the Ollama HttpClient, the SDK's own Timeout/MaxRetries for Anthropic. One retry layer per provider.
public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, ResilienceSettings? resilience = null)
{
    if (resilience is not null)
        services.AddHttpClient("ollama", c => c.Timeout = Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler(o => { /* the four lines from Program.cs, unchanged, incl. AllAttempts + 20 s */ });

    // The only place that knows which provider is used (moved from both Program.cs files)
    services.AddKeyedSingleton<IChatClient>(ChatClientKeys.Provider, (sp, _) =>
    {
        string provider = configuration["Chat:Provider"] ?? throw new InvalidOperationException("Chat:Provider is missing in appsettings.json.");
        string model = configuration["Chat:Model"] ?? throw new InvalidOperationException("Chat:Model is missing in appsettings.json.");
        return ChatClientFactory.Create(provider, model, configuration["Anthropic:ApiKey"],
            ollamaHttpClient: resilience is not null && provider == "ollama" ? sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama") : null,
            ollamaUrl: configuration["Chat:OllamaUrl"],
            anthropicAttemptTimeout: resilience?.AttemptTimeout,
            anthropicMaxRetries: resilience?.MaxRetries);
    });

    services.AddSingleton<IProviderErrorClassifier, ProviderErrorClassifier>();
    services.AddSingleton<IChangeLogRepository, InMemoryChangeLogRepository>();
    services.AddSingleton<ITestTicketStore, TestTicketFiles>();
    services.AddSingleton<IJobFileStore, JobFileStore>();
    services.AddSingleton<IEvalRunStore, FileEvalRunStore>();
    services.AddSingleton(TimeProvider.System);
    return services;
}
```

**`AiLab.Infrastructure/Logging/FileLogging.cs`**: `public static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "logs");` and `AddFileLogging(this IServiceCollection, IConfiguration, string filePrefix)`, which is the existing `AddSerilog(... ReadFrom.Configuration ... WriteTo.File(Folder/{prefix}-.log, Day))`. `AppContext.BaseDirectory` is the entry app's folder, so the paths stay `AiLab.Api/bin/Debug/net10.0/logs/api-*.log` and `AiLab.Console/bin/Debug/net10.0/logs/ai-*.log`.

**`AiLab.Api/Program.cs` after**
```csharp
using AiLab.Api.Endpoints;
using AiLab.Api.Middleware;
using AiLab.Api.OpenApi;
using AiLab.Application;
using AiLab.Domain.Constants;
using AiLab.Domain.Responses;
using AiLab.Domain.Settings;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Logging;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

// (existing comments kept)
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

ResilienceSettings resilience = builder.Configuration.GetSection("Resilience").Get<ResilienceSettings>() ?? new();

builder.Services.AddInfrastructure(builder.Configuration, resilience);
builder.Services.AddFileLogging(builder.Configuration, "api");
builder.Services.AddApplication();

// The first Use... is the outermost layer: function invocation → logging → provider. (existing comment block kept)
builder.Services.AddChatClient(sp => sp.GetRequiredKeyedService<IChatClient>(ChatClientKeys.Provider))
    .UseFunctionInvocation(configure: f =>
    {
        f.MaximumIterationsPerRequest = resilience.MaxToolIterations;
        f.IncludeDetailedErrors = false;
    })
    .UseLogging();

builder.Services.AddRequestTimeouts(options => options.AddPolicy("analyze-ticket", new RequestTimeoutPolicy
{
    Timeout = TimeSpan.FromSeconds(resilience.RequestTimeoutSeconds),
    TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
    WriteTimeoutResponse = context => context.Response.WriteAsJsonAsync(new ErrorResponse("The request timed out.", ErrorCodes.Timeout)),
}));

builder.Services.ConfigureHttpJsonOptions(options => { /* unchanged */ });
builder.Services.AddTicketApiDocument();

var app = builder.Build();

app.UseErrorResponses();
app.UseRequestTimeouts();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "AiLab API v1"));
    app.MapDevEndpoints();
}

app.Services.GetRequiredService<IChatClient>();
if (TimeSpan.FromSeconds(resilience.RequestTimeoutSeconds) <= resilience.AllAttempts)
    app.Logger.LogWarning(/* unchanged */);

app.MapAnalyzeTicket();

Console.WriteLine($"Logs: {FileLogging.Folder}");
app.Run();

public partial class Program
{
    public const string ProviderClientKey = ChatClientKeys.Provider; // tests use Program.ProviderClientKey, unchanged
}
```

**`AiLab.Console/Program.cs` after**
```csharp
using AiLab.Application;
using AiLab.Console.Chat;
using AiLab.Domain.Constants;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Logging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.Services.AddInfrastructure(builder.Configuration);   // no resilience: as today
builder.Services.AddFileLogging(builder.Configuration, "ai");
builder.Services.AddApplication();

// Logs every call (existing comment kept). The console has no tools, so no function invocation.
builder.Services.AddChatClient(sp => sp.GetRequiredKeyedService<IChatClient>(ChatClientKeys.Provider)).UseLogging();

builder.Services.AddSingleton<TurnCancellation>();
builder.Services.AddSingleton<JobCommands>();
builder.Services.AddSingleton<ChatSession>();

var host = builder.Build(); // built, not run (existing comment kept)

Console.WriteLine($"Logs: {FileLogging.Folder}");
await host.Services.GetRequiredService<ChatSession>().RunAsync();
```
The pipeline differs per host only in the line each `Program.cs` owns, so the factory isn't duplicated. The one-line keyed lookup appears in both.

**Options and config**

| Setting | Lives | Bound / read |
|---|---|---|
| `Resilience` → `ResilienceSettings` | Domain/Settings | Api `Program.cs` (`Get<ResilienceSettings>() ?? new()`). Passed to `AddInfrastructure` and used for the cap, request timeout and warning. Console: not bound |
| `Chat:Provider`, `Chat:Model`, `Chat:OllamaUrl` | no class (unchanged) | Read as strings in `AddInfrastructure`'s keyed factory only |
| `Anthropic:ApiKey` | user-secrets | Same place |
| `ChangeLog:FailComponents` | no class | `InMemoryChangeLogRepository(IConfiguration)` ctor (Infrastructure), unchanged |
| `Serilog` | each host's appsettings | `FileLogging.AddFileLogging` (`ReadFrom.Configuration`) |

**Required behaviours: where each ends up**

| Behaviour | After |
|---|---|
| Provider switch is config-only | `AddInfrastructure` keyed factory plus `ChatClientFactory`. Nothing else reads `Chat:Provider`. `ChatSession` and `TicketEval` still read the model name from `ChatClientMetadata` |
| Exactly one retry layer | Polly only when `resilience` is passed, and only on the Ollama `HttpClient`. Anthropic gets `Timeout`/`MaxRetries` from the same settings. Console: no Polly, SDK defaults |
| Api pipeline order | `AiLab.Api/Program.cs`: `UseFunctionInvocation` (outer) → `UseLogging` (inner) → keyed provider |
| Console pipeline | `AiLab.Console/Program.cs`: `UseLogging` → keyed provider |
| MaxToolIterations / attempt timeout / request timeout | Api Program (cap, request timeout policy), `AddInfrastructure` (attempt timeout, retries) |
| MaxOutputTokens | `TicketAnalyzer` const 2000 and `JobExtractor` 2000 (Application, unchanged); chat options in `ChatExperimentSettings` |
| User-secrets in Development | Unchanged. Both host csprojs keep `UserSecretsId 081e3468-…`. `CreateBuilder` reads it from the entry assembly, so the class libraries need none. `launchSettings.json` still sets `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT=Development` |
| Log files | `FileLogging.Folder` under each host's `bin/Debug/net10.0/logs/`, prefixes `api-` and `ai-` |
| `inputs/`, `prompts/` | `JobFileStore`: relative to the working directory (run from the repo root), as today |
| `eval-runs/` | `FileEvalRunStore`: first folder above `AppContext.BaseDirectory` with `AiLab.slnx`, else the working directory. Used by both evals (agreed) |
| `TestTickets/` | Files stay in `AiLab.Api/TestTickets/` and are copied to output. `TestTicketFiles` reads `BaseDirectory/TestTickets` for the Swagger examples and the ticket eval; `.http` reads `./TestTickets/` |

## F. Abstractions to introduce

Ports go in `AiLab.Domain/Interfaces`, adapters in `AiLab.Infrastructure`.

| Interface | Members | Implementation | Why |
|---|---|---|---|
| `IChangeLogRepository` | unchanged | `Repositories/InMemoryChangeLogRepository` | **Just moves** |
| `IProviderErrorClassifier` | `bool IsProviderFailure(Exception ex)` | `Providers/ProviderErrorClassifier`: `ex is HttpRequestException or OllamaException or AnthropicException or ExecutionRejectedException` | Keeps the SDK and Polly types out of Application. The cancellation rule stays in `TicketAnalyzer` (it needs the token) |
| `ITestTicketStore` | `IReadOnlyList<(string Letter, string Json)> ReadAll()`, `Dictionary<string, string?> ReadExpected()` | `Files/TestTicketFiles` | `TicketEval` and the Swagger examples read the files through it |
| `IJobFileStore` | `bool Exists(string path)`, `string FullPath(string path)`, `Task<string> ReadAllTextAsync(string path, CancellationToken ct = default)`, `IReadOnlyList<string> FindInputs()` (`inputs/job-*.txt`, `[]` when the folder is missing), `JobPosting? LoadExpected(string path, out string? error)` | `Files/JobFileStore` | Deliberately thin: the path conventions (`prompts/extract-{v}.txt`, `job-N.expected.json`, the `job-(\d+)` sort) stay in Application, so the logic doesn't change |
| `IEvalRunStore` | `string CreateRunFolder(string runName)`, `Task WriteAsync(string runFolder, string fileName, string content, CancellationToken ct = default)` | `Files/FileEvalRunStore` (contains `RepoRoot()`) | Both evals write raw replies through it |
| `ITicketAnalyzer` | `AnalyzeAsync`, `RunAsync` (current signatures) | `Application/Services/TicketAnalyzer` | Service interface by your convention (agreed) |
| `ITicketEval` | `Task<string> RunAsync(int runs, decimal? maxCost, CancellationToken)` | `Application/Services/TicketEval` | Same |

No wrapper around `IChatClient` and no `IClock` (`TimeProvider` already is one).

## H. Docs and paths to update (step 6)

- **README.md:**
  - "Settings worth knowing" → `AiLab.Application/Services/ChatExperimentSettings.cs` (~133).
  - The tests paragraph: 43 tests (~263).
  - "Project structure": rewrite from B with one-line roles (~266–300).
  - Add a short "Layers" paragraph with the reference rule.
  - Run commands, user-secrets, appsettings, log, TestTickets, `.http` and curl paths are unchanged.
- **CLAUDE.md:**
  - Stack: six projects and their roles, the factory location, how the hosts are wired (AddInfrastructure, AddApplication, pipeline per host), typed-extraction paths, the eval paths.
  - "37 tests" → 43 in two places.
  - Stage 4 line: `AiLab.Rag` stays a separate project and will reference Domain, Application and Infrastructure when it's created.
- Both `launchSettings.json` files and `analyze-ticket.http` need no change (`.http` uses relative `./TestTickets/`).
- `.gitignore`: no change (`eval-runs/` and `[Ll]ogs/` are path-independent).
- **docs/:**
  - `docs/plans/*` and `experiments.md` are history; leave them.
  - `docs/failure-table.md` line 3 names `AiLab.Api` "as of 2026-10-23"; leave it.
  - `docs/reviews/2026-10-23-week-2.md`: add one line under the header saying that File:line refers to the pre-restructure layout, with a link to this plan's mapping table, because its Resolution column is still open.

## Tests

No new tests. The plan changes no test logic, only usings and references (steps 2 and 4a).

| Step | Tests | Layer | How to run |
|---|---|---|---|
| Every step (0–6) | The existing 43: `AnalyzeTicketTests` (17), `TicketScorerTests` (21 cases), `TicketToolsTests` (5 cases) | Integration (`WebApplicationFactory<Program>` with `ScriptedChatClient`, offline) and unit | `dotnet build AiLab.slnx` with 0 warnings, then `dotnet test` with 43/43 |
| 0 | Baseline console transcript (`/extract v2 inputs/job-1.txt`, `/eval v1 1`, one chat turn, `/reset`, Ctrl+C during a reply) and POST ticket C | Manual E2E on Ollama | `dotnet run --project AiLab.Console` from the repo root; `dotnet run --project AiLab.Api` (check port 5028 first) |
| 3 | Api smoke: POST ticket C, `api-*.log` written, Swagger examples shown; `POST /dev/eval-tickets?runs=1` writes to `<repo>/eval-runs/…_tickets_llama3.2` | Manual E2E on Ollama | Run the Api in Development, send `analyze-ticket.http` C, open `/swagger`, call the dev endpoint |
| 4a | Information lines "GetRecentChanges called …" and "Cause grounded …" still in the Api log | Manual E2E on Ollama | POST a ticket and read `logs/api-*.log` |
| 4b | Rerun the step-0 transcript; same line formats, except the agreed absolute "replies saved to …" path | Manual E2E on Ollama | Same commands as step 0 |
| Final | `grep -rn "AiLab.Core" --include=*.cs --include=*.csproj --include=*.slnx .` returns nothing | Static check | Shell |

## Decisions & trade-offs

- **Service interfaces only where the signature uses Domain types alone:** `ITicketAnalyzer` and `ITicketEval`. `JobExtractor`, `ChatConversation` and the other console services stay concrete, because their signatures carry `ChatResponse<T>` or `ChatResponseUpdate`, and Domain stays package-free.
- **Experiment knobs** go in one file, `AiLab.Application/Services/ChatExperimentSettings.cs`. It's just as easy to edit as the top of `ChatSession.cs` and still needs a rebuild, as today. Prompts stay in `prompts/` (no rebuild).
- **eval-runs/:** both evals use the repo-root search for the `.slnx`. In practice it's the same folder as today.
- **Application layout:** flat `Services/`, like MultiTenantSaaSApi.
- **No `IChatClient` wrapper.** `IChatClient` is already the provider-neutral port. Application uses the full Microsoft.Extensions.AI package and Infrastructure only Abstractions.
- **`IProviderErrorClassifier`** keeps the Anthropic, Ollama and Polly exception types out of Application. The cancellation rule stays in `TicketAnalyzer` because it needs the request token.
- **`AddInfrastructure(configuration, resilience?)`**: `null` gives the console's plain clients (no Polly, SDK defaults); a value gives the Api's single retry layer per provider. The host-specific middleware pipeline stays in each `Program.cs`, so the factory isn't duplicated.
- **`ResilienceSettings` and `ChatClientKeys` live in Domain,** so the Api host and Infrastructure share them without the host depending on Infrastructure types.
- **`TicketAnalyzer` is not split further.** It's one cohesive use case and the rules are already in `TicketAnalysisRules`.
- **Namespaces stay file-scoped.** Switching to block-scoped would touch every line of every file for no structural gain.
- **Written rules win over MultiTenantSaaSApi** where they differ: Application doesn't reference Infrastructure, and DI goes through `AddApplication` and `AddInfrastructure` instead of inline registrations.
- **Agreed visible differences:**
  - The console's `/eval` header prints an absolute `eval-runs` path.
  - The console now honours `Chat:OllamaUrl` if set. It isn't set today, so nothing changes.
- **Honest cost:** reading one chat turn now spans `ChatSession` (printing) and `ChatConversation` (history and cost), and `/eval` spans `JobCommands`, `JobEval` and two stores. That's more files to step through while debugging.

## Alternatives considered

- **Domain interfaces for every service,** matching MultiTenantSaaSApi exactly. Rejected: Domain would have to reference Microsoft.Extensions.AI, because `ChatResponse<T>` and `ChatResponseUpdate` appear in the signatures.
- **No service interfaces at all** (hosts resolve concrete classes). Rejected in favour of your convention where the signatures allow it.
- **Experiment knobs at the top of `ChatConversation.cs`** or in an `appsettings.json` `Chat:Experiment` section. The appsettings option is new configuration, and a multi-line JSON system message is awkward to edit in JSON.
- **Keep both eval-runs behaviours exactly** (the store takes its root as a parameter). Rejected: an extra parameter on `AddInfrastructure` for no practical difference in where the files land.
- **Feature folders in Application** (Tickets/, Grounding/, Evaluation/, …). Closer to today's code, but not your standard.
- **A custom wrapper around `IChatClient`.** Rejected: it would only rename `IChatClient` and would hide the middleware pipeline the tests rely on.

## Out of scope

- Behaviour, route, prompt and response-shape changes (apart from the agreed differences above).
- The week-2 review findings (`docs/reviews/2026-10-23-week-2.md`), including the ones in code being split (L10 history trim, L11 Ctrl+C race, L12 `/extract` cancel): fixing them would change behaviour. Recommendation: restructure first (Stage 4 starts 2026-10-26), then fix the findings on the new layout. Step 2 does the file split that L13 suggests for `ModelCallException.cs`.
- New tests. Optional follow-up, not in this plan: an `AiLab.Application.Tests` project for `JobEval.Summarize`, `ChatConversation` trimming, `StructuredReply` and `ModelPrices` (see review finding M8).
- Rewriting history docs (`docs/plans/*`, `experiments.md`, `docs/failure-table.md` line 3).

## Risks

- **Step 4b is the riskiest step.** The console has no tests, and the split is checked only by comparing the transcript on Ollama.

## Open questions

None. Answered on 2026-10-23: `AiLab.Rag` (Stage 4) will be a separate project that references Domain, Application and Infrastructure. It isn't created by this plan.

## Changes

- **2026-10-23, step 3: `AddInfrastructure` takes no `IConfiguration`.** The signature is `AddInfrastructure(this IServiceCollection services, ResilienceSettings? resilience = null)`. The keyed provider factory reads `IConfiguration` from the container when the client is first resolved, as both `Program.cs` files did before. A configuration passed in at registration would be unused, or it could miss settings that `WebApplicationFactory` adds later. Section E's sketches still show the old parameter.
- **2026-10-23, step 3: `JobFileStore.LoadExpected` is in place, and `JobScorer.LoadExpected` stays in the Console until step 4b.** The two are duplicates for now. Step 4b removes `JobScorer.LoadExpected` when `ChatSession` is split.
