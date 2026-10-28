# Stage 4 step 3: ingestion and chunking (Wednesday)

- **Date:** 2026-10-28
- **Branch:** `stage-4-ingestion` (from `main`, after `stage-4-pgvector` was merged)
- **Goal:** Turn real documents into searchable chunks: ingest MultiTenantSaaSApi's README, ADRs and API docs with two chunkers (fixed-size with overlap, and by heading), stored with metadata (source, heading, position).
- **Status:** done, merged into `main` (PR #5); the optional `HeaderChunker` comparison not done
- **Approval:** confirmed via /save-plan

Tutor mode: chunking, retrieval and prompt assembly are the user's to write. Claude does plumbing, DI, endpoints, tests and docs unless the user hands a part over. **3.3 is the user's.**

Deliverables:

1. Ingest MultiTenantSaaSApi's README, ADRs and API docs
2. Two chunkers: fixed-size with overlap, and by heading
3. Chunks stored with metadata (source, heading, position)

Corpus (from `~/RiderProjects/MultiTenantSaaSApi`, about 35 KB, roughly 9,000 tokens):

- `README.md` (7.8 KB, 11 headings)
- `docs/decisions.md`: the ADRs, one `###` per accepted decision plus open review items (5.4 KB)
- `docs/contracts.md`: the API docs (billing callbacks, tenant ops, webhooks), `##` / `###` with code blocks (8.1 KB)
- `docs/architecture.md` (13.6 KB, 10 `##` sections)

## Steps

- [x] **3.1** Corpus snapshot (20 min)
  - Copy the four files into `data/corpus/` as a fixed snapshot (keep their relative paths: `README.md`, `docs/decisions.md`, `docs/contracts.md`, `docs/architecture.md`)
  - `data/README.md` (moved out of `corpus/`, see Changes): the source repo, its commit hash, the date
- [x] **3.2** Data model gets metadata (30 min)
  - `TextChunk` and `TextChunkRecord` gain: `Heading` (section path joined with `" > "`), `Position` (order in the document), `StartLine`, `EndLine`, `Chunker` (`"fixed"` | `"heading"` | `"lines"`, `IsIndexed`), `ContentHash` (SHA-256 of the text)
  - One collection `"corpus"`, filtered by `Chunker` (`VectorSearchOptions` `Filter`), not a collection per strategy. Same mechanism as tenant filtering in Week 9
  - Key = `"{chunker}:{source}:{position}"`
  - The schema changes and the connector doesn't alter tables: drop the old `chunks` table and re-seed the 10 sentences (Chunker `"lines"`, Position = line number)
- [x] **3.3** The two chunkers (2 h, **MINE: the user writes them**; handed over to Claude after `IChunker` and `ChunkingOptions`, see Changes)
  - `IChunker { string Name; IReadOnlyList<TextChunk> Chunk(string source, string markdown); }` in Application
  - Tokens estimated as characters / 4; compare with the real counts `VectorSearchService` logs
  - [x] **3.3a** `FixedSizeChunker`: about 500 tokens, 50 overlap (about 2,000 / 200 characters); normalise `\r\n`; cuts moved back to whitespace; `StartLine` / `EndLine`; `Heading` = the nearest heading above the start; a last chunk under about 100 characters is merged into the previous one
  - [x] **3.3b** `HeadingChunker`: a heading stack gives the `Heading` path; `#` inside ```` ``` ```` code fences is NOT a heading; a body runs to the next heading at any level; sections over about 500 tokens are split with the fixed logic, keeping the `Heading`; headings with no body make no chunk; the heading path is prepended to the chunk text (free contextual retrieval); a file with no headings falls back to fixed
  - Offline unit tests (see Tests)
- [x] **3.4** Ingestion endpoint (45 min)
  - `POST /dev/ingest?chunker=fixed|heading|both` (Development only): reads every `.md` under `Corpus:Path` (default `data/corpus`, via `RepoPaths`), Source = the path relative to the corpus folder, `IndexAsync`; returns per-file and total counts, the min / avg / max estimated tokens, real tokens, elapsed ms
  - `GET /dev/chunks?chunker=&source=`: the stored chunks in order, for reading
  - `GET /search`: an optional `chunker` filter; hits add `heading`, `position`, `startLine`, `endLine`, `chunker`
  - `rag.http` updated
- [x] **3.5** Look and compare (45 min; handed over to Claude, see Changes)
  - Ingest both; SQL count, avg and max length by `Chunker`; read `/dev/chunks` for `contracts.md` under both chunkers (cut code blocks or tables? empty heading chunks?)
  - 5 questions × 2 chunkers, `top=3`, each marked rank 1 / top 3 / miss:
    - How are internal billing callbacks authenticated? → `contracts.md`, Internal billing subscription events > Authentication
    - Which origins does CORS allow outside Development? → `decisions.md`, CORS is a configured allowlist…
    - How is a tenant resolved from a request? → `architecture.md`, Tenant resolution and isolation
    - Is BillingService's queue durable? → `decisions.md`, Current billing durability is file-backed
    - What are the current limitations? → `README.md`, Current limitations
  - `experiments.md` section 12 (the user's to write; these 5 start Friday's 10-question failure log)
- [x] **3.6** Skip unchanged chunks (if time, 30 min)
  - On re-ingest, embed only chunks whose `ContentHash` changed; delete keys whose position no longer exists; a second run reports "0 embedded, N unchanged"
  - Optional stretch: MEDI's `HeaderChunker` on the same corpus, compared with mine on `contracts.md`

Done when:

- `data/corpus/` holds the four files, with the source commit noted
- both chunkers written by the user, unit tests passing
- `POST /dev/ingest?chunker=both` stores chunks with source, heading, position, line range, chunker, hash
- the SQL count and one `/dev/chunks` listing looked at
- `experiments.md` section 12: 5 questions × 2 chunkers

## Tests

- **3.3, unit** (offline, `dotnet test`): a code fence with `# not a heading`, a 3,000-character section, empty sections, a skipped heading level, a file with no headings, overlap correctness, sequential positions.
  - Added while implementing (see Changes), in `AiLab.Api.Tests`: `ChunkingOptionsTests` (zero/negative refused and named, overlap of half or more refused, 101/50 accepted, the default); `FixedSizeChunkerTests` (sizes and whole-word cuts, line numbers, `\r\n`, nearest heading, short tail merged, a word longer than a window still ends, blank file, default ≈ 450–500 tokens); `HeadingChunkerTests` (`~~~` fences too, body runs to the next heading at any level, text before the first heading, closing `#`s and indented headings, `#hashtag`); `CorpusChunkingTests` over `data/corpus` (both chunkers: sequential positions, unique keys, size limit; the heading chunker finds the 5 sections of the 3.5 questions).
- **3.5, manual checks against Ollama + Postgres:** SQL count, avg and max length by `Chunker`; `/dev/chunks` for `contracts.md` under both chunkers; the 5 questions × 2 chunkers at `top=3`, each marked rank 1 / top 3 / miss.
- **3.6, manual check:** a second ingest reports "0 embedded, N unchanged".
  - Added while implementing (see Changes). Unit, `VectorSearchServiceTests`: `SyncAsync` embeds everything once and nothing on a second run (no generator call, 0 tokens), only a changed text is embedded, a shorter source loses its old last positions, a source that left the corpus has its chunks deleted, `force` embeds everything, another chunker's rows are never touched, a chunk filed under the wrong source is refused before anything is written. `TextVectorStoreTests`: `DeleteAsync` removes keys and ignores unknown ones (also in the `AILAB_PG=1` test). Integration, `IngestEndpointTests`: second ingest 0 embedded with no generator call, an edited file embeds only its changed chunk, a shorter file loses positions, a removed file's chunks are deleted (row with `files` 0), `force=true`. Manual on Postgres: two runs, an edit to two corpus files, restore, `force=true`.
- **3.2** (added while implementing, see Changes): unit, offline. `TextChunkTests` (key shape, SHA-256 of the text, only the text changes the hash); `TextVectorStoreTests` (metadata round trip, chunker filter, same position under two chunkers is two rows; the `AILAB_PG=1` test also checks the metadata columns and the filter in Postgres); `VectorSearchServiceTests` (the service passes the filter on); the existing tests moved to the new key shape. Manual: `DROP TABLE chunks`, seed twice → `SELECT count(*) FROM corpus` = 10, `\d corpus` shows the new columns and a `Chunker` index.
- **3.4** (added while implementing, see Changes): integration, offline, `AiLab.Rag.Tests/IngestEndpointTests.cs` over a temporary corpus (a root file, a subfolder file, a `.txt` that is skipped): both chunkers' per-file rows and totals (sources relative with `/`, counts equal to the chunkers', totals = sums), one chunker only, ingesting twice replaces the rows, `/dev/chunks` order and fields, `/search?chunker=` returns only that chunker with the metadata, no or empty `chunker` searches all, unknown chunker 400 on ingest / search / chunks, missing corpus 400 `corpus_not_found`, `/dev/chunks` before anything is stored 409, the default path is the repo's `data/corpus`, both endpoints 404 outside Development. Unit: `VectorSearchServiceTests` (tokens summed over batches, null without usage, unknown chunker refused), `TextVectorStoreTests` (`GetChunksAsync` filters and order, missing collection; the `AILAB_PG=1` test translates every filter shape to SQL). `OpenApiTests` (the two new paths, ingest's responses, the `chunker` parameter, `corpus_not_found`). Manual, Ollama + Postgres: ingest both, `/dev/chunks` on `contracts.md`, a filtered search, a second ingest, the SQL count by chunker.
- **3.1:** none specified in the approved plan (beyond the "Done when" checks).

