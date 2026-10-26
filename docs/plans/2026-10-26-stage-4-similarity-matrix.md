# Stage 4 step 1: the similarity matrix (/similarity)

- **Date:** 2026-10-26
- **Branch:** `stage-4-embeddings`, merged into main
- **Goal:** Embed the sentences in `inputs/similarity-sentences.txt` with `nomic-embed-text`, score every pair with cosine similarity, and print the grid in the console. This is the first look at what embeddings do and don't capture, before pgvector.
- **Status:** done

The user wrote `SimilarityService` and `SimilarityMatrix`, then handed over the rest of the step (overrides tutor mode). Claude tidies `SimilarityService` without changing its algorithm.

## Steps

- [x] **1** `SimilarityService`: `sealed`, primary-constructor parameter instead of the `_embeddingGenerator` field. The algorithm is untouched: one `GenerateAsync` call for all sentences, checks on count and vector length, half the grid computed and mirrored.
- [x] **2** Register `SimilarityService` in `AddApplication()` ("The console: /similarity").
- [x] **3** `EmbeddingCommands.SimilarityAsync`, in the style of `JobCommands`:
  - `/similarity [path]`, default `inputs/similarity-sentences.txt`, relative to the repo root like `/extract`. Lines trimmed, blanks skipped.
  - `[file not found: ...]`; fewer than 2 sentences → usage message.
  - `TurnCancellation` Begin/End; `[similarity cancelled]`; `[error: ...]` plus a hint.
  - Header (provider/model/dimensions from `EmbeddingGeneratorMetadata`, falling back to the matrix's `Dimensions`; tokens or `n/a`; ms), the S1..Sn grid with `{score,6:F2}` cells, ≥ 0.70 green and ≤ 0.40 dark grey (diagonal uncoloured), then the key.
- [x] **4** `Program.cs` registers `EmbeddingCommands`; `ChatSession` dispatches `/similarity` and shows it in the welcome line.
- [x] **5** Offline tests with a fixed-vector fake generator.
- [x] **6** Real run on Ollama.
- [x] **7** Docs: `CLAUDE.md`, `README.md`, this plan. `experiments.md` is the user's.

## Tests

- `AiLab.Api.Tests/SimilarityServiceTests.cs` over `Fakes/FixedEmbeddingGenerator.cs` (returns the given vectors and usage, no network):
  - the diagonal is 1 and the grid is symmetric
  - known values: [1,0]·[0,1] = 0, [1,0]·[1,0] = 1, [1,1]·[1,0] ≈ 0.7071 (cos 45°)
  - `Dimensions` is the vector length (5), not the sentence count (2)
  - `InputTokens` comes from `Usage`, and is null without it
  - empty list → `ArgumentException`; wrong embedding count or mixed vector lengths → `InvalidOperationException`
  - `AddInfrastructure()` + `AddApplication()` with no `IEmbeddingGenerator` passes `ValidateOnBuild` (see Changes)
- Manual, on Ollama: default file; a missing file; a one-line file; `Embedding__Model=no-such-model`; `Embedding__OllamaUrl=http://localhost:11999`; colours checked through a pseudo-terminal (`script`); the API started in Development.

## Decisions & trade-offs

- **The command reads the file itself** (3): it's console input, like reading a line, so no new store interface. `IJobFileStore` is about jobs, and stretching it to sentences would blur its name.
- **The hint depends on the status code** (3): no status (connection refused) means Ollama isn't running or the URL is wrong. 404 means the model isn't pulled.
- **Colour by the printed value** (3): the thresholds are compared after rounding to 2 decimals, so every cell that shows 0.40 is grey. Before, 0.401 printed as 0.40 but stayed uncoloured next to a grey 0.40.
- **Tests in `AiLab.Api.Tests`**, next to `EmbeddingGeneratorFactoryTests`; the fake goes in `Fakes/` with `ScriptedChatClient`.

## Out of scope

- pgvector, chunking, `/ask`; any change to the similarity algorithm; `experiments.md`.

## Changes

- **2026-10-26:** Done. 62 tests pass (53 before, 8 for `SimilarityService`, 1 for startup validation).
  - **Bug found:** with `services.AddSingleton<SimilarityService>()`, `dotnet run --project AiLab.Api` crashed at startup: in Development, ASP.NET Core checks every type registration (`ValidateOnBuild`), and the API has no `IEmbeddingGenerator`. The tests missed it because `ApiFactory` runs as "Testing". The registration is now a factory lambda, which is only checked when something asks for it. A new test runs the check itself; it failed on the old line and passes on the new one. The API now starts in Development.
  - **Hint fix:** a missing model first printed both hints ("is Ollama running?" and "ollama pull"): OllamaSharp reports it as an `HttpRequestException` with 404. The hint now uses the status code.
  - **Colour fix:** see "Colour by the printed value" above.
  - **Real run:** 10 sentences, 768 dimensions, 120 tokens, 809 ms on the first call (model load), 120 ms on the next. The calls are in the `ai-` log ("GenerateAsync invoked", "generated 10 embedding(s)"). The output is in `README.md`; the observations are the user's to write in `experiments.md`.
