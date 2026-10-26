# Stage 4 embedding infrastructure: a keyed embedding generator, its own config, logging

- **Date:** 2026-10-26
- **Branch:** `stage-4-embeddings`
- **Goal:** Wire up `IEmbeddingGenerator<string, Embedding<float>>` for stage 4, separate from chat, so Monday's similarity matrix only has to ask for it. Ollama only (`nomic-embed-text`, 768 dimensions).
- **Status:** done (not committed: the user reviews the diff and commits)

Claude writes the plumbing (the user's request overrides tutor mode for it). `SimilarityService`, `SimilarityMatrix` and the `/similarity` command are the user's, and Claude doesn't touch them.

## Steps

- [x] **1** Review what's started: `EmbeddingGeneratorFactory`, `AddInfrastructure`, the console's `appsettings.json` and `Program.cs`. Keep what's right.
- [x] **2** Config: `Embedding:Provider` / `Embedding:Model` / `Embedding:OllamaUrl` in `AiLab.Console/appsettings.json` (`ollama`, `nomic-embed-text`, `http://localhost:11434`).
- [x] **3** `EmbeddingGeneratorFactory`, shaped like `ChatClientFactory`: a separate `OllamaApiClient` with the embedding model. A missing or empty provider or model, `anthropic` or an unknown provider throws with the key to set.
- [x] **4** `AddInfrastructure` registers the keyed `"embedding-provider"` singleton, read on first resolve.
- [x] **5** Console `Program.cs`: `AddEmbeddingGenerator(keyed).UseLogging()`.
- [x] **6** `System.Numerics.Tensors` in `AiLab.Application` (for `TensorPrimitives.CosineSimilarity`).
- [x] **7** `dotnet build`, `dotnet test` (43 still pass), plus offline factory tests.
- [x] **8** Docs: `CLAUDE.md` (Stack, Stage 4 checklist), `README.md` (requirements, embedding config, project structure), this plan. `experiments.md` is the user's.

## Tests

- `AiLab.Api.Tests/EmbeddingGeneratorFactoryTests.cs`, offline (creating an `OllamaApiClient` makes no call):
  - valid ollama config, for `nomic-embed-text` and `all-minilm` (model and URL read back from `EmbeddingGeneratorMetadata`)
  - missing / empty / blank model, missing provider: message names the key
  - unknown provider, `anthropic` (message says it has no embeddings API)
  - `AddInfrastructure` with no `Embedding` section builds, and fails only when the generator is resolved; with the section, it resolves the right model

## Decisions & trade-offs

- **Lazy, not skipped** (4): the keyed factory runs only when something resolves the generator. The API never does, so it needs no `Embedding` section, and the tests stay offline. Skipping the registration when the section is missing would turn a clear "Embedding:Model is missing" into a vague "no service registered".
- **Checks in the factory, not in `AddInfrastructure`** (3): unlike `ChatClientFactory`, where `AddInfrastructure` checks with `??`. Here the factory checks, so empty strings fail too (`??` lets `""` through) and the tests can call the factory directly.
- **`OllamaUrl` optional** (3): missing or blank means `http://localhost:11434`, like `Chat:OllamaUrl`. Only provider and model are required.
- **Kept the `ollamaHttpClient` parameter, dropped the Anthropic ones** (3): when `POST /ask` arrives in the API, embeddings can use the same Polly-wrapped `"ollama"` HttpClient as chat. Anthropic's timeout and retry parameters had nothing to do.
- **No `Embedding:Dimensions`** (3): the model returns its own size. pgvector (Tue) needs the size for the column, so that's where a setting may come in.
- **Tests in `AiLab.Api.Tests`**: it already holds plain unit tests (`TicketToolsTests`, `TicketScorerTests`) and references Infrastructure, so there was no need for a new project.

## Out of scope

- `SimilarityService`, `SimilarityMatrix`, `/similarity` (the user's), the API's `Embedding` section, pgvector.

## Changes

- **2026-10-26:** Done.
  - **Bug found:** the started registration was keyed as the non-generic `IEmbeddingGenerator`, but `Program.cs` resolved `IEmbeddingGenerator<string, Embedding<float>>`, so resolving it would have failed. Both now use the generic type, and the factory returns it.
  - **Started code that was dropped:** the Anthropic parameters copied from `ChatClientFactory`, and the generic "not supported yet" message.
  - **Build:** the solution doesn't build yet because of `SimilarityService.cs` (in progress, the user's): `Embedding<float>.Vector` is a `ReadOnlyMemory<float>`, not a `float[]`. To check the rest, that file was moved aside for one build and test run and put back unchanged (same checksum). With it aside: build clean, 53 tests pass (43 old, 10 new).
  - Not run against a live Ollama: nothing calls the generator until `/similarity` exists.