## Decisions & trade-offs

- **Own chunkers**, not `Microsoft.Extensions.DataIngestion`'s `HeaderChunker`: chunking is the user's to understand and explain. `HeaderChunker` is only an optional comparison at the end (3.6 stretch).
- **Whole document vs RAG:** the corpus (~9,000 tokens) is well under the ~200k-token size where the whole thing fits in a prompt. RAG is built on it to learn the mechanics, not because it's needed. This answers Friday's "whole document vs RAG" question.
- **One collection `"corpus"`, filtered by `Chunker`**, not a collection per strategy: same mechanism as tenant filtering in Week 9.
- **Key `"{chunker}:{source}:{position}"`**: stable per chunk position, so re-ingest (3.6) can compare hashes and delete positions that no longer exist.
- **Schema change by dropping the table**: the connector doesn't alter tables, so the old `chunks` table is dropped and the 10 sentences re-seeded (Chunker `"lines"`, Position = line number).
- **Tokens estimated as characters / 4**, checked against the real counts `VectorSearchService` logs.
- **Heading path prepended to the chunk text** in the heading chunker: free contextual retrieval.

## Alternatives considered

- MEDI's `HeaderChunker` as the main chunker: rejected (see Decisions); kept as an optional comparison.
- A collection per chunking strategy: rejected in favour of one collection with a `Chunker` filter.
- Putting the whole corpus in the prompt: possible at this size, but doesn't teach the RAG mechanics.

