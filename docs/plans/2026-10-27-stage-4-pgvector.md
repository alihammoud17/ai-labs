# Stage 4 step 2: AiLab.Rag and pgvector (top-5 search)

- **Date:** 2026-10-27
- **Branch:** `stage-4-pgvector`
- **Goal:** A new host, `AiLab.Rag`, that stores embedded text chunks in a vector store (in memory or Postgres with pgvector, through `Microsoft.Extensions.VectorData`) and returns the top 5 chunks for a query, with their scores. `POST /ask` comes on Thursday in the same host.
- **Status:** in progress (2a done)

Tutor mode: the user writes the record, the store and the search service. Claude writes Docker, the project scaffolding, DI wiring and test scaffolding (2a).

## Steps

- [x] **2a** Scaffolding (Claude):
  - `docker-compose.yml`: `pgvector/pgvector:pg17` on host port 5433, `docker/init-vector.sql` creates the `vector` extension (0.8.7).
  - `AiLab.Rag`: `dotnet new web`, .NET 10, same `UserSecretsId` as the console and API, references Domain, Application and Infrastructure, in `AiLab.slnx`. http profile only, port 5074, Development.
  - Packages in Infrastructure: `CommunityToolkit.VectorData.InMemory` and `CommunityToolkit.VectorData.PgVector` 1.0.1.
  - `appsettings.json`: `Chat`, `Embedding` (as the console), `VectorStore` (`postgres`, `chunks`), Serilog levels. `ConnectionStrings:Vectors` in user-secrets only.
  - `Program.cs`: `AddInfrastructure()`, `AddFileLogging(..., "rag")`, `AddApplication()`, `AddChatClient(keyed).UseLogging()`, `AddEmbeddingGenerator(keyed).UseLogging()`, `GET /health`. TODO placeholders for `AddVectorStore` and the endpoints. `rag.http`.
- [ ] **2b** `TextChunkRecord` in `AiLab.Infrastructure/VectorStore/` (user): key, text, source, a 768-dimension vector with cosine distance.
- [ ] **2c** `TextVectorStore` behind `ITextVectorStore`, and `AddVectorStore(IConfiguration)` choosing in-memory or Postgres from `VectorStore:Provider` (user, with hints).
- [ ] **2d** `VectorSearchService` in Application: embed the query, ask the store for the top 5, return `SearchHit`s with scores (user).
- [ ] **2e** Endpoints: `POST /dev/seed` (Development only, a few hand-written chunks) and `GET /search?q=` (top 5 with scores). Requests in `rag.http`.
- [ ] **2f** Same seed and queries against `inmemory` and `postgres`: same order and scores? Results in `experiments.md` (user).

## Tests

- 2a: `dotnet build` with no warnings, 62 tests pass, `AiLab.Rag` starts in Development (`ValidateOnBuild`) and `GET /health` returns `{"status":"ok"}`, the API still starts in Development, `psql` shows the `vector` extension.
- Later: store and search tests over the in-memory connector and `FixedEmbeddingGenerator`, offline.

## Decisions & trade-offs

- **A separate host** (2a): RAG gets its own process and port, so the ticket API and its tests stay untouched.
- **Port 5433, not 5432** (2a): `docker-compose.yml` already published 5433, and the container was running on it; left as it was. The connection string uses 5433.
- **`AddChatClient` in the Rag host** (2a): `AddApplication()` registers the chat services (ticket analyzer, job extraction), so Development's `ValidateOnBuild` stopped the host without an `IChatClient`. `/ask` needs it anyway. The keyed provider is still read on first resolve, so nothing calls Ollama at startup.
- **No OpenAPI package** (2a): the template's `Microsoft.AspNetCore.OpenApi` 10.0.10 pulled in `Microsoft.OpenApi` 2.0.0, which has a known high-severity vulnerability (NU1903). Nothing needs it yet; add the API's version (10.0.13) if `/ask` gets a document.
- **No error middleware yet** (2a): `UseErrorResponses` lives in `AiLab.Api/Middleware`, and a host can't reference another host. Move it to a shared place before `/ask`.
- **CommunityToolkit connectors** (2a): `Microsoft.SemanticKernel.Connectors.PgVector` is deprecated. The toolkit packages need `Microsoft.Extensions.VectorData.Abstractions` ≥ 10.8.2 and `Microsoft.Extensions.AI.Abstractions` ≥ 10.8.3; our 10.11.0 satisfies both, so no conflict.

## Out of scope

- Chunking and ingesting `data/corpus` (Wed), `POST /ask` (Thu), the failure log (Fri).

## Changes

- **2026-10-27:** 2a done. 62 tests pass, no build warnings. `/health` answers on 5074.
