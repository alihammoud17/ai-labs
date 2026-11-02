# Week 4 recap: embeddings, pgvector, chunking and RAG

- **Week:** 2026-10-26 to 2026-10-31 (roadmap week 4, Stage 4)
- **Written by:** Claude, at the user's request. My own notes for the week are in [week-4-review.md](week-4-review.md).

## The week in one paragraph

On Monday, `ai-labs` could chat with a model. By Saturday, it has a small but complete RAG system: `AiLab.Rag` embeds the MultiTenantSaaSApi docs with `nomic-embed-text`, stores them in pgvector, splits them by heading, retrieves the closest chunks for a question, answers with numbered citations the server checks, and abstains when the docs don't cover a question. `main` went from 53 to 229 tests (228 pass, 1 Postgres test skipped without `AILAB_PG=1`), through PRs #5 and #6 and the failure-log branch. The week ended with measurement instead of more code: 44 runs over 20 questions showed that **retrieval is not where this RAG fails. Generation is.** For every English question, the right chunk was sent to the model; llama3.2 then got 15 of 22 runs wrong in some way, and Haiku 5.5 got 4.

## Day by day

| Day | Built | Learned |
|---|---|---|
| **Mon** | Embedding infrastructure (keyed `"embedding-provider"`), `SimilarityService`, the `/similarity` matrix in the console | Embeddings rank well but don't threshold well: a negation pair scored 0.94 similar, a paraphrase with no shared words 0.49 |
| **Tue** | Postgres 17 + pgvector in Docker (port 5433), `TextVectorStore` (in-memory and Postgres), `VectorSearchService` (mine), `GET /search`, `/dev/seed`, Swagger | Cosine distance (lower = closer); HNSW; a search never creates the collection, so a missing table is a clear 409 |
| **Wed** | `FixedWindows` and `HeadingChunker`, `TextChunk` metadata (heading path, lines, `Key`, `ContentHash`), `SyncAsync` re-ingest, `/dev/ingest`, `/dev/chunks`, shared error handling in `AiLab.Web`. PR #5 | Heading chunks ranked better than fixed windows (section 12); `Key` says *where* a chunk is, `ContentHash` says *what* it is, which is how re-ingest skips unchanged text |
| **Thu** | The Review Lab: manual tests and reviews of everything from Mon to Wed | `ORDER BY round(distance)` silently skips the HNSW index; an Ollama outage was reported as a 503 (a bare `SocketException` check), fixed in `45642bd` |
| **Fri** | `POST /ask`: the distance gate, `AskPrompt.Build` (mine), `prompts/ask-v1.md`, typed `AskAnswer` with one repair call, `CitationCheck`, rate limiter, 38 new tests, the 10-question eval. PR #6 | Valid JSON is not a complete answer: Ollama's JSON mode cut answers at an unescaped quote; `grounded` only proves a cited number was sent |
| **Sat** | The failure log: 10 new questions, gold chunks labelled before any model call, every run coded as retrieval or generation. Merged into `main` | Retrieval is fine on this corpus in English; the model is the weak link |

## How AiLab.Rag answers a question

```text
POST /ask { question, top? }
  validate        1–500 chars, top 1–10                         → 400
  retrieve        embed question → pgvector HNSW, heading chunks → 409 / 502 / 503
  gate            best distance > 0.50 → abstained, no model call
  assemble        <source n file section lines> blocks + ask-v1.md
  generate        typed AskAnswer, one repair if unreadable      → 502
  check           cited numbers vs the sources sent
  respond         grounded | abstained | unverified, citations from the server's map
```

## Numbers worth remembering

| What | Value |
|---|---|
| Negation pair similarity (Mon) | 0.94: embeddings don't see "not" |
| Distances of answerable questions | 0.26 – 0.48 |
| Unrelated question ("center a div") | 0.59: the gate's only catch |
| Not-answerable questions that passed the gate | 0.36 – 0.45: inside the answerable range |
| Gold chunk in the top 4, English questions | 100% |
| Only retrieval miss in 44 runs | the French question (English-only embeddings) |
| llama3.2 / Haiku 5.5, OK runs of 22 | 7 / 18 |
| Generation failure codes, llama3.2 / Haiku | 21 / 3 |
| Cost of 11 `/ask` calls on Haiku | about half a cent |
| Tests on `main` | 229 |

## Where this RAG fails

From [eval/results/2026-10-31-failure-log.md](../../eval/results/2026-10-31-failure-log.md):

