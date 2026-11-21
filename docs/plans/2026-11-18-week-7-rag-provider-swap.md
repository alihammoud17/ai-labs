# RAG Provider Swap Day Plan: Prove the Provider Swap on a Real App

- **Date:** Wed 18 Nov 2026 (Week 7, theme Bedrock via MEAI), worked later on aws-emulator
- **Branch:** `stage-7-rag-provider-swap` (from `main`), not created yet
- **Status:** not started
- **Focus:** Prove the provider swap on a real app: `AiLab.Rag` on Bedrock chat **and** Bedrock embeddings, switched by configuration only
- **Where:** `AiLab.Infrastructure` (embedding factory, vector store), `AiLab.Rag` (config), `docker/aws-setup/` (policy), tests

Tutor mode: I type the commands and write the code; Claude explains each step, reviews it (build, tests) and ticks the boxes. Claude writes a step's code only when I say "hand it over".

After each finished step, Claude adds a short **How we did it** note under it when I ask.

## Where things stand

| Block | Status |
|---|---|
| 1. Concepts + emulator check | done (handed over, 2026-11-21) |
| 2. Bedrock embeddings in the factory | done (handed over, 2026-11-21) |
| 3. Vector size from configuration | not done |
| 4. One switch: a Bedrock profile for `AiLab.Rag` | not done |
| 5. Tests (offline, real SDK path) | not done |
| 6. Policy + real-account runbook (D2 ready) | not done |
| 7. Housekeeping | not done |

**Minimum:** 2 → 3 → 4 → 5 → 7. Block 6 makes D2 a 10-minute job on the real account.

## Review (2026-11-20, before Block 1): findings and refinements R1–R11, all accepted

Checked against the repo and the decompiled packages (AWS.Bedrock.MEAI 1.1.3, CommunityToolkit.VectorData.* 1.0.1, Microsoft.Extensions.VectorData.Abstractions 10.8.2).

