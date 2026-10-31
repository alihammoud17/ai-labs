# ai-lab

A .NET 10 solution (a console chat app and an ASP.NET Core API, in Clean Architecture layers) for learning AI integration hands-on. Part of a ~12-week self-study plan to become an AI-enabled .NET specialist.

## About me

- .NET backend developer, 5 years of experience.
- Strong in C#, ASP.NET Core and SaaS architecture; new to LLM integration.
- Already studied: tokens, context windows, inference, sampling (temperature, top-p, top-k), hallucinations, reasoning models.

## Stack

- Solution `AiLab` (`AiLab.slnx`), .NET 10, Clean Architecture (references point inward only), nine projects:
  - `AiLab.Domain`: no packages, no references. `DTOs/`, `Entities/`, `Outputs/`, `Responses/`, `Enums/`, `Constants/`, `Exceptions/`, `Settings/` (`ResilienceSettings`, `VectorSearchSettings`, `AskSettings`), `Interfaces/` (`ITicketAnalyzer`, `ITicketEval`, `IChangeLogRepository`, `ITestTicketStore`, `IJobFileStore`, `IEvalRunStore`, `IProviderErrorClassifier`, `ITextVectorStore`, `IVectorSearchService`, `IAskService`). `/ask` types: `AskRequest`, `AskAnswer` (the model's typed reply; its `[Description]`s go to the model), `AskResponse` + parts, `GroundingStatus`, `GateResult` (lowercase JSON via `[JsonStringEnumMemberName]`).
  - `AiLab.Application` (→ Domain): flat `Services/` with all business logic, `AddApplication()`. `Chunking/` (stage 4: `IChunker`, `FixedSizeChunker`, `HeadingChunker`). `Ask/` (stage 4: `AskPrompt.Build` → messages + source map, `CitationCheck.Evaluate`). Includes `StructuredReply.TryParse<T>` (typed parse with the ```` ```json ```` fence fallback), `ModelPrices`, `SessionCostTracker`, `ChatConversation`, `ChatExperimentSettings`, `SimilarityService`, `VectorSearchService` and `AskService` (stage 4; the last two factory-registered).
  - `AiLab.Infrastructure` (→ Domain): provider SDKs, Polly, Serilog, files. `AddInfrastructure(ResilienceSettings? resilience = null)`, `Providers/ChatClientFactory`, `ProviderErrorClassifier`, `Repositories/InMemoryChangeLogRepository`, `Files/` (`TestTicketFiles`, `JobFileStore`, `FileEvalRunStore`, `RepoPaths`), `Logging/FileLogging`, `VectorStore/` (`TextChunkRecord`, `TextVectorStore`, `AddVectorStore` in `VectorStoreServices`).
  - `AiLab.Console` (→ Application, Infrastructure, Domain): the chat app (stages 1–2, plus `/similarity` in stage 4). `Chat/ChatSession` (loop, streaming, Ctrl+C), `Chat/JobCommands` (`/extract`, `/eval` parsing and output), `Chat/EmbeddingCommands` (`/similarity`).
  - `AiLab.Web` (→ Domain, ASP.NET Core framework only): what the two web hosts share. `ErrorResponses`: `AddErrorResponses()` (`ThrowOnBadRequest` everywhere, so an unreadable request reaches the handler; a shared `BadHttpRequestException` handler → 400 `invalid_request` "The request could not be read.") and `UseErrorResponses()` (the host's `IExceptionHandler`s first, then 500 `internal_error`), plus `Result(...)` / `WriteAsync(...)` for `{ error, code }`. Not in Infrastructure, so the console doesn't pull in ASP.NET Core.
  - `AiLab.Api` (→ Application, Infrastructure, Domain, Web): minimal API (stage 3), endpoints in `Endpoints/`, errors via `AiLab.Web` (the ticket endpoint still maps its own 422 / 502), OpenAPI setup in `OpenApi/`, `POST /analyze-ticket`, on http://localhost:5028. Swagger UI at `/swagger` in Development. Test tickets A–D in `AiLab.Api/TestTickets/` (one JSON body each, plus `expected.json`); Swagger and `analyze-ticket.http` read them from there.
  - `AiLab.Rag` (→ Application, Infrastructure, Domain, Web): minimal API for stage 4 (vector search and `POST /ask`), on http://localhost:5074. `AddInfrastructure()` (no resilience), `AddFileLogging(..., "rag")`, `AddApplication()`, `AddChatClient(keyed).UseLogging()` (needed by `ValidateOnBuild`, since `AddApplication` registers chat services), `AddEmbeddingGenerator(keyed).UseLogging()`, `AddVectorStore(config)` (only this host calls it). Endpoints in `Endpoints/`: `GET /health`, `GET /search?q=&top=5&chunker=`, `POST /ask` (`AskEndpoint`; per-IP fixed-window rate limiter `Ask:PermitsPerMinute` 10 → 429 `rate_limited` with `Retry-After`), and in Development only `POST /dev/seed`, `POST /dev/ingest?chunker=fixed|heading|both` (every `.md` under `Corpus:Path`, default `data/corpus`) and `GET /dev/chunks?chunker=&source=`; `rag.http` sends them. Swagger UI at `/swagger` in Development (`OpenApi/`: document info, examples from the real 2e runs, and the `ErrorResponse.code` description rewritten with Rag's codes in this document only). Errors as `{ error, code }` through `AiLab.Web`'s `UseErrorResponses` and `Middleware/RagExceptionHandler.cs` (an `IExceptionHandler`; the endpoints have no try/catch): only `InvalidRequestException` (Domain) is a 400, any other `ArgumentException` is a 500; `NpgsqlException` → 503 (not a bare `SocketException`: `HttpClient` wraps one when Ollama is down), `HttpRequestException` → 502, missing collection → 409, `ModelCallException` → 502 with its code (checked first: a chat-provider outage wraps an `HttpRequestException` too). Errors an endpoint finds itself (missing seed file or corpus, bad `chunker` on ingest) return `ErrorResponses.Result(...)`. Config: `Chat:*`, `Embedding:*` (as the console), `VectorStore:Provider` (`postgres` / `inmemory`), `VectorStore:Collection` (`corpus`; one table for every chunker, filtered by `Chunker`), `Embedding:UseTaskPrefixes` (default false: nomic's `search_document: ` / `search_query: ` prefixes, embedding input only; changing it means seeding again), `Ask:*` (`AskSettings`: Top 5, MaxTop 10, Chunker `heading`, MaxDistance 0.50, MaxQuestionChars 500, MaxOutputTokens 800, PromptPath `prompts/ask-v1.md` made absolute with `RepoPaths` and read on every request, PermitsPerMinute 10). Connection string only in user-secrets, key `ConnectionStrings:Vectors`. Same `UserSecretsId` as the console and API.
  - `AiLab.Api.Tests`: xUnit v3 integration tests, `WebApplicationFactory<Program>` with a `ScriptedChatClient` registered under the keyed service `"provider"`, so the real function-invocation and logging middleware run above the fake. `dotnet test` runs offline (no network, Ollama or key). The plain unit tests live here too (`TicketScorerTests`, `TicketToolsTests`, `EmbeddingGeneratorFactoryTests`, `SimilarityServiceTests` over a `FixedEmbeddingGenerator` fake, `TextVectorStoreTests`, `VectorSearchServiceTests` over a `MappedEmbeddingGenerator` fake).
  - `AiLab.Rag.Tests`: xUnit v3, `WebApplicationFactory<Program>` over `AiLab.Rag` with `VectorStore:Provider=inmemory` and a `HashedEmbeddingGenerator` (one fixed 768-number vector per text) under the keyed `"embedding-provider"`; for `/ask` also its own `ScriptedChatClient` (steps can throw) under the keyed `"provider"` (`RagFactory`'s `chat`). Offline. Separate from `AiLab.Api.Tests` because one test project can't host two top-level Programs.
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
- Embeddings (stage 4): `AiLab.Infrastructure/Providers/EmbeddingGeneratorFactory.cs`, called only by the keyed `"embedding-provider"` singleton (`EmbeddingGeneratorKeys.Provider`) that `AddInfrastructure` registers as `IEmbeddingGenerator<string, Embedding<float>>`.
  - Config `Embedding:Provider` / `Embedding:Model` / `Embedding:OllamaUrl`, only in the console's `appsettings.json` so far: `ollama`, `nomic-embed-text` (768 dimensions), `http://localhost:11434`.
  - Separate from `Chat:*` because the chat provider can be Anthropic, which has no embeddings API. Only `"ollama"` is supported; `"anthropic"` or any other value throws, as does a missing or empty provider or model (the message names the key).
  - Its own `OllamaApiClient`, not the chat one. The model sets the vector size, so another model (e.g. `all-minilm`, 384) is a config change.
  - Read on first resolve, so the API and the tests run without an `Embedding` section.
  - Console: `AddEmbeddingGenerator(keyed).UseLogging()`, logged to the same `ai-` file as chat calls. `AiLab.Application` has `System.Numerics.Tensors` for `TensorPrimitives.CosineSimilarity`.
  - `SimilarityService` is registered in `AddApplication()` with a factory lambda, not `AddSingleton<SimilarityService>()`: the API has no `IEmbeddingGenerator`, and Development's startup check (`ValidateOnBuild`) would stop it. A test runs that check.
- Vector store (stage 4): `docker compose up -d` (Colima: `colima start` first) runs `pgvector/pgvector:pg17` on host port 5433, user/password/database `ailab`, `docker/init-vector.sql` creates the `vector` extension (0.8.7). Packages in Infrastructure: `CommunityToolkit.VectorData.InMemory` and `.PgVector` 1.0.1 (pull `Microsoft.Extensions.VectorData.Abstractions` 10.8.2; not the deprecated `Microsoft.SemanticKernel.Connectors.PgVector`).
- Provider and model: `Chat:Provider` / `Chat:Model` in each project's `appsettings.json` (the API also has `Chat:OllamaUrl`). Keep it on Ollama; for a one-off Anthropic run, override with the env vars `Chat__Provider` / `Chat__Model`.
- Logging: Serilog to a daily file in each project's `bin/Debug/net10.0/logs/` (`ai-` for the console, `api-` for the API). Levels under `Serilog:MinimumLevel`; `Verbose` shows full requests and per-call usage.
- Typed extraction: `JobPosting` (Domain), `JobExtractor`, `JobExtraction`, `JobScorer` (Application; parsing via `StructuredReply`), `/extract [v1|v2] <path>`. Inputs and golden answers in `inputs/`, prompts in `prompts/`. Run the app from the repo root.
- Repeated scored runs: `/eval <v1|v2> [runs]` (default 3, `JobEval`) over every `inputs/job-N.txt` with a golden answer; raw replies in the repo's `eval-runs/` (git-ignored) for offline re-scoring.
- Similarity matrix: `/similarity [path]` (default `inputs/similarity-sentences.txt`, one sentence per line). `SimilarityService` embeds all sentences in one call and scores every pair with `TensorPrimitives.CosineSimilarity`; `EmbeddingCommands` prints the grid (≥ 0.70 green, ≤ 0.40 grey, by the printed value) and an S1..Sn key. Tokens come from the call's `Usage`.
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

**Current step:** Stage 4 (RAG), week of 2026-10-26. Mon, Tue and Wed done (merged into `main`; Wed is PR #5). Thu/Fri: `POST /ask` built and evaluated, merged into `main` (PR #6), section 13 done. Stage 4 is done. Week 2 failure log (`eval/results/2026-10-31-failure-log.md`, `experiments.md` section 14) on branch `stage-4-failure-log`. Next step not set yet. The shared error handling (`AiLab.Web`) is done, on `main` (fb2f976).

## Stage 4 (done): embeddings, pgvector, RAG

Built on branch `stage-4-embeddings`, merged into `main`. Tutor mode applies: I write chunking, retrieval and prompt assembly myself.
Claude may write Docker, DI wiring and test scaffolding.

0. [x] Infrastructure: keyed `"embedding-provider"` generator, `Embedding:*` config, console `UseLogging`, `System.Numerics.Tensors`, factory tests (53 pass). Plan: `docs/plans/2026-10-26-stage-4-embedding-infrastructure.md`
1. [x] Mon: similarity matrix with IEmbeddingGenerator (nomic-embed-text via Ollama): `SimilarityService`, `SimilarityMatrix`, `EmbeddingCommands`, `/similarity`. Claude finished it (handed over). 62 tests pass. Plan: `docs/plans/2026-10-26-stage-4-similarity-matrix.md`. Results: section 10 of `experiments.md` (negation scores 0.94, a paraphrase without shared words 0.49; good for ranking, not for a threshold)
2. [x] Tue: AiLab.Rag (a separate project referencing Domain, Application and Infrastructure) + pgvector via Microsoft.Extensions.VectorData, top-5 query with scores. Plan: `docs/plans/2026-10-27-stage-4-pgvector.md`
   - 2a scaffolding done (`AiLab.Rag`, docker compose, packages, `GET /health`)
   - 2b record done (`TextChunkRecord`: string key, `Source` indexed, 768 dimensions, cosine distance, HNSW)
   - 2c store plumbing done: `ITextVectorStore` (Domain), `TextVectorStore` over a `VectorStoreCollection<string, TextChunkRecord>`, `AddVectorStore(config)` in `Infrastructure/VectorStore/VectorStoreServices.cs` (`inmemory` | `postgres`; provider read at registration, connection string on first resolve). `SearchHit.Score` is cosine distance (0 = identical, lower = closer), the same on both stores. `TextVectorStore` refuses vectors that aren't `TextChunkRecord.Dimensions` (768) long, naming `Embedding:Model`, and NaN scores (zero vectors). 76 tests pass, plus 1 Postgres test that runs only with `AILAB_PG=1` (table `chunks_test`, checks the HNSW index). Postgres columns are quoted PascalCase (`"Key"`, `"Embedding"`) in psql.
   - 2d search service done: `IVectorSearchService` (Domain), `VectorSearchService` (Application, factory-registered in `AddApplication()`). User wrote it; Claude applied the review fixes (asked). `IndexAsync` refuses empty texts and duplicate keys, embeds in batches of 32 (tokens logged per batch), creates the collection; `SearchAsync` (top 1–20) never creates it, and a missing collection fails with a message naming it and `VectorStore:Collection`. 88 tests pass (`VectorSearchServiceTests` over `Fakes/MappedEmbeddingGenerator`).
   - 2e endpoints done: `GET /search` returns `{ query, top, provider, elapsedMs, hits: [{ rank, key, text, source, distance, similarity }] }`. `distance` is cosine distance (lower = closer); `similarity` = 1 − distance (4 decimals), only to compare with `/similarity`. `POST /dev/seed` (Development) indexes `inputs/similarity-sentences.txt` (`Seed:Path`, relative to the repo root via `RepoPaths`, since `dotnet run` starts a web project in its own folder), keys `similarity-sentences:<line>`. Errors: 400 `invalid_request` / `seed_file_not_found`, 409 `collection_missing` (search before seeding), 502 `embedding_unavailable`, 503 `vector_store_unavailable`, 500 `internal_error`. Both stores gave the same top 5 with the same distances on 4 queries. 105 tests pass (88 + 17 in `AiLab.Rag.Tests`). Swagger added afterwards: 110 (22 in `AiLab.Rag.Tests`).
   - 2f done: `InvalidRequestException` for real input errors (other `ArgumentException`s are a 500), `Embedding:UseTaskPrefixes` (default false) and the prefix experiment. Results: section 11 of `experiments.md` (Postgres = in-memory; prefixes a draw, kept off; negation still a near tie). 114 tests (113 pass, 1 Postgres test skipped without `AILAB_PG=1`).
3. [x] Wed: ingest data/corpus (fixed-size vs heading chunker), metadata, content hash to skip unchanged chunks. Branch `stage-4-ingestion`, merged into `main` (PR #5). Plan: `docs/plans/2026-10-28-stage-4-ingestion.md` (steps 3.1–3.6; 3.3, the two chunkers, was mine, handed over)
   - 3.1 done: `data/corpus/` (README.md, docs/decisions.md, docs/contracts.md, docs/architecture.md from MultiTenantSaaSApi at `c87cc20`), notes in `data/README.md` (outside the ingested folder)
   - 3.2 done: `TextChunk(Chunker, Source, Position, Text, Heading, StartLine, EndLine)` with computed `Key` (`"{chunker}:{source}:{position}"`) and `ContentHash` (SHA-256 hex); same columns on `TextChunkRecord` (`Chunker` indexed). `SearchHit(TextChunk Chunk, double Score)`. `SearchAsync(..., chunker)` filters by chunker (null = all). Collection `corpus` (old `chunks` table dropped); seed keys `lines:similarity-sentences.txt:<line>`. 121 tests (120 pass, 1 Postgres test skipped without `AILAB_PG=1`)
   - 3.3 done (handed over to Claude after `IChunker` and `ChunkingOptions`): `AiLab.Application/Chunking/` (`MarkdownText`, `FixedWindows`, `FixedSizeChunker` "fixed", `HeadingChunker` "heading"), `ChunkingOptions.Default` 2000 / 200 / 100 characters (validated: positive, overlap under half). Corpus: fixed 21 chunks, heading 50. 154 tests (153 pass, 1 Postgres test skipped without `AILAB_PG=1`)
   - 3.4 done: `/dev/ingest` (per-file and total chunks, min/avg/max estimated tokens, real embedding tokens from `IndexResult`, elapsed), `/dev/chunks` (`ITextVectorStore.GetChunksAsync`), `/search?chunker=` (hits add chunker, heading, position, lines). `ChunkerNames` in Domain; unknown chunker 400; `corpus_not_found` 400. Corpus on Postgres: fixed 21 chunks / 9,208 tokens, heading 50 / 8,805 (chars/4 is ~4% high). 174 tests (173 pass, 1 Postgres test skipped without `AILAB_PG=1`)
   - 3.5 done (handed over; draft for my review): `experiments.md` section 12. Heading 4 rank 1 / 1 top 3 / 0 miss, fixed 3 / 1 / 1 (5 questions, top 3); fixed's miss is a 3-line ADR diluted in a 2,000-char window; both answer 5 / 5 from the top 3 because facts repeat across files. Thursday: search `chunker=heading`, top ≥ 3, cite source + lines
   - 3.6 done: `SyncAsync` (embeds only new keys / changed `ContentHash`, deletes lost positions and removed files' chunks), `ITextVectorStore.DeleteAsync`, `/dev/ingest?force=true` (after a model or prefix change). Second run: 0 embedded. An edit re-embeds 1 heading section but every later fixed window in that file. Optional MEDI `HeaderChunker` comparison skipped. 187 tests (186 pass, 1 Postgres test skipped without `AILAB_PG=1`)
4. [x] Thu: POST /ask with numbered sources, typed answer, citation check against retrieved IDs, length limit, rate limiter (done in item 6)
5. [x] Fri: 10 questions, failure log (retrieval miss / wrong chunk / generation error / should have refused) → experiments.md section 12 (done as section 13: a 10-question subset, results in `eval/results/2026-10-30-ask-v1-subset.md`; conclusion written)
6. [x] Thu/Fri: POST /ask (grounded answers), branch `stage-4-ask`, merged into `main` (PR #6); section 13 written. Plan done. Plan: `docs/plans/2026-10-30-stage-4-ask.md`
   - 4.0–4.5 done: Domain contracts and `AskSettings` (`Ask:*` in Rag's appsettings, `PromptPath` resolved from the repo root); `AskService` (validation, retrieval, distance gate with `GateResult` `passed` / `no_hits` / `too_far`, best and second distance logged; mine), `prompts/ask-v1.md` + `AskPrompt.Build` (mine, review fixes by Claude), generation (typed `AskAnswer`, one repair, `reply_cut_off` / `reply_unreadable` / `provider_unavailable` as `ModelCallException`), `CitationCheck` (list + inline `[n]` against the sources sent). 200 tests (199 pass, 1 Postgres test skipped without `AILAB_PG=1`).
   - 4.6 done: `POST /ask` (`Endpoints/AskEndpoint.cs`), `IAskService` factory-registered, rate limiter (`Ask:PermitsPerMinute` 10 per IP, 429 `rate_limited` with `Retry-After`), `ModelCallException` → 502 with its code (mapped before the `HttpRequestException` check), OpenAPI with real llama3.2 examples, `rag.http`. Smoke test on llama3.2: grounded, gate abstain (0.600), `notInDocs` abstain (0.388), 400s, 429. Don't use "invoices emailed" as a near-miss: it's `ask-v1.md`'s own example.
   - 4.7 done: 28 offline tests in `AiLab.Rag.Tests/AskEndpointTests.cs` (Rag.Tests' own `ScriptedChatClient`; `RagFactory` takes it): every status and error code in the contract, the prompt sent, the repair, the rate limiter. 228 tests (227 pass, 1 Postgres test skipped without `AILAB_PG=1`). Next: 4.8, my manual eval
   - Found in 4.8: Ollama's JSON mode (`format: "json"`, which `GetResponseAsync<T>` asks for) cuts `answer` at the first unescaped `"` copied from a source, and the model closes the object there (valid JSON, half an answer, no `citedSources`). Fixed by `ask-v1.md` rule 10 (values in backticks, no double quotes) and by treating a missing `citedSources` as unreadable (one repair). 229 tests (228 pass, 1 skipped)

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