| | llama3.2 | Haiku 5.5 |
|---|---|---|
| OK | 7 | 18 |
| Retrieval: the right chunk wasn't in the top 10 (R1) | 1 | 1 |
| Generation: wrong citation (M2) | 10 | 0 |
| Generation: left out what was sent (M3) | 5 | 0 |
| Generation: invented (M1) | 4 | 1 |
| Generation: general knowledge instead of abstaining (M4) | 2 | 0 |
| Generation: format, cut off (M5) | 0 | 2 |
| Gate let a no-answer question through (G0) | 3 | 3 |

What that means:
- **Retrieval tuning would buy little here.** Hybrid search (BM25 + embeddings, from Eugene Yan's article) helps with exact identifiers, but `Jwt__Secret` and the callback URL were already found at ranks 1–2. It's worth revisiting on a bigger or more varied corpus.
- **The model decides the quality.** llama3.2's typical failure is a right fact with the wrong citation, which `CitationCheck` can't see. It's a development model here, not an answering model.
- **The gate is a cost filter, not a quality filter.** It stops clearly unrelated questions for free. Near-misses depend on the model saying "not in the docs": Haiku did 3 of 3 times, llama3.2 1 of 3.
- **Haiku's only real failure was budget:** "List every rate limit" was cut off twice at 800 output tokens, because its thinking counts toward `MaxOutputTokens`. Fixed: `Ask:MaxOutputTokens` is now 2000, and B7 is grounded at about 1,030 output tokens.

## Bugs and mistakes that taught the most

1. **Docker compose list-of-maps** for `environment` (Tue): YAML that looks right and isn't.
2. **`ORDER BY round(distance)`** (Thu): the query still works, but Postgres can't use the HNSW index. Only `ORDER BY embedding <=> query` uses it.
3. **A bare `SocketException` → 503** (Thu): `HttpClient` wraps one when Ollama is down, so an embedding outage looked like a database outage. Map by the outermost meaningful exception, not by what's inside.
4. **The quote that ended the answer** (Fri): grammar-constrained JSON plus an unescaped quote gave valid JSON with half an answer. Fixed with prompt rule 10, and by treating a missing `citedSources` as unreadable.
5. **A prompt example leaking into answers** (Fri): "How are invoices emailed?" got back the prompt's own example sentence word for word. Keep examples on topics the corpus doesn't have.

## Who wrote what

Tutor mode says I write chunking, retrieval and prompt assembly. This week:
- **Mine:** `VectorSearchService` (Tue), the `/ask` validation, retrieval and gate (4.2), `AskPrompt.Build` (4.3), the Review Lab work, both real bugs found in testing (the 503 mapping and the cut-off answer), and the weekly notes.
- **Handed to Claude Code:** the chunkers (Wed, 3.3), with the Review Lab meant to make them mine.
- **Written by Claude at my request:** `experiments.md` sections 12, 13 and 14, including the conclusions, and the full `ask-v1.md`.

## Done at the end of the week

- [x] Failure log: H5 recoded from R2 to R1 (its full answer isn't in the top 10)
- [x] `stage-4-failure-log` merged into `main`
- [x] Weekly notes: [week-4-review.md](week-4-review.md)
- [x] `Ask:MaxOutputTokens` 800 → 2000 for Haiku's list answers

## Open, carried into week 5

- [ ] Rewrite the conclusions of sections 12, 13 and 14 in my own words, or keep them and remove the "written by Claude" markers once I agree with every line.
- [ ] Decide the default chat provider for `/ask` (Haiku for answers, llama3.2 for development).
- [ ] Non-English questions miss at retrieval (English-only embeddings).
- [ ] Hybrid search (keywords + vectors) for exact identifiers and numbers, if a bigger corpus needs it.

## Into week 5 (evals)

The failure log is a hand-made eval harness. Week 5 automates it:
- **Faithfulness:** an LLM judge that checks whether each cited chunk supports its claim. This is the check `CitationCheck` can't do, and it would catch llama3.2's M1, M2 and M4 automatically.
- **Repeated runs:** one run per question gives anecdotes. Several runs give rates.
- **The 30-question set** in [docs/evals/ask-set-v1.md](../evals/ask-set-v1.md), with gold answers, becomes the regression suite: retrieval recall@k without a model, answer quality with one.

## Sources used this week

- [Microsoft Learn: Embeddings in .NET](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/embeddings)
- [pgvector README](https://github.com/pgvector/pgvector)
- [Pinecone: Chunking Strategies for LLM Applications](https://www.pinecone.io/learn/chunking-strategies/)
- [Generative AI for Beginners .NET](https://github.com/microsoft/Generative-AI-for-beginners-dotnet)
- [Microsoft Learn: Retrieval-augmented generation in .NET](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/rag)
- [Chip Huyen: Building A Generative AI Platform](https://huyenchip.com/2024/07/25/genai-platform.html)
- [Eugene Yan: Patterns for Building LLM-based Systems & Products](https://eugeneyan.com/writing/llm-patterns/)