**Findings in the code:**
- `768` sits in 4 app files (`TextChunkRecord`, `TextVectorStore`, `PostgresKeywordSearch`, `InMemoryKeywordSearch`) and 4 test files (`HashedEmbeddingGenerator`, `KeywordSearchTests`, `TextVectorStoreTests`, `VectorSearchServiceTests`).
- `RagExceptionHandler` doesn't use `IProviderErrorClassifier`, and `AskService` uses it only around the model call. So a Bedrock embedding failure (`ModelErrorException`) is a **500** in `/dev/ingest`, `/search` and `/ask` today.
- `/dev/*` and Swagger are mapped only when `IsDevelopment()`. `dotnet run` uses the `http` launch profile, which sets `Development` over the shell's value.
- `Bedrock` doesn't load user-secrets, where `ConnectionStrings:Vectors` lives.
- No endpoint shows the collection name or vector size.
- `SyncAsync` calls `EnsureCreatedAsync` before embedding, so a failed ingest leaves an **empty** collection.
- `/ask` embeds the question first, so on the emulator it never reaches Bedrock chat while embeddings are Titan.
- Rag's `appsettings.json` chat default is `anthropic` / Haiku since week 5; only Development uses Ollama. Embeddings default to Ollama.
- The in-memory connector never checks vector size, so "it accepts 1024" proves nothing.
- `BedrockEmbeddingGenerator`: one `InvokeModel` per text, one after another. Body `{inputText, dimensions}`; `dimensions` is left out when null; `normalize` is never sent (Titan's default is true). Reads `{embedding, inputTextTokenCount}` and adds up input/total tokens. A response without `embedding` is skipped silently; `IndexAsync`'s count check catches that.
- VectorData: a definition passed to `GetCollection` is merged **per property** with the attributes, and the definition wins (it always sets `Dimensions`). Both connectors pass it to `CollectionModelBuilder.Build`. Postgres creates `vector(N)` (HNSW up to 2000). `CREATE TABLE IF NOT EXISTS` never changes an existing table.

**Refinements (placed in the blocks below):**
- **R1** One internal `BedrockRuntimeClientFactory` (`Providers/`) builds every `AmazonBedrockRuntimeClient` from a `BedrockClientOptions` record; both factories call it (Block 2).
- **R2** A `bedrock` launch profile (Block 4).
- **R3** User-secrets also for the `Bedrock` environment (Block 4).
- **R4** Dev endpoints also for `Bedrock` (Block 4).
- **R5** `RagExceptionHandler` maps provider failures through the classifier: 502 `embedding_unavailable`, with a hint per `Embedding:Provider` (Block 2.3).
- **R6** One startup log line with the collection, vector size and both providers (Block 4).
- **R7** A partial definition (the vector property only) plus a `VectorStoreSettings(Collection, Dimensions)` record (Block 3).
- **R8** The chat half of D1: Bedrock chat with Ollama embeddings on `corpus`, read only (Block 4).
- **R9** The size test on Postgres instead of in memory (Block 5).
- **R10** Titan in its own policy statement, `InvokeModel` only (Block 6).
- **R11** A failed ingest says the empty collection was left behind (Block 4).

## Deliverables (adapted: embeddings can't run on the emulator)
- [ ] **D1. RAG app switched to Bedrock chat + Bedrock embeddings by configuration only:** after today's one-time code changes, `AiLab.Rag` runs on Bedrock for both by setting one environment (`ASPNETCORE_ENVIRONMENT=Bedrock`) and nothing else. Proven on the emulator for chat; proven offline (real SDK, fake HTTP) for embeddings.
- [ ] **D2. Corpus re-embedded:** **ready, not run.** The embedding path, a separate collection, the IAM permission and a step-by-step runbook are in place; the run itself happens on the real account (it takes minutes there).

Why D2 can't run today: aws-emulator's Bedrock Runtime has no embedding model family. An `InvokeModel` call to Titan Text Embeddings returns `ModelErrorException` ("No model runs in this … emulator, so it serves no embedding inference"). Block 1 confirms it.

## Resources
- AWS.Bedrock.MEAI (NuGet): https://www.nuget.org/packages/AWS.Bedrock.MEAI/
- Its embedding generator (source): https://github.com/aws/aws-dotnet-ai/tree/main/src/AWS.Bedrock.MEAI (`BedrockEmbeddingGenerator.cs`)
- Bedrock User Guide, Amazon Titan Text Embeddings (request body, dimensions): https://docs.aws.amazon.com/bedrock/latest/userguide/titan-embedding-models.html
- Microsoft.Extensions.VectorData, defining the schema in code (record definition): https://learn.microsoft.com/dotnet/ai/vector-stores/
- Tuesday: `experiments.md` section 22; Monday: `docker/aws-setup/README.md`

## Scope rules for today
- **No AWS account, no Anthropic calls.** Bedrock chat runs on aws-emulator (`https://127.0.0.1:9297`, account `000000000000`) after the safety check. Bedrock embeddings are tested offline.
- **"By configuration only" is the test.** Today's code changes are one-time and provider-neutral (an embedding `case`, a configurable vector size). After them, switching Ollama ↔ Bedrock must need no code change. If a step needs a code change per provider, it's a finding.
- **Never mix vectors from two models.** Bedrock embeddings go to their own collection (`corpus_titan_v2_1024`); the Ollama `corpus` stays untouched, so you can switch back at any time.
- **The defaults don't change:** Ollama embeddings in `appsettings.json`; chat stays `anthropic`/Haiku in `appsettings.json` (week 5) and Ollama in `appsettings.Development.json`.
- **Known emulator limit:** `/ask` on Nova Lite returns `502 reply_unreadable` (the emulator fills the forced tool's input with `{}`; section 22). For the chat half of D1, reaching Bedrock and mapping the result correctly is the proof.

---

## Block 1: Concepts + emulator check (20 min) — done (handed over, 2026-11-21)

**What Block 1 delivers** (no code, nothing committed except ticks):
- **B1a. A list of every place "config only" can break**, so Blocks 2–4 have a checklist. Five items: vector size, collection content, task prefixes, distance gate, content-hash skip. For each: what breaks, and whether today fixes it in code or by config.
- **B1b. Titan v2's contract in your own words**: model ID, allowed sizes, request and response body, one call per text. Blocks 2 and 5 are built on it (the factory's `dimensions`, the fake HTTP answer in the test).
- **B1c. The emulator's exact answer to a Titan call**, copied word for word. It's the evidence for "D2: ready, not run" in section 23. If a vector comes back instead, D2 runs today.

B1a and B1b go in your own notes (outside the repo, like earlier weeks). B1c waits in your notes until Block 7 puts it in `experiments.md` section 23.

- [x] **Two providers, two settings.**
  - **What to do:** note that `Chat:*` and `Embedding:*` are separate sections (Embedding was split from Chat in week 4 because Claude can't embed). Bedrock can do both, but they stay two settings: you could run Bedrock chat with Ollama embeddings.
  - **Why:** "switch the RAG app" means switching both sections and the collection, not one key.
  - **Sub-steps:**
    1. Open `AiLab.Rag/appsettings.json` and `appsettings.Development.json`. Write down which file sets `Chat:*`, which sets `Embedding:*`, and the values each environment ends up with. Development: chat `ollama`/`llama3.2`, embeddings `ollama`/`nomic-embed-text`. Base: chat `anthropic`/Haiku, embeddings Ollama.
    2. In `AiLab.Infrastructure/DependencyInjection.cs`, find the two keyed singletons (`ChatClientKeys.Provider`, `EmbeddingGeneratorKeys.Provider`). Note that each reads only its own section.
    3. Write the three keys a full switch touches: `Chat:Provider`/`Model`, `Embedding:Provider`/`Model` (plus `Dimensions` from today), and `VectorStore:Collection`. Then one sentence: why the collection is part of the switch.
    4. Check yourself: "Bedrock chat + Ollama embeddings on `corpus`": which keys change? (Only `Chat:*`. That's R8's run in Block 4.)
  - **Done (handed over):** two keyed singletons in `DependencyInjection.cs`, each reading only its own section: `ChatClientKeys.Provider` reads `Chat:*`, `EmbeddingGeneratorKeys.Provider` reads `Embedding:*`.
    - Rag's base `appsettings.json`: chat `anthropic`/`claude-haiku-5-5`, embeddings `ollama`/`nomic-embed-text`. `appsettings.Development.json` overrides only `Chat` (`ollama`/`llama3.2`).
    - A full switch touches `Chat:Provider`/`Model`, `Embedding:Provider`/`Model`/`Dimensions` and `VectorStore:Collection`. The collection is part of it because the stored vectors belong to one embedding model; searching them with another model's query vector compares two unrelated spaces.
    - Bedrock chat + Ollama embeddings on `corpus` changes `Chat:*` only (R8's run).
- [x] **What an embedding swap really changes.**
  - **What to do:** list what depends on the embedding model in `AiLab.Rag`: the vector size (`TextChunkRecord.Dimensions = 768`, hard-coded), the collection's content (vectors from one model only), `Embedding:UseTaskPrefixes` (nomic-only), `Ask:MaxDistance` (0.50, tuned on nomic's distances), the content-hash skip in ingestion (it can't see a model change).
  - **Why:** each of these is a place where "config only" can break. Today fixes the first; the others are config.
  - **Sub-steps** (one line per item in your notes: *what breaks* → *fix: code today / config / runbook*):
    1. **Vector size:** `grep -rn "TextChunkRecord.Dimensions" --include='*.cs' .` and count the app and test files (the review found 4 + 4). Breaks: a 1024 vector is refused by `CheckDimensions`, or by Postgres's `vector(768)` column. Fix: code, Block 3.
    2. **Collection content:** why can't nomic and Titan vectors share a table, even at the same size? (Distances between two models' spaces mean nothing.) Fix: config, its own `VectorStore:Collection`.
    3. **Task prefixes:** read the comment at `VectorSearchService.cs:25`. Breaks: Titan would embed `search_document: ` as plain text. Fix: config, `false`.
    4. **Distance gate:** look at `AskService.cs:43`. Breaks: 0.50 was tuned on nomic's distances, so Titan may refuse everything or let everything through. Fix: runbook, re-tuned on the real account (Block 6 step 6).
    5. **Content-hash skip:** look at `TextChunk.ContentHash` (`AiLab.Domain/DTOs/TextChunk.cs:19`). It hashes the text only. Breaks: re-ingesting the *same* collection with a new model embeds nothing. Fix: config (a new collection starts empty) or `force=true`.
  - **Done (handed over):** what breaks → how today fixes it:
    1. **Vector size:** `TextChunkRecord.Dimensions` in 4 app files (`TextChunkRecord`, `TextVectorStore`, `PostgresKeywordSearch`, `InMemoryKeywordSearch`) and 4 test files. A 1024 vector is refused by `CheckDimensions`, or by Postgres's `vector(768)` column. → **code**, Block 3.
    2. **Collection content:** cosine distance between vectors from two models means nothing, even at the same size. → **config**: its own `VectorStore:Collection`.
    3. **Task prefixes:** `search_document: ` / `search_query: ` are nomic's training markers (`VectorSearchService.cs:25`); Titan would embed them as plain words. → **config**: `false`.
    4. **Distance gate:** `Ask:MaxDistance` 0.50 (`AskService.cs:43`) was tuned on nomic's distances; Titan's spread is unknown, so it may refuse everything or let everything through. → **runbook**: re-tune on the real account (Block 6 step 6).
    5. **Content-hash skip:** `TextChunk.ContentHash` hashes the text only, so re-ingesting the *same* collection with a new model embeds nothing and leaves old vectors. → **config**: a new collection starts empty (or `force=true`).
- [x] **Titan Text Embeddings v2, the target model.**
  - **What to do:** from the Titan page, note: model `amazon.titan-embed-text-v2:0`, `dimensions` 256, 512 or 1024 (default 1024), the request body `{ "inputText", "dimensions" }`. Note that `BedrockEmbeddingGenerator` sends exactly that body, **one `InvokeModel` per text** (no batching).
  - **Why:** 768 isn't a Titan size, so the store's size must change; and ingestion makes one call per chunk.
  - **Sub-steps:**
    1. Read the Titan page (Resources). Note the model ID, the three sizes, the default, `normalize` (default true), the input limit (8,192 tokens), and the response fields `embedding` and `inputTextTokenCount`.
    2. Read `AsIEmbeddingGenerator` in the package's XML docs (`~/.nuget/packages/aws.bedrock.meai/1.1.3/lib/net8.0/AWS.Bedrock.MEAI.xml`, search `AsIEmbeddingGenerator`). Note its parameters `(model, defaultModelDimensions)` and that `EmbeddingGenerationOptions.Dimensions` overrides the default per call.
    3. Note what the review found in the package code (Review section above): `dimensions` is left out when null, `normalize` is never sent, one call per text one after another, and a reply without `embedding` is skipped silently.
    4. Work out the cost of one ingest: heading 50 + fixed 21 chunks = 71 `InvokeModel` calls per full run (Ollama: 3 batched calls). Why does that matter for timeouts and throttling on the real account?
    5. Check yourself: why can't `Embedding:Dimensions` stay 768 for Titan?
  - **Done (handed over):** Titan v2 contract:
    - Model `amazon.titan-embed-text-v2:0`; `dimensions` 256 / 512 / 1024 (default 1024); `normalize` default true; input up to 8,192 tokens.
    - Request `{"inputText","dimensions","normalize"}`; response `{"embedding": float[], "inputTextTokenCount"}`.
    - `AsIEmbeddingGenerator(model, defaultModelDimensions)`; `EmbeddingGenerationOptions.Dimensions` overrides the default per call. The metadata reports `aws.bedrock`, the model and the dimensions.
    - From the package code: `dimensions` is left out when null; `normalize` is never sent (so true, fine for cosine); one `InvokeModel` per text, one after another; a reply without `embedding` is skipped silently (`IndexAsync`'s count check catches it).
    - Cost of a full ingest: heading 50 + fixed 21 = **71 calls** (Ollama: 3 batched calls), each with its own timeout, retries and throttling budget on the real account.
    - 768 isn't a Titan size: the store must take its size from config.
- [x] **Confirm the emulator gap** (safety check first):
  ```bash
  aws bedrock-runtime invoke-model --model-id amazon.titan-embed-text-v2:0 \
    --body '{"inputText":"hello","dimensions":1024}' --cli-binary-format raw-in-base64-out "$TMPDIR/emb.json"
  ```
  - **What to do:** expect `ModelErrorException` ("…serves no embedding inference"). Write the exact message down.
  - **Why:** it's the reason D2 is "ready, not run". If the emulator does return a vector, D2 runs today: tell Claude.
  - **Sub-steps:**
    1. Start the emulator if it's down (it was on 2026-11-20: `~/.aws/emulator/9297/` was empty): `docker/bedrock-kb/bedrock-kb-emulator.sh`. Wait until it has written `~/.aws/emulator/9297/ca.pem` and the credentials file.
    2. In the same terminal: `source ~/.aws/emulator/ai-lab-kb.env`, then the safety check from `docker/aws-setup/README.md`. Only `SAFE` lets you go on; `STOP` (or `NoCredentials`) means stop here.
    3. Check that the model exists in the emulator's catalog: `aws bedrock list-foundation-models --query "modelSummaries[?contains(modelId,'titan-embed')].modelId"`. If it exists, a failure is about inference, not about a missing model.
    4. Run the `invoke-model` call above. Copy the full error: the exception name, the HTTP status if shown, and the message.
    5. Run it once more without `dimensions` (`--body '{"inputText":"hello"}'`) to see whether the answer changes.
    6. If a vector does come back: count it with `jq '.embedding | length' "$TMPDIR/emb.json"` and tell Claude. D2 would then run today.
  - **Done (handed over):** the emulator was down (`~/.aws/emulator/9297/` empty since 2026-11-20 23:06). Restarted with `docker/bedrock-kb/bedrock-kb-emulator.sh` (its own account guard passed; new KB `GOKDRLFgzj`, `ai-lab-kb.env` rewritten). Safety check `SAFE`.
    - Titan v2 is in the catalog (with Titan v1, G1, Image, Cohere v3/v4).
    - Both calls (with and without `dimensions`) give HTTP 424 `ModelErrorException`, "No model runs in this Amazon Bedrock Runtime emulator, so it serves no embedding inference for amazon.titan-embed-text-v2:0." So D2 stays "ready, not run".
    - Full answer in `experiments.md` section 23.

---

## Block 2: Bedrock embeddings in the factory (45 min) — done (handed over, 2026-11-21)

### 2.1 Branch
- [x] `git switch main && git pull && git switch -c stage-7-rag-provider-swap`, then commit this plan on the branch (it's untracked on `main`)
  - **Done (handed over):** branch `stage-7-rag-provider-swap` from an up-to-date `main`; first commit `cd06e5b` holds this plan and `experiments.md` section 23's start.

### 2.2 A `bedrock` case in `EmbeddingGeneratorFactory`
- [x] **R1, first:** move `ChatClientFactory.CreateBedrock`'s client building into `AiLab.Infrastructure/Providers/BedrockRuntimeClientFactory.cs` (`internal static AmazonBedrockRuntimeClient Create(BedrockClientOptions options)`). `BedrockClientOptions` is a record (`Region`, `ServiceUrl`, `EmulatorCaPath`, `AttemptTimeout`, `MaxRetries`), filled once in `AddInfrastructure` from `Bedrock:*` and the resilience settings. `ChatClientFactory` takes `BedrockClientOptions?` instead of its 5 `bedrock*` parameters, and the existing `ChatClientFactoryTests` stay green.
  - **Why:** one place builds AWS clients, so Tuesday's TLS, timeout and endpoint rules apply to embeddings for free.
  - **Done (handed over):**
    - `BedrockClientOptions` is a public positional record, every value optional (`new()` = real AWS, us-east-1, SDK defaults). Public, because `AddInfrastructure` and the tests build it.
    - `BedrockRuntimeClientFactory.Create` is internal and holds `CreateBedrock`'s old body word for word, minus `.AsIChatClient(model)`.
    - `ChatClientFactory.Create` takes `BedrockClientOptions? bedrock` instead of 5 `bedrock*` parameters.
    - `AddInfrastructure` builds the options once in a private `BedrockOptions(config, resilience)`, which 2.3 reuses for embeddings.
    - `ChatClientFactoryTests`: only the calls changed, not what they assert. 371 tests pass (as before).
- [x] Add `"bedrock"`: `BedrockRuntimeClientFactory.Create(options).AsIEmbeddingGenerator(model, dimensions)`.
  - **Done (handed over):** in `EmbeddingGeneratorFactory`; `AsIEmbeddingGenerator(model, defaultModelDimensions)` from AWS.Bedrock.MEAI (namespace `Amazon.BedrockRuntime`).
- [x] New parameter: **dimensions** (`Embedding:Dimensions`). Required for Bedrock (Titan has no 768); ignored by Ollama (its model decides).
  - **Done (handed over):** `int? dimensions` and `BedrockClientOptions? bedrock`, both optional, so every existing caller compiles unchanged. Bedrock without it → `InvalidOperationException` naming `Embedding:Dimensions`; zero or less → refused. The factory doesn't check Titan's sizes (256/512/1024): other Bedrock models (Cohere) have their own, and Bedrock answers a wrong one with a `ValidationException`.
- [x] Update the messages and comments that say "only ollama is supported", and the `"anthropic"` message's suggestion.
  - **Done (handed over):** the missing-provider, unknown-provider and anthropic messages now say `"ollama" or "bedrock"`; the missing-model message gives an example for each provider; the class comment explains who decides the size.

### 2.3 DI
- [x] In `AddInfrastructure`, pass `Embedding:Dimensions` and the `Bedrock:*` values to the embedding factory.
  - **Done (handed over):** `dimensions: config.GetValue<int?>("Embedding:Dimensions")` (missing = null, which Ollama ignores and Bedrock refuses) and `bedrock: BedrockOptions(config, resilience)`, the same method the chat client uses. Still read on first resolve, so hosts without an `Embedding` section are unaffected.
- [x] **R5:** `RagExceptionHandler` doesn't use the classifier today, so a Bedrock embedding failure is a 500. Inject `IProviderErrorClassifier` and check every exception down the `InnerException` chain. A provider failure becomes 502 `embedding_unavailable`, with a hint per `Embedding:Provider`: Ollama keeps today's two hints; Bedrock says "check `Embedding:Model`, model access and the `Bedrock:*` endpoint". Order: `ModelCallException` first, then `NpgsqlException`, then provider failures. One offline test in `AiLab.Rag.Tests`: an embedding generator that throws an `AmazonServiceException` → 502 `embedding_unavailable`. Confirm live in Block 4.
  - **Why:** Rag stays free of AWS types, and AccessDenied on the real account becomes a 502 too.
  - **Done (handed over):**
    - `RagExceptionHandler` takes `IProviderErrorClassifier` and asks it about every exception in the chain. The first match becomes 502 `embedding_unavailable`. This replaces the old `HttpRequestException`-only branch, so Ollama's two hints (not running / not pulled) are kept, now in `EmbeddingHint`.
    - Bedrock's hint names `Embedding:Model`, model access, `Bedrock:Region` / `Bedrock:ServiceUrl` and credentials. The provider's own message stays in the log only.
    - Order: `ModelCallException` → `NpgsqlException` (its timeouts wrap a `TimeoutException`, which the classifier would also match) → provider failure.
    - Side effect, intended: an `OllamaException`, or an Ollama timeout (`TaskCanceledException` wrapping `TimeoutException`), is now a 502 instead of a 500.
    - Tests (3 new, offline, the emulator's real `ModelErrorException` text with status 424): `/dev/seed` and `/search` → 502 with the Bedrock hint and no Ollama wording; `/ask` → 502 before any model call. With the old handler all 3 fail with 500 (checked). 374 tests pass (371 + 3).

---

## Block 3: Vector size from configuration (60 min) — not done

The one change that makes the swap config-only. Today `768` is a `const` used in three places.

- [ ] **The record:** `TextChunkRecord.Dimensions` is a compile-time constant inside the `[VectorStoreVector]` attribute, so no config can change it.
  - **What to do (R7):** keep the record and its attributes, and rename the constant to `DefaultDimensions = 768` (the attribute and the tests keep using it). In `VectorStoreServices`, pass a partial definition: `new VectorStoreCollectionDefinition { Properties = [new VectorStoreVectorProperty(nameof(TextChunkRecord.Embedding), typeof(ReadOnlyMemory<float>?), dimensions)] }` to `GetCollection<string, TextChunkRecord>(collection, definition)`. Cosine distance and HNSW still come from the attribute.
  - **Why:** the model builder merges per property and the definition wins (checked in the decompiled 10.8.2 builder), so one property is enough.
- [ ] **The three checks (R7):** `TextVectorStore.CheckDimensions`, `PostgresKeywordSearch` and `InMemoryKeywordSearch` compare against the constant. Inject the configured size instead: `VectorStoreSettings(string Collection, int Dimensions)` (Infrastructure), built once in `AddVectorStore` from `VectorStore:Collection` (default `corpus`) and `Embedding:Dimensions` (default 768; zero or less refused at startup).
- [ ] **Known trap, comment only:** `CREATE TABLE IF NOT EXISTS` never changes an existing table. 1024 with `Collection=corpus` makes Postgres refuse the insert, and that shows as 503 "Is Postgres running?". The rule (a new size means a new collection) goes in the comment and the error text.
- [ ] **The error messages** that say "change TextChunkRecord.Dimensions": now "set `Embedding:Dimensions` and use a new `VectorStore:Collection`".
- [ ] **Regression:** with no new settings, everything behaves exactly as before (768, `corpus`). `dotnet test` green; one `/search` on the existing Ollama collection returns the same top 3 as before.
  - **Why:** the swap must not change the default path.

---

## Block 4: One switch for `AiLab.Rag` (40 min) → D1 — not done

- [ ] **`AiLab.Rag/appsettings.Bedrock.json`** (no secrets, committed): the keys that differ from the default.
  | Key | Value |
  |---|---|
  | `Chat:Provider` / `Chat:Model` | `bedrock` / `us.amazon.nova-lite-v1:0` (real account: `us.anthropic.claude-haiku-5-5`) |
  | `Embedding:Provider` / `Embedding:Model` | `bedrock` / `amazon.titan-embed-text-v2:0` |
  | `Embedding:Dimensions` | `1024` |
  | `Embedding:UseTaskPrefixes` | `false` (nomic-only) |
  | `VectorStore:Collection` | `corpus_titan_v2_1024` |
  | `Bedrock:Region` | `us-east-1` |
  | `Ask:MaxDistance` | keep 0.50 for now, marked "re-tune on the real account" |
  - **Emulator-only keys** (`Bedrock:ServiceUrl`, `Bedrock:EmulatorCaPath`) stay out of this file: pass them as environment variables, so the file is also the real-account config.
  - **Why:** ASP.NET Core loads `appsettings.{Environment}.json` on top of `appsettings.json`, so `ASPNETCORE_ENVIRONMENT=Bedrock` is the one switch. That's "configuration only" in its plainest form.
  - **Connection string (R3), decided:** user-secrets for `Bedrock` too. In `Program.cs`: `if (builder.Environment.IsEnvironment("Bedrock")) builder.Configuration.AddUserSecrets<Program>(optional: true);`. Note it: added this way, secrets come after the environment variables and win over them. Harmless, since secrets hold only the connection string and keys. This works on the real account too.
- [ ] **R2:** a `bedrock` profile in `AiLab.Rag/Properties/launchSettings.json` (`ASPNETCORE_ENVIRONMENT=Bedrock`, `http://localhost:5074`). `dotnet run` otherwise applies the `http` profile, whose `Development` overrides the shell's value.
- [ ] **R4:** dev endpoints and Swagger when `IsDevelopment() || IsEnvironment("Bedrock")` (the runbook needs `/dev/ingest` on the real account). Name the environment once (e.g. a `RagEnvironments.Bedrock` constant).
- [ ] **R6:** one log line at startup: `Vector store {Provider}/{Collection}, {Dimensions} dims; embeddings {EmbeddingProvider}/{EmbeddingModel}; chat {ChatProvider}/{ChatModel}`.
- [ ] **R11:** when ingest fails after `EnsureCreatedAsync`, an empty collection is left behind. Say so: the Bedrock hint from R5 adds "the collection may be left empty; ingest again when the provider works". No rollback: an empty table is harmless, and on a mid-way failure the content hash lets the next run carry on.
- [ ] **Run it on the emulator** (safety check first; Postgres container up):
  ```bash
  source ~/.aws/emulator/ai-lab-kb.env
  Bedrock__ServiceUrl=https://127.0.0.1:9297 Bedrock__EmulatorCaPath=$HOME/.aws/emulator/9297/ca.pem \
    dotnet run --project AiLab.Rag --launch-profile bedrock
  ```
- [ ] **What to check, in order:**
  1. Startup works, and the R6 line shows `corpus_titan_v2_1024`, 1024, `bedrock/amazon.titan-embed-text-v2:0`, `bedrock/us.amazon.nova-lite-v1:0`.
  2. `POST /dev/ingest` → 502 `embedding_unavailable` (R5), not 500. Record the response; `corpus_titan_v2_1024` exists and is empty (R11); `corpus` is untouched (row count before = after).
  3. `POST /ask` with Titan embeddings → the same 502 at retrieval: it never reaches chat on the emulator.
  4. **The chat half (R8):** start again with the Bedrock profile plus `Embedding__Provider=ollama Embedding__Model=nomic-embed-text Embedding__Dimensions=768 VectorStore__Collection=corpus`. This reads `corpus` and never writes to it: **no `/dev/*` calls in this run**. `POST /ask` with an answerable question → reaches Bedrock chat. Expect 502 `reply_unreadable` (Nova on the emulator fills the forced tool with `{}`, section 22), or a gate abstain. The log shows the Bedrock call. It shows chat and embeddings are two independent settings.
  5. Stop, start with the default profile: the Ollama `corpus` answers as before.
- [ ] Record the five results for `experiments.md`. ✅ **D1** (chat on the emulator, embeddings in Block 5).

---

## Block 5: Tests, offline, through the real SDK (45 min) — not done

- [ ] **Bedrock embeddings without a network:** an `AmazonBedrockRuntimeClient` with fake credentials (`BasicAWSCredentials("test", "test")`) and an `HttpClientFactory` whose handler answers every `InvokeModel` with a Titan-shaped body (`{"embedding":[…1024 floats…],"inputTextTokenCount":3}`).
  - **Assert:** `GenerateAsync(["a", "b"])` returns 2 embeddings of 1024 floats, usage = 6 input tokens, and the handler saw 2 requests with `"dimensions":1024` in the body.
  - **Why:** the real package and the real SDK run (signing, serialization, response parsing); only the HTTP answer is fake. That's as close to Bedrock as you can get without an account.
- [ ] **Factory:** `EmbeddingGeneratorFactory.Create("bedrock", …)` without `Embedding:Dimensions` fails with a message that names the key; with it, `EmbeddingGeneratorMetadata` shows the model and 1024.
- [ ] **Store size from config:** with `VectorStoreSettings.Dimensions = 1024`, `CheckDimensions` (both keyword searches too) refuses a 768 vector, naming `Embedding:Dimensions` and `VectorStore:Collection`, and accepts a 1024 one. Not "the in-memory collection accepts 1024": that connector never checks the size.
- [ ] **R9, the real schema:** extend the opt-in Postgres test (`AILAB_PG=1`) with a table `chunks_test_1024` built through the definition, then read the column type: `SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = '"chunks_test_1024"'::regclass AND attname = 'Embedding'` → `vector(1024)`.
- [ ] `dotnet test` on the whole solution: green.

---

## Block 6: Policy + real-account runbook (30 min) → D2 ready — not done

- [ ] **IAM (R10):** `AiLabBedrockInvoke` allows only Nova Lite and Haiku. Add a **second statement** (`Sid` `AllowBedrockEmbeddings`) with `bedrock:InvokeModel` only on `arn:aws:bedrock:us-east-1::foundation-model/amazon.titan-embed-text-v2:0`. Embeddings don't stream; in the first statement Titan would also get `InvokeModelWithResponseStream`. The simulator rows add Titan v2 + `InvokeModelWithResponseStream` → `implicitDeny`.
  - **Check on the emulator:** `simulate-custom-policy` with the `jq -Rs '[.]'` form: Titan v2 `allowed`, Titan v1 (`amazon.titan-embed-text-v1`) `implicitDeny`. Add the rows to the README table.
  - **Why:** without it, ingestion fails with AccessDenied as `ai-lab-dev` on day one.
- [ ] **Runbook** in `docker/aws-setup/README.md`, section "Re-embed the corpus on Bedrock":
  1. Safety check reversed: the account is the real one, profile `ai-lab-dev`.
  2. `dotnet run --project AiLab.Rag --launch-profile bedrock` (no emulator variables; `unset AWS_ENDPOINT_URL AWS_CA_BUNDLE`, because the SDK copies `AWS_ENDPOINT_URL` into `ServiceURL`: section 22).
  3. `POST /dev/ingest` once; record chunk count, calls, tokens, time.
  4. Collection counts: `corpus_titan_v2_1024` has as many chunks per chunker as `corpus`.
  5. Retrieval eval (`AiLab.Evals` retrieval runner) against both collections: hit@1/5, MRR, nomic vs Titan.
  6. Re-tune `Ask:MaxDistance` from the Titan distances; put the value in `appsettings.Bedrock.json`.
  7. Cost: tokens × Titan v2's price from the Bedrock pricing page (confirm the number that day).
  - **Why:** D2 becomes a short, checked run instead of a research day.

---

## Block 7: Housekeeping (30 min) — not done

- [ ] `experiments.md` section 23: "The provider swap on `AiLab.Rag`": what "config only" required (the one-time changes), the emulator results from Block 4, the offline embedding test, why D2 waits, the runbook link.
- [ ] `CLAUDE.md`: Week 7 Wednesday entry (`Embedding:Dimensions`, `appsettings.Bedrock.json`, the collection rule, the shared Bedrock client helper).
- [ ] `README.md`: "Run the RAG app on Bedrock" (the one switch, the emulator variables).
- [ ] Commit, open a PR.

---

## End of day
- [ ] D1 checked; D2 marked "ready, runs on the real account"
- [ ] `docker/bedrock-kb/bedrock-kb-emulator.sh stop`
- [ ] Ask Claude for today's recap PDF (for OneDrive)

> **Short on time?** Do 2 → 3 → 4 → 7. Block 3 is the heart of "configuration only"; Block 5's SDK test is what proves the embedding half without an account.
