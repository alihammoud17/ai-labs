# Stage 4 step 2: AiLab.Rag and pgvector (top-5 search)

- **Date:** 2026-10-27
- **Branch:** `stage-4-pgvector`
- **Goal:** A new host, `AiLab.Rag`, that stores embedded text chunks in a vector store (in memory or Postgres with pgvector, through `Microsoft.Extensions.VectorData`) and returns the top 5 chunks for a query, with their scores. `POST /ask` comes on Thursday in the same host.
- **Status:** in progress (2a–2e done; 2f is the user's)

Tutor mode: the user writes the record, the store and the search service. Claude writes Docker, the project scaffolding, DI wiring and test scaffolding (2a).

## Steps

- [x] **2a** Scaffolding (Claude):
  - `docker-compose.yml`: `pgvector/pgvector:pg17` on host port 5433, `docker/init-vector.sql` creates the `vector` extension (0.8.7).
  - `AiLab.Rag`: `dotnet new web`, .NET 10, same `UserSecretsId` as the console and API, references Domain, Application and Infrastructure, in `AiLab.slnx`. http profile only, port 5074, Development.
  - Packages in Infrastructure: `CommunityToolkit.VectorData.InMemory` and `CommunityToolkit.VectorData.PgVector` 1.0.1.
  - `appsettings.json`: `Chat`, `Embedding` (as the console), `VectorStore` (`postgres`, `chunks`), Serilog levels. `ConnectionStrings:Vectors` in user-secrets only.
  - `Program.cs`: `AddInfrastructure()`, `AddFileLogging(..., "rag")`, `AddApplication()`, `AddChatClient(keyed).UseLogging()`, `AddEmbeddingGenerator(keyed).UseLogging()`, `GET /health`. TODO placeholders for `AddVectorStore` and the endpoints. `rag.http`.
- [x] **2b** `TextChunkRecord` in `AiLab.Infrastructure/VectorStore/` (user): string key, text, source (indexed), a 768-dimension vector with cosine distance and an HNSW index.
- [x] **2c** Store plumbing (Claude, handed over):
  - `ITextVectorStore` (Domain, no VectorData types): `EnsureCreatedAsync`, `UpsertAsync(chunks, embeddings)`, `SearchAsync(queryEmbedding, top)`.
  - `TextVectorStore` over `VectorStoreCollection<string, TextChunkRecord>`, in-memory or Postgres without knowing which. Mismatched counts → `ArgumentException`; one batched upsert; `top < 1` → `ArgumentOutOfRangeException`; a null score throws. Debug log with counts, never vectors.
  - `AddVectorStore(IConfiguration)` in `Infrastructure/VectorStore/VectorStoreServices.cs`, called only by `AiLab.Rag`.
  - Small fixes: `TextChunk` and `SearchHit` sealed, `Score` a `double`, unused `using System;` removed, template files `AiLab.Rag.http` and `appsettings.Development.json` deleted (by the user).
- [x] **2d** `VectorSearchService` in Application behind `IVectorSearchService` (user): `IndexAsync` embeds and stores chunks, `SearchAsync` embeds the query and returns the top 5 (1–20) `SearchHit`s. Review fixes applied by Claude at the user's request (see Changes).
- [x] **2e** Endpoints (Claude): `POST /dev/seed` (Development only) indexes `inputs/similarity-sentences.txt` (`Seed:Path`), one chunk per non-blank line, keys `similarity-sentences:<line>`, returns `{ indexed, source, provider, elapsedMs }`. `GET /search?q=&top=5` returns `{ query, top, provider, elapsedMs, hits: [{ rank, key, text, source, distance, similarity }] }`. Errors mapped in `Endpoints/RagErrors.cs`. `rag.http` with the four searches and three invalid requests. Tests in the new `AiLab.Rag.Tests`.
- [ ] **2f** Same seed and queries against `inmemory` and `postgres`: same order and scores? Results in `experiments.md` (user).

## Tests

- 2a: `dotnet build` with no warnings, 62 tests pass, `AiLab.Rag` starts in Development (`ValidateOnBuild`) and `GET /health` returns `{"status":"ok"}`, the API still starts in Development, `psql` shows the `vector` extension.
- 2c, `AiLab.Api.Tests/TextVectorStoreTests.cs`, over a real in-memory collection with 768-long vectors on two axes (e0, e1, halfway at 45°):
  - closest first, scores 0 / 0.2929 (1 − cos 45°) / 1; `top` limits the hits
  - re-upserting a key replaces the row
  - mismatched counts and `top` 0 or −1 throw
  - `AddVectorStore`: `inmemory` resolves under `ValidateOnBuild`; missing provider, unknown provider and `postgres` without a connection string name the key
  - Postgres, only with `AILAB_PG=1` (connection from `AILAB_PG_CONNECTION`, default port 5433): drops and recreates `chunks_test`, upserts 3, checks order and scores, and that `pg_indexes` has a `USING hnsw` index. Left in place for `\d chunks_test`.
- 2d, `AiLab.Api.Tests/VectorSearchServiceTests.cs` over `Fakes/MappedEmbeddingGenerator` (one vector per text, records batch sizes) and the in-memory store: closest first; 70 chunks → batches 32/32/6, all stored; empty input makes no call; empty text and duplicate keys refused before embedding; wrong embedding count; search before indexing names the collection; `top` 0 and 21; empty query. The Postgres test also checks the missing-collection message.

- 2e, `AiLab.Rag.Tests/SearchEndpointTests.cs` (17) over `WebApplicationFactory<Program>`, `VectorStore:Provider=inmemory` and `Fakes/HashedEmbeddingGenerator` (a fixed vector per text from a SHA-256 seed): order by distance, exact copy at rank 1 with distance 0 and similarity 1, one embedding call for 10 chunks, keys follow line numbers (blank lines counted), seeding twice leaves 10, the default path finds the repo file, 400s (empty, missing and blank `q`; `top` 0 and 50) with a plain message, 409 before seeding, 400 for a missing seed file, 502 with both hints, 503 for Postgres on a closed port (offline), 404 for `/dev/seed` outside Development, `IndexAsync([])` makes no call.
- 2e, real runs on Ollama, both stores: seed, the four `rag.http` searches, the three invalid ones; `SELECT count(*) FROM chunks` = 10 after seeding twice.

## Decisions & trade-offs

- **A separate host** (2a): RAG gets its own process and port, so the ticket API and its tests stay untouched.
- **Port 5433, not 5432** (2a): `docker-compose.yml` already published 5433, and the container was running on it; left as it was. The connection string uses 5433.
- **`AddChatClient` in the Rag host** (2a): `AddApplication()` registers the chat services (ticket analyzer, job extraction), so Development's `ValidateOnBuild` stopped the host without an `IChatClient`. `/ask` needs it anyway. The keyed provider is still read on first resolve, so nothing calls Ollama at startup.
- **No OpenAPI package** (2a): the template's `Microsoft.AspNetCore.OpenApi` 10.0.10 pulled in `Microsoft.OpenApi` 2.0.0, which has a known high-severity vulnerability (NU1903). Nothing needs it yet; add the API's version (10.0.13) if `/ask` gets a document.
- **No error middleware yet** (2a): `UseErrorResponses` lives in `AiLab.Api/Middleware`, and a host can't reference another host. Move it to a shared place before `/ask`.
- **CommunityToolkit connectors** (2a): `Microsoft.SemanticKernel.Connectors.PgVector` is deprecated. The toolkit packages need `Microsoft.Extensions.VectorData.Abstractions` ≥ 10.8.2 and `Microsoft.Extensions.AI.Abstractions` ≥ 10.8.3; our 10.11.0 satisfies both, so no conflict.

- **Provider read at registration, connection string on first resolve** (2c): the provider decides which connector to register, so `AddVectorStore` reads it from the `IConfiguration` it's given; a missing or unknown provider stops the host at startup. The connection string goes through `AddPostgresVectorStore`'s `Func<IServiceProvider, string>` overload, so registering needs no database and the missing-secret error appears when the store is first asked for.
- **Factory for the collection, type registration for the store** (2c): `GetCollection` is called on first resolve; `TextVectorStore` is `AddSingleton<ITextVectorStore, TextVectorStore>()`, which `ValidateOnBuild` can check because only `AiLab.Rag` registers it.
- **Score direction** (2c): with `DistanceFunction.CosineDistance` both connectors return a distance (0 = identical); the in-memory one converts its similarity, Postgres uses pgvector's `<=>`. The same test numbers pass on both.

- **Errors mapped in the endpoints** (2e): `UseErrorResponses` stays in `AiLab.Api` until `/ask`. Driver errors arrive wrapped (Postgres: `VectorStoreException` "Call to vector store failed."), so `RagErrors` looks down the inner-exception chain for `NpgsqlException`/`SocketException` (503) and `HttpRequestException` (502). Request cancellation is rethrown, not logged as a 500.
- **409 `collection_missing`** (2e, not in the brief): searching before seeding happens after every restart with the in-memory store. As an `InvalidOperationException` it would have been a 500 "Something went wrong", so `TextVectorStore` throws `VectorCollectionMissingException` (Domain) and the endpoint answers 409 with "POST /dev/seed".
- **Seed path from the repo root** (2e): the brief said "relative to the current directory, like /extract", but `dotnet run --project` starts a web project in its own folder, so the first real run looked in `AiLab.Rag/inputs/`. `RepoPaths.Resolve` (the lookup `FileEvalRunStore` already used, now shared) takes a relative path from the folder holding `AiLab.slnx`.

## Out of scope

- Chunking and ingesting `data/corpus` (Wed), `POST /ask` (Thu), the failure log (Fri).

## Changes

- **2026-10-27:** 2a done. 62 tests pass, no build warnings. `/health` answers on 5074.
- **2026-10-27:** 2b and 2c done. 73 tests pass offline (11 new) and 1 skipped; the Postgres test passes with `AILAB_PG=1`. No build warnings. `AiLab.Rag` (provider `postgres`) and `AiLab.Api` both start in Development.
  - The user had already made the records sealed, `Score` a `double` and the keys strings, and drafted the interface; Claude turned its comments into XML docs.
  - **Name clash:** the namespace `AiLab.Infrastructure.VectorStore` hides VectorData's `VectorStore` type inside it, so `VectorStoreServices.cs` uses an alias (`VectorStoreBase`).
  - **Test token:** `TestContext.Current.CancellationToken` must be read per test (a property), not cached in a static field.
  - **Review fixes:** probing both stores showed that a 384-dimension embedding was accepted in memory and then broke every later search in the collection ("Input span arguments must all have the same length"), while Postgres only said "Call to vector store failed." A zero query vector gave `Score = NaN`. `TextVectorStore` now checks every vector against `TextChunkRecord.Dimensions` (the one place 768 is written) with a message naming `Embedding:Model`, and refuses NaN scores. 3 new tests (76 pass), and the Postgres test checks the NaN case too. Also found: searching before `EnsureCreatedAsync` throws on both stores (2e must create the collection first), and duplicate keys in one batch are accepted silently (relevant for content-hash keys on Wednesday).
  - **Postgres schema:** the connector creates the table on `EnsureCollectionExistsAsync` with quoted PascalCase columns, a btree primary key on `"Key"`, a btree index on `"Source"` (from `IsIndexed`) and `hnsw ("Embedding" vector_cosine_ops)`.
- **2026-10-27:** 2d done. The user wrote `VectorSearchService`; Claude reviewed it and, asked to, applied the fixes. 88 tests pass offline (12 new), the Postgres test passes, no build warnings, both hosts start in Development.
  - **No `EnsureCreated` on search:** it cost a database call per search, and a wrong `VectorStore:Collection` quietly created an empty table and returned 0 hits, which looks like a retrieval miss. Indexing (a write) creates the collection; search doesn't. `TextVectorStore` turns the store's failure on a missing collection (Postgres: only "Call to vector store failed.") into a message naming the collection; the existence check runs only after a failure.
  - **Empty texts refused:** Ollama embeds `""` and `"   "` as a normal non-zero vector (2 tokens), so an empty chunk would come back as a contentless hit.
  - **Duplicate keys refused**, not deduped: the store let the last one replace the others silently, and the returned count was wrong. Wednesday's content-hash skip should drop duplicates before calling `IndexAsync`.
  - **Batches of 32:** one call for a whole corpus could outlast OllamaSharp's 100 s timeout. Tokens are logged per batch (Debug) and in total, to spot chunks near nomic-embed-text's context limit (Ollama truncates silently).
  - **DI comment fixed:** it said the console registers the store; only `AiLab.Rag` does.
- **2026-10-27:** 2e done. 105 tests pass (88 + 17 new in `AiLab.Rag.Tests`), no build warnings, both hosts start in Development.
  - **Seed path bug, found on the first real run:** `/dev/seed` answered `seed_file_not_found` for `.../AiLab.Rag/inputs/similarity-sentences.txt`. Every test had passed, because they all set an absolute `Seed:Path`; a test now covers the default path. See "Seed path from the repo root".
  - **400 messages:** .NET appends " (Parameter 'top')" and "Actual value was 0." to argument messages; `RagErrors` keeps only the sentence.
  - **Test isolation checked:** Rag's appsettings default is `postgres` and Docker was up, so a passing test run alone didn't prove the tests used the in-memory store. No `chunks` table existed in Postgres afterwards, so they did.
  - **Real runs:** both stores gave the same top 5 in the same order with the same distances (4 decimals) on all four queries. "Is caching turned off?" ranked "Caching is not enabled." first (0.1594), just ahead of "Caching is enabled." (0.1643). The exact copy scored 0.0000, its negation 0.0571 (similarity 0.9429, Monday's 0.94). Seeding: 10 chunks, 120 tokens (Monday's count). The observations are the user's to write in `experiments.md`.
- **2026-10-27:** Swagger for `AiLab.Rag`, as in the API: `Microsoft.AspNetCore.OpenApi` 10.0.13 (not the template's 10.0.10, whose `Microsoft.OpenApi` has a known vulnerability) and `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1. `/openapi/v1.json` and `/swagger` in Development only.
  - `OpenApi/RagApiDocument.cs`: title and description. `ErrorResponse` is shared with the ticket API, whose `[Description]` lists the ticket codes, so a schema transformer rewrites the `code` description with Rag's codes in this document only.
  - `OpenApi/RagExamples.cs`: the 200 examples are the real 2e replies (Postgres); every error status has its body as an example.
  - Endpoint metadata: names, tags, summaries, a description of distance vs similarity, `Produces` per status, `[Description]` on `q` and `top` and on the response records' fields.
  - Strict JSON number handling, as in the API, so numbers are plain `number` in the schema. `/health` returns a named `HealthResponse` instead of an anonymous type.
  - 5 new tests in `AiLab.Rag.Tests/OpenApiTests.cs` (110 pass). Checked for real: the document, `/swagger`, a seed and a search on Postgres, and the API's Swagger still loads.
