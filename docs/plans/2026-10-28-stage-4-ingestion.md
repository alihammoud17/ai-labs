# Stage 4 step 3: ingestion and chunking (Wednesday)

- **Date:** 2026-10-28
- **Branch:** `stage-4-ingestion` (from `main`, after `stage-4-pgvector` was merged)
- **Goal:** Turn real documents into searchable chunks: ingest MultiTenantSaaSApi's README, ADRs and API docs with two chunkers (fixed-size with overlap, and by heading), stored with metadata (source, heading, position).
- **Status:** in-progress
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
- [ ] **3.2** Data model gets metadata (30 min)
  - `TextChunk` and `TextChunkRecord` gain: `Heading` (section path joined with `" > "`), `Position` (order in the document), `StartLine`, `EndLine`, `Chunker` (`"fixed"` | `"heading"` | `"lines"`, `IsIndexed`), `ContentHash` (SHA-256 of the text)
  - One collection `"corpus"`, filtered by `Chunker` (`VectorSearchOptions` `Filter`), not a collection per strategy. Same mechanism as tenant filtering in Week 9
  - Key = `"{chunker}:{source}:{position}"`
  - The schema changes and the connector doesn't alter tables: drop the old `chunks` table and re-seed the 10 sentences (Chunker `"lines"`, Position = line number)
- [ ] **3.3** The two chunkers (2 h, **MINE: the user writes them**)
  - `IChunker { string Name; IReadOnlyList<TextChunk> Chunk(string source, string markdown); }` in Application
  - Tokens estimated as characters / 4; compare with the real counts `VectorSearchService` logs
  - [ ] **3.3a** `FixedSizeChunker`: about 500 tokens, 50 overlap (about 2,000 / 200 characters); normalise `\r\n`; cuts moved back to whitespace; `StartLine` / `EndLine`; `Heading` = the nearest heading above the start; a last chunk under about 100 characters is merged into the previous one
  - [ ] **3.3b** `HeadingChunker`: a heading stack gives the `Heading` path; `#` inside ```` ``` ```` code fences is NOT a heading; a body runs to the next heading at any level; sections over about 500 tokens are split with the fixed logic, keeping the `Heading`; headings with no body make no chunk; the heading path is prepended to the chunk text (free contextual retrieval); a file with no headings falls back to fixed
  - Offline unit tests (see Tests)
- [ ] **3.4** Ingestion endpoint (45 min)
  - `POST /dev/ingest?chunker=fixed|heading|both` (Development only): reads every `.md` under `Corpus:Path` (default `data/corpus`, via `RepoPaths`), Source = the path relative to the corpus folder, `IndexAsync`; returns per-file and total counts, the min / avg / max estimated tokens, real tokens, elapsed ms
  - `GET /dev/chunks?chunker=&source=`: the stored chunks in order, for reading
  - `GET /search`: an optional `chunker` filter; hits add `heading`, `position`, `startLine`, `endLine`, `chunker`
  - `rag.http` updated
- [ ] **3.5** Look and compare (45 min)
  - Ingest both; SQL count, avg and max length by `Chunker`; read `/dev/chunks` for `contracts.md` under both chunkers (cut code blocks or tables? empty heading chunks?)
  - 5 questions × 2 chunkers, `top=3`, each marked rank 1 / top 3 / miss:
    - How are internal billing callbacks authenticated? → `contracts.md`, Internal billing subscription events > Authentication
    - Which origins does CORS allow outside Development? → `decisions.md`, CORS is a configured allowlist…
    - How is a tenant resolved from a request? → `architecture.md`, Tenant resolution and isolation
    - Is BillingService's queue durable? → `decisions.md`, Current billing durability is file-backed
    - What are the current limitations? → `README.md`, Current limitations
  - `experiments.md` section 12 (the user's to write; these 5 start Friday's 10-question failure log)
- [ ] **3.6** Skip unchanged chunks (if time, 30 min)
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
- **3.5, manual checks against Ollama + Postgres:** SQL count, avg and max length by `Chunker`; `/dev/chunks` for `contracts.md` under both chunkers; the 5 questions × 2 chunkers at `top=3`, each marked rank 1 / top 3 / miss.
- **3.6, manual check:** a second ingest reports "0 embedded, N unchanged".
- **3.1, 3.2, 3.4:** none specified in the approved plan (beyond the "Done when" checks).

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