## Out of scope

- `POST /ask`, citations, rate limiter (Thursday).
- The 10-question failure log beyond these 5 questions (Friday).
- Other files in the source repo's `docs/` (`CHANGELOG.md`, `operations.md`, `deployment-roadmap.md`, `features.md`, `archive/`, `plans/`): not in the corpus.

## Changes

- **2026-10-28, 3.1:** the snapshot notes went to `data/README.md` instead of `data/corpus/README.md`. That path is taken by the source repo's own `README.md` (part of the corpus), and 3.4 ingests every `.md` under `data/corpus`, so notes inside it would become chunks. Snapshot: commit `c87cc20` (2026-10-24) on `master`, all four files byte-identical (`cmp`).
- **2026-10-28, 3.2:** decisions the plan left open. `Key` and `ContentHash` are computed properties of `TextChunk` (from `Chunker`/`Source`/`Position`, and from `Text`), so chunkers only pass `TextChunk(Chunker, Source, Position, Text, Heading, StartLine, EndLine)` and can't get the key format wrong. `Heading` is null when no heading is above the chunk; lines are 1-based and inclusive. `SearchHit` is now `(TextChunk Chunk, double Score)`. The filter is a `chunker` argument on `ITextVectorStore.SearchAsync` and `IVectorSearchService.SearchAsync` (null = all); the `/search` query parameter and the extra hit fields stay in 3.4. `VectorStore:Collection` and its default are now `corpus`. Seed keys changed from `similarity-sentences:5` to `lines:similarity-sentences.txt:5` (OpenAPI examples too). Tests added (see Tests): 121 total, 120 pass offline, 1 Postgres test skipped without `AILAB_PG=1` (it passes with it).
- **2026-10-28, 3.3:** handed over to Claude ("implement 3.3, continue where I left off") after the user wrote `IChunker` (with `EstimateTokens`) and `ChunkingOptions` (Claude added the constructor checks the user asked for: all positive, overlap under half of `MaxChars`; get-only properties so `with` can't skip them; `ChunkingOptions.Default` = 2000 / 200 / 100). Code in `AiLab.Application/Chunking/`: `MarkdownText` (`\r\n` normalised, line offsets, ATX headings outside ``` and ~~~ fences, heading paths from a stack), `FixedWindows` (the window both chunkers use), `FixedSizeChunker`, `HeadingChunker`. Choices the plan left open:
  - The half rule alone doesn't guarantee progress (moving a cut back to whitespace shortens the step), so the cut only moves back as far as `start + overlap + 1`; past that a long word is cut mid-word. The overlap then starts at the next word boundary.
  - "A last chunk under ~100 characters" is measured as the new characters the last window adds beyond the previous one (its length is mostly overlap). A merged chunk can be up to `MinTailChars` over `MaxChars`.
  - Positions start at 1 per file and chunker. The fixed chunker's `Heading` is the full path of the nearest heading above the window start.
  - Heading chunker: the heading line itself isn't repeated in the body (the path replaces it); text = `path + "\n\n" + body`. A section is split when its body is over `MaxChars`, so a chunk can be the path's length over it. The first piece's `StartLine` is the heading line. Text before the first heading is a chunk with a null `Heading`. No headings → fixed windows stored with chunker `heading`. Setext headings (`===` underlines) aren't recognised; the corpus has none.
  - On the corpus: fixed 21 chunks (≈ 454 tokens average), heading 50 (≈ 184, max 513). 154 tests (153 pass, 1 Postgres test skipped without `AILAB_PG=1`).
- **2026-10-28, 3.4:** done by Claude. Choices the plan left open:
  - Real tokens: `IVectorSearchService.IndexAsync` returns `IndexResult(Indexed, InputTokens)` (null when the provider reports no usage), and ingest calls it once per file and chunker, so each file has its own count. `/dev/seed` uses `.Indexed`.
  - `ChunkerNames` (Domain) holds `fixed`, `heading`, `lines`; the chunkers' `ChunkerName` points at it. `VectorSearchService.SearchAsync` refuses an unknown chunker (400 with the names); an empty `?chunker=` means no filter. `/dev/ingest` takes `fixed`, `heading` or `both` (default `both`); `lines` is refused there.
  - Both chunkers are registered in `AddApplication()` as `IChunker` (default options); ingest picks them by `Name`.
  - `/dev/chunks` reads the store directly (`ITextVectorStore.GetChunksAsync(chunker, source)`, up to 10,000 rows, sorted in code by source, chunker, position); a missing collection is the usual 409.
  - Response shapes: `IngestResponse { chunker, corpus, provider, elapsedMs, files[], totals[] }` with `IngestStats { chunker, source (null in a total), files, chunks, minTokens, avgTokens, maxTokens, embeddingTokens, elapsedMs }`; `ChunksResponse { chunker, source, count, chunks[] }` with `key, chunker, source, position, heading, startLine, endLine, estimatedTokens, contentHash, text`. `/search` adds `chunker` to the response and `chunker, heading, position, startLine, endLine` to each hit.
  - New error code `corpus_not_found` (400). Ingest doesn't delete positions a file no longer has; that's 3.6.
  - `rag.http`: ingest, chunks, the five 3.5 questions (with `chunker=heading`, to run again with `fixed`), the seed searches now `chunker=lines`, two invalid requests. OpenAPI examples from the real run.
  - Real run (Ollama, Postgres): fixed 21 chunks / 9,208 embedding tokens, heading 50 / 8,805; the characters / 4 estimate is about 4% high (9,534 and 9,200). Whole ingest 1.7 s, a second one 1.1 s. SQL: fixed 21 (avg 1,815 characters, max 2,000), heading 50 (733, 2,049), lines 10. 174 tests (173 pass, 1 Postgres test skipped without `AILAB_PG=1`; it passes with it).
- **2026-10-28, 3.5:** handed over to Claude ("do 3.5"); section 12 of `experiments.md` is a draft for the user to review, like section 11. Matching rule added, since the plan didn't say how to mark a fixed chunk: a hit counts if it comes from the target file and contains a phrase from the target section's answer (`X-Billing-Signature`, `allows only the origins in`, `TenantMiddleware`, `This is not a distributed queue`, `placeholder webhook adapter`); a second column counts any chunk in the top 3 that answers. Result: heading 4 rank 1 / 1 top 3 / 0 miss, fixed 3 / 1 / 1; both answerable 5 / 5 from the top 3. Recommendation for Thursday: `chunker=heading`, top 3 or more, cite source and line range.
- **2026-10-28, 3.6:** done by Claude. `IVectorSearchService.SyncAsync(chunker, chunksBySource, force)` (`SyncResult` per source: chunks, embedded, unchanged, deleted, tokens, ms) reads the chunker's stored rows once, embeds new keys and changed hashes through `IndexAsync`, deletes keys a source no longer has (`ITextVectorStore.DeleteAsync`), and deletes every chunk of a stored source missing from the corpus (the plan only said positions; a removed file had the same problem). Every file is validated before anything is written. Added beyond the plan: `force=true` on `/dev/ingest`, since the hash covers the text only and a new embedding model or task-prefix setting would otherwise keep the old vectors. `IngestStats` gains `embedded`, `unchanged`, `deleted`; `embeddingTokens` now counts only what was embedded.
  - Real run (Ollama, Postgres): a second ingest 0 embedded / 71 unchanged / 0 tokens in 7–100 ms (a forced full embed: 1.1 s). One sentence shortened in `decisions.md` and the last section of `contracts.md` removed: heading 1 embedded (68 tokens), 1 deleted; fixed 3 embedded (993 tokens), because a shorter sentence moves every later window boundary in that file (2 of `decisions.md`'s 3 windows) and the last window of `contracts.md` got shorter. Restored with `git checkout`: heading 2, fixed 3 embedded again. SQL back to 21 / 50 / 10.
  - Not done: the optional `HeaderChunker` (MEDI) comparison. 187 tests (186 pass, 1 Postgres test skipped without `AILAB_PG=1`; it passes with it).
