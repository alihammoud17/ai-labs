# Wednesday 4 Nov: measure retrieval on its own, then improve it with hybrid search

- **Date:** 2026-11-04
- **Branch:** `stage-5-hybrid-search` (from `main`)
- **Goal:** Compute hit rate@k for the 25 answerable questions of the golden set, from retrieval alone (no chat model, free). Add Postgres full-text search next to vector search and merge the two rankings. Record the numbers before and after.
- **Status:** done
- **Approval:** confirmed via /save-plan (the user's "Start with 4.0", 2026-11-04)

Tutor mode: **Me** steps are yours and are written out in detail below, so you can follow them. **CC** steps are Claude's: plumbing, DI, test scaffolding, docs.

## Deliverables and what "done" means

| Deliverable | Done when |
|---|---|
| Hit rate@k computed for the golden set | one free command prints hit@1, @3, @5, @10 and MRR for the 25 answerable rows of `ask-set-v1`, per search mode, and writes the table to a file |
| Hybrid search: Postgres full-text + vector, merged | `GET /search?mode=hybrid` returns one list built from a keyword ranking and a vector ranking merged with RRF; `mode=vector` (today's behaviour) and `mode=keyword` also work |
| Before/after numbers | `experiments.md` section 16 has vector vs keyword vs hybrid on the same 25 questions, with the questions that moved |

---

## Background: read this first (~20 minutes)

### Why measure retrieval on its own

`/ask` can fail in two places. Either retrieval doesn't send the right chunk, or the model does something wrong with the chunks it got. Yesterday's eval mixed both. Retrieval can be measured alone, for free: it needs only embeddings (Ollama) and Postgres, no chat model. A change to search can then be checked in seconds before paying for answers.

### Hit rate@k, recall@k, MRR

For each question you know the gold lines (from `ask-set-v1.json`). A retrieved chunk is a **hit** when it comes from the gold file and its line range contains a gold line. That's the same rule as yesterday's `RecallAtKEvaluator`.

- **First hit rank:** the rank of the first hit in the list (1 = top). None if no hit in the list.
- **Hit rate@k:** the share of questions whose first hit is at rank ≤ k. Example: 15 of 24 questions with a hit at rank 1 → hit@1 = 0.63.
- **Recall@k** (yesterday) is the same thing for a question with one gold source. They differ only for B6 and G1, where both sources are needed. For those, a "hit" means *every* needed source is in the top k.
- **MRR (mean reciprocal rank):** the average of 1 / first hit rank; a miss counts 0. Rank 1 → 1, rank 2 → 0.5, rank 4 → 0.25. It rewards putting the answer *higher*, not just somewhere in the list.

**Where we start (yesterday, vector search, heading chunker, the 24 English answerable rows):** hit@5 = 24/24 and MRR = 0.77. First hits: 15 at rank 1, 4 at rank 2, 3 at rank 3, 2 at rank 4. **Hit@5 is already full**, so hybrid search can't raise it. Any gain shows up at **hit@1, hit@3 and MRR**: the answer moves up. That matters, because the answer at rank 1 is what the model reads first, and it allows a smaller top-k (fewer tokens).

### Postgres full-text search in 5 ideas

1. **`to_tsvector('english', text)`** turns text into **lexemes**: lower-cased, stemmed words without stop words. Checked on our setup: `variable` → `variabl`, `minute` → `minut`, `Jwt__Secret` → `jwt` + `secret`, `X-Tenant-Webhook-Signature` → the whole thing plus `tenant`, `webhook`, `signatur`. `2026-03-18` → `2026`, `-03`, `-18`, so dates are split.
2. **`to_tsquery` / `plainto_tsquery`** turn the question into lexemes joined by operators. `plainto_tsquery('english', 'Which environment variable sets the JWT secret?')` → `'environ' & 'variabl' & 'set' & 'jwt' & 'secret'`. The `&` means **every** word must be in the chunk.
3. **`@@`** is "does this text match this query".
4. **`ts_rank_cd`** scores a match: more matching words, closer together, score higher. It is **not BM25**: Postgres has no word rarity (IDF) built in. BM25 is what the Contextual Retrieval article uses; extensions like ParadeDB add it. Good enough for 50 chunks.
5. **A GIN index** on `to_tsvector('english', "Text")` makes `@@` fast on big tables. At 50 chunks Postgres scans the table anyway; the index is for scale.

**The trap (checked on our table):** with `&` (AND), the JWT question matches **0 chunks**: no chunk has all five words. With `|` (OR), the same words find `README.md:65-76`, the answer, at **rank 1** (vector search has it at rank 2). So the keyword query must use OR and let the ranking sort it out. The connector's built-in `HybridSearchAsync` uses `plainto_tsquery` (AND). That's one reason this plan writes its own keyword query.

### Merging two rankings: Reciprocal Rank Fusion (RRF)

Vector search gives a cosine distance (0 to 2, lower is closer); keyword search gives a `ts_rank_cd` score (0 and up, higher is better). They aren't on the same scale, so you can't add them. RRF ignores the scores and uses only the **ranks**:

```
score(chunk) = 1 / (k + rank in vector list) + 1 / (k + rank in keyword list)
```

A chunk missing from one list gets 0 from that list. `k` (usually 60) flattens the curve, so that rank 1 vs 2 matters less than "in both lists" vs "in one". Worked example with k = 60:

| Chunk | Vector rank | Keyword rank | RRF score |
|---|---|---|---|
| `README.md:65-76` | 2 | 1 | 1/62 + 1/61 = 0.0325 |
| `docs/decisions.md:29-31` | 1 | 3 | 1/61 + 1/63 = 0.0323 |
| `docs/contracts.md:105-111` | 3 | - | 1/63 = 0.0159 |

The chunk that ranks well in **both** lists wins. Here that moves F5's answer from rank 2 to rank 1.

### What the Contextual Retrieval article adds

Anthropic's article (https://www.anthropic.com/news/contextual-retrieval):
- **The problem:** a chunk loses its context. Their example: "revenue grew by 3%" without saying which company or quarter.
- **The fix:** an LLM writes 50–100 tokens of context for each chunk ("This chunk is from ACME's Q2 2023 filing…"). That context is added to the chunk before both the embedding and the BM25 index.
- **Their numbers** (failure rate = 1 − recall@20, across their datasets):

  | Setup | Failure rate | Improvement |
  |---|---|---|
  | Baseline | 5.7% | |
  | Contextual embeddings | 3.7% | −35% |
  | Plus contextual BM25 (hybrid) | 2.9% | −49% |
  | Plus a reranker | 1.9% | −67% |

- They fuse BM25 and embeddings with rank fusion, and recommend passing the top 20 chunks.

How it maps here:
- **Today does the middle step without the "contextual" part:** keyword + vector, merged.
- **Our heading chunker already keeps some context:** each chunk has its file and heading path, as metadata.
- **The cheap version of contextual retrieval** is to put the file and heading into the indexed text. It needs no LLM. It's an optional variant in step 4.6.
- **The LLM-written context and the reranker** are out of scope today.

---

## Steps

- [x] **4.0** (CC) Branch `stage-5-hybrid-search`, this plan to in-progress, CLAUDE.md current step.

### Part A: measure the baseline (deliverable 1)

- [x] **4.1** (Me) **`RetrievalMetrics`: hit rate@k and MRR as plain functions.** File `AiLab.Evals/Retrieval/RetrievalMetrics.cs`. No model, no HTTP: lists in, numbers out.
  1. Write `int? FirstHitRank(AskSetRow row, IReadOnlyList<RetrievedChunk> ranked)`.
     - Walk `ranked` in order and return the 1-based rank of the first chunk that `RecallAtKEvaluator.IsHit(gold, chunk)` accepts for any of `row.Gold`.
     - For `goldMode == "all"`, the question only counts as found once every gold source is hit. So return the rank at which the **last** needed source first appears, or null if one never does.
     - Rows with no gold (abstain, gate) return null; the caller leaves them out.
  2. Write `double HitRate(IEnumerable<int?> firstHitRanks, int k)`: the share of ranks that are not null and ≤ k.
  3. Write `double Mrr(IEnumerable<int?> firstHitRanks)`: the average of `1.0 / rank`, with null counting as 0.
  4. Check by hand with yesterday's ranks: 15 × rank 1, 4 × rank 2, 3 × rank 3, 2 × rank 4 → hit@1 = 15/24 = 0.625, hit@3 = 22/24, hit@5 = 1, MRR = (15 + 2 + 1 + 0.5) / 24 = 0.771.

  *Hint:* `IsHit` is already `public static` on `RecallAtKEvaluator`; reuse it rather than copying the rule.
- [x] **4.2** (CC) **The retrieval runner:** `AiLab.Evals/RetrievalEvalTests.cs`.
  - It runs only with `AILAB_EVAL=1` and needs only Ollama and Postgres: no Anthropic key, $0.
  - For each answerable row it calls `GET /search?q=…&top=10&chunker=heading&mode=<mode>` and computes first hit ranks with 4.1.
  - It writes `eval-runs/retrieval/<date>-<mode>.md`: hit@1, @3, @5, @10 and MRR overall, per category, and per question (rank and the top 5 sources). H5 (French) is reported on its own line, as in the eval plan.
  - Until 4.4 exists the only mode is `vector`. Run it and check it reproduces yesterday's ranks (A15 at 4, H6 at 4, G1 at 3…). That run is the **"before"**.

### Part B: keyword search in Postgres (understand it in psql first)

- [x] **4.3** (Me) **Write the keyword query by hand in psql**, so you see each piece work before it becomes C#. Open psql with `docker exec -it ai-labs-vectors-1 psql -U ailab -d ailab`. Columns are quoted PascalCase.
  1. **See the lexemes:** `SELECT to_tsvector('english', 'Set Jwt__Secret for X-Tenant-Webhook-Signature');` Note how identifiers are split.
  2. **See why AND fails:** `SELECT count(*) FROM corpus WHERE "Chunker" = 'heading' AND to_tsvector('english', "Text") @@ plainto_tsquery('english', 'Which environment variable sets the JWT secret?');` gives 0.
  3. **Build an OR query from the same lexemes:** `to_tsquery('english', replace(plainto_tsquery('english', $q)::text, '&', '|'))`. Run the count again.
  4. **Rank and limit:** select `"Key", "Source", "StartLine", "EndLine", ts_rank_cd(to_tsvector('english', "Text"), q) AS rank`, filter on `"Chunker" = 'heading'` and `@@ q`, then `ORDER BY rank DESC LIMIT 10`. Check that `README.md:65-76` comes first.
  5. **Add the cosine distance** so every keyword hit also carries the number the `/ask` gate needs: `"Embedding" <=> $vector AS distance`. In psql, take the vector from an existing row to try it: `(SELECT "Embedding" FROM corpus WHERE "Key" = 'heading:README.md:…')`.
  6. **Try 3 more questions from the set:** an identifier (F4, the billing endpoint), a typo question (H6), and the French one (H5). Write down what each returns. The French one should find nothing: the 'english' stemmer doesn't know French.
  7. Paste the final query into the PR description or your notes. 4.4 turns exactly this query into C#.
- [x] **4.4** (CC) **The keyword search in code**, from your 4.3 query:
  - `IKeywordSearch` (Domain) with `SearchAsync(string query, ReadOnlyMemory<float> queryVector, int top, string? chunker)`. It returns `SearchHit`s whose `Score` is the cosine distance, like vector search.
  - `PostgresKeywordSearch` (Infrastructure) on its own `NpgsqlDataSource` built from `ConnectionStrings:Vectors`, with the vector type mapped.
  - A GIN index created on ingest: `CREATE INDEX IF NOT EXISTS … USING GIN (to_tsvector('english', "Text"))`, on the collection's table.
  - `InMemoryKeywordSearch` for the offline tests: counts query words in the chunk text. It's simple and only there so the endpoint works without Postgres.
  - Registered by `AddVectorStore` next to the store, so `postgres` / `inmemory` choose both together.

### Part C: merge the two lists (deliverable 2)

- [x] **4.5** (Me) **`RankFusion.Merge`, reciprocal rank fusion.** File `AiLab.Application/Search/RankFusion.cs`, a pure function. Signature: `IReadOnlyList<SearchHit> Merge(IReadOnlyList<SearchHit> vector, IReadOnlyList<SearchHit> keyword, int top, int k = 60)`.
  1. Make a dictionary keyed by `hit.Chunk.Key` that holds the score so far and the `SearchHit`.
  2. For each list, for `i` from 0: `score[key] += 1.0 / (k + i + 1)`. Ranks are 1-based, so the first item is `1 / (k + 1)`.
  3. If a chunk is in both lists, keep one `SearchHit`. The distance is the same in both, because 4.4 makes keyword hits carry the cosine distance too.
  4. Sort by RRF score, highest first. Break ties by **smaller distance**, then by key, so the order is the same on every run.
  5. Take `top`.
  6. Check by hand with the worked example above: README first, decisions.md second.

  *Why `k` = 60:* it's the value from the original RRF paper (Cormack et al., 2009) and the usual default. You can try 10 and 100 in 4.7 to see how much it matters on our set.
- [x] **4.6** (CC, Me reviews) **Wire the modes:**
  - **`VectorSearchService.SearchAsync`** gets a `mode` (`vector`, `keyword`, `hybrid`; the names live in Domain `SearchModes`):
    - vector: as today
    - keyword: embed the question (for the distances), then keyword search
    - hybrid: both searches with a larger window (top 20 each, as the article recommends), then `RankFusion.Merge` down to `top`
  - **`GET /search`** gets `mode=` (default `vector`); an unknown mode is a 400.
  - **`/ask`** gets `Ask:SearchMode`, **default `vector` until the numbers are in**.
  - **AskService's gate:** it reads `hits[0].Score` as the best distance, which is only true when the list is sorted by distance. With hybrid it must take the **smallest** distance in the list. That's a one-line change in your `AskService`; review it.
  - **Optional variant** ("contextual" keyword text, no LLM): index and search `coalesce("Heading", '') || ' ' || "Text"`, so a question can match a section's name. It's one more mode, `hybrid-heading`, measured in 4.7 and kept only if it helps.

### Part D: before / after (deliverable 3)

- [x] **4.7** (Me runs, CC drafts the tables) **Measure every mode on the same 25 questions:**

  ```bash
  AILAB_EVAL=1 dotnet test AiLab.Evals --filter RetrievalEvalTests
  ```

  - Fill one table: mode × hit@1, @3, @5, @10, MRR, plus the questions whose first hit **moved** (rank before → after).
  - Expect hit@5 to stay at 24/24; look at hit@1 and MRR.
  - Look at the moved questions one by one: is a move up a real improvement (the answering chunk), or a lucky keyword match?
  - Optional: try `k` = 10 and 100.
- [x] **4.8** (Me decides) **Keep or not:** switch `Ask:SearchMode` to `hybrid` only if MRR goes up and no question loses its hit@5. Optionally, run the Haiku answer eval once with hybrid (~$0.19: new chunks mean new prompts, so no cache hits) to see whether better ranks change the answers. Without that run, the decision rests on retrieval numbers only, and that's fine.
- [x] **4.9** (CC drafts, Me concludes) `experiments.md` section 16 (expected vs actual, the table, the moved questions, time). Update the eval plan's layer 1 with hit@1/@3 and MRR now that they're measured. Week 5 review notes. The conclusion in your words.
- [x] **4.10** (CC) Docs and PR: CLAUDE.md (search modes, `Ask:SearchMode`, the keyword index), README (`/search?mode=`), `docs/evals/` (how to run the retrieval eval), PR.

Commit after each step.

## Tests

Plain `dotnet test` stays offline and green (today: 306 tests, 270 pass, 36 skipped).

- **4.1, unit (`AiLab.Evals/RetrievalMetricsTests.cs`, offline; Me writes them with the code):**
  - first hit at rank 1 / 3 / none
  - `all` mode returns the rank where the last needed source appears, and null if one never does
  - hit@k over a mix of ranks and nulls
  - MRR with a miss counting 0
  - yesterday's ranks give hit@1 0.625 and MRR 0.771

  Run: `dotnet test --filter RetrievalMetricsTests`.
- **4.2, eval (`AiLab.Evals/RetrievalEvalTests.cs`, `AILAB_EVAL=1`, Ollama + Postgres, free):** one run per mode. It fails only on setup errors (HTTP error, an empty result for a mode that should return hits). The `vector` run must reproduce yesterday's first-hit ranks.
- **4.2, unit (`AiLab.Evals/RetrievalReportTests.cs`, offline; added during 4.2):** an `all` row is compared with Tuesday's any-source rank, a rank beyond 5 matches Tuesday's "not in the top 5", the non-English row is kept out of the totals.
- **4.4, integration (`AiLab.Api.Tests`, Postgres only with `AILAB_PG=1`, like the existing Postgres test):**
  - the GIN index exists after ingest
  - the OR query finds the JWT chunk where AND finds none
  - keyword hits carry a cosine distance
  - the chunker filter applies
- **4.4, unit (offline):** `InMemoryKeywordSearch` ranks a chunk with more query words higher and respects the chunker filter. Added during 4.4 (`AiLab.Api.Tests/KeywordSearchTests.cs`): hits carry the cosine distance; stopwords-only or unknown words give no hits; both providers register `IKeywordSearch`; a collection name that isn't a plain identifier is refused (it goes into the SQL text). The Postgres test also checks the missing-table error (409's `VectorCollectionMissingException`) and a stopwords-only query (empty tsquery, no error).
- **4.5, unit (`AiLab.Api.Tests/RankFusionTests.cs`, offline; Me writes them with the code):**
  - the worked example (README before decisions.md)
  - a chunk in both lists beats one in a single list
  - a chunk in only one list keeps its 1/(k+rank)
  - ties broken by distance, then by key
  - `top` respected
  - empty lists
  - Added during 4.5: hits keep their cosine distance (not the RRF score); `top` < 1 and `k` < 0 are refused.
- **4.6, integration (`AiLab.Rag.Tests`, offline, in-memory store and fake keyword search):**
  - `GET /search?mode=vector|keyword|hybrid` returns hits, ordered as the mode says
  - an unknown mode → 400 `invalid_request`
  - `mode` defaults to `vector` (the existing tests still pass unchanged)
  - `/ask` with `Ask:SearchMode=hybrid` gates on the **smallest** distance, not on `hits[0]`
  - Done in `AiLab.Rag.Tests/SearchModeTests.cs` (7 tests). The gate case runs with `keyword`, where the first hit is far and the second an exact copy (it fails on the old gate); with `hybrid` the tie-break already puts the exact copy first, so that case is only end to end. Added: hybrid equals `RankFusion.Merge` of the vector and keyword lists; keyword hits carry the same distance as vector hits; an unknown `Ask:SearchMode` stops the host at startup.

## Decisions & trade-offs

- **Retrieval is measured with no chat model,** so every search change is checked for free in seconds before any paid answer run.
- **Same golden set, same hit rule as yesterday** (gold file + gold line inside the chunk's range), so the numbers compare with section 15.
- **The keyword query is written by hand, not taken from the connector's `HybridSearchAsync`:**
  - the connector uses `plainto_tsquery` (AND), which matched 0 chunks for a normal question on our table
  - it returns only the fused list, so keyword-only can't be measured
  - its score is the RRF score, not a distance, which would break `/ask`'s gate
- **Keyword hits carry the cosine distance** (computed in the same SQL), so `SearchHit.Score` keeps one meaning everywhere and the gate still works.
- **RRF over weighted scores:** ranks need no score normalisation, and there's one setting (`k`) with a standard default.
- **`/ask` stays on vector until the numbers say otherwise.** Search mode is a setting, so the switch is one line and easy to undo.
- **The 'english' text search configuration:** the corpus is English. It stems (`minutes` matches `minute`) and splits identifiers into words that still match.

## Alternatives considered

- **The connector's built-in hybrid search:** rejected for the three reasons above. Still useful as a cross-check if needed.
- **BM25 through an extension (ParadeDB `pg_search`):** closer to the article, but a new Postgres image and extension for 50 chunks. Kept for later.
- **Weighted sum of normalised scores** (e.g. 0.7 × vector + 0.3 × keyword): needs both scores scaled to the same range and a weight to tune on 25 questions. Too easy to overfit.
- **A reranker model:** the article's biggest gain, but a new model and dependency. Next step, not today.

## Risks

- **No room to improve hit@5** (already 24/24). The gain, if any, shows in hit@1 and MRR. Say so in the conclusion rather than overselling.
- **Keyword search can promote a wrong chunk** that shares common words. Look at every moved question, not only the totals.
- **French (H5) won't improve:** keyword search with an English stemmer doesn't help a French question. Expected; reported apart.
- **Recreating the table:** none needed. The GIN index is added to the existing table, and `IF NOT EXISTS` makes it safe to repeat.
- **The gate:** with hybrid, `hits[0]` isn't the closest. Handled in 4.6 and tested.

## Out of scope

- LLM-written chunk context (true contextual retrieval) and re-embedding with it.
- A reranker.
- BM25 extensions.
- New golden questions. A few identifier-only queries (e.g. "TENANT_REACTIVATED", "AllowedClockSkewMinutes") would show where keyword search shines. Noted for later; they need gold lines labelled first.
- Changing the default chunker or top-k for `/ask`.

## Changes

- **2026-11-04, 4.1:** `RetrievalMetrics` written by the user. Review fixes applied by Claude on request: a null rank now counts as a miss in `HitRate` and `Mrr` (it was left out of the total, which would have reported the French H5 miss as a perfect 1.0), the missing usings, `ranked[i]` instead of `ranked[i].Chunk`, `public static`, and `FirstHitRank` reads `row.GoldMode` instead of taking a parameter. The `RetrievalMetricsTests` (marked Me) were written by Claude on request: 15 tests, the plan's five cases plus a row without gold, `any` mode over two sources, one chunk covering both `all` sources, empty input, and H5 as a 25th row (hit@5 0.96, matching Tuesday's Recall@5). Offline suite: 321 tests, 285 pass, 36 skipped.
- **2026-11-04, 4.2:** `RetrievalEvalTests` and `Retrieval/RetrievalReport.cs` (the markdown, a pure function). `EvalFixture` now creates the judge on first use, so this runner needs no Anthropic key, and `SearchAsync` takes an optional `mode` (sent as `&mode=`, ignored by `/search` until 4.6). Added `RetrievalReportTests` (3 offline tests, not in the plan).
  - **Found:** Tuesday's "First hit rank" (`RecallAtKEvaluator`) is the first chunk of *any* gold source, even on `all` rows. 4.1's `FirstHitRank` waits for the *last* needed source, so B6 is 4 (Tuesday 1) and G1 is 5 (Tuesday 3). The retrieval itself is unchanged: compared under Tuesday's rule, 25 of 25 rows match, and the runner checks exactly that.
  - **So the "before" numbers differ from the plan's expectation above** (15/4/3/2 → hit@1 0.625, MRR 0.771, which used Tuesday's rule). Under 4.1's rule, vector, top 10, heading, 24 English rows: **hit@1 0.583, hit@3 0.833, hit@5 1.000, hit@10 1.000, MRR 0.734.** H5 (French) is found at rank 9: a miss at 5, a hit at 10. Report: `eval-runs/retrieval/2026-11-04-vector.md` (git-ignored). Offline suite: 325 tests, 288 pass, 37 skipped.
- **2026-11-04, 4.3:** done in psql by the user (sub-steps 1–5; queries in `eval/queries/`). Sub-step 6 run by Claude on request: `cosine_distance.sh` now takes the question as an argument (quotes and accents escaped). Results in `experiments.md` section 16, started now (with 4.2's before numbers). **H5's expectation was wrong:** the French question isn't "nothing found" but keyword rank 2, because `tenant` is the same word in both languages. F4 is worse with keywords (rank 4 vs vector 1: its words are common across the corpus).
- **2026-11-04, 4.4:** `IKeywordSearch` (Domain), `PostgresKeywordSearch` and `InMemoryKeywordSearch` (Infrastructure), registered by `AddVectorStore`. The SQL is the 4.3 query (OR of the stems, `ts_rank_cd`, `"Embedding" <=> @vector`), plus `"Key"` as a tie-break so the order is stable (F4 had several 0.4 ties). The GIN index `<table>_text_fts` is created at the end of `/dev/ingest` and, for a table ingested earlier, on the first keyword search (`IF NOT EXISTS`). A missing table is a `VectorCollectionMissingException`, like vector search. Checked on the real corpus: ingest (0 embedded) created `corpus_text_fts`. With 50 heading chunks Postgres may still prefer a sequential scan; the index matters for a bigger corpus. Offline suite: 333 tests, 295 pass, 38 skipped (`AILAB_PG=1`: both Postgres tests pass).
- **2026-11-04, 4.5:** `RankFusion.Merge` written by the user; the RRF part (1-based `1/(k+rank)`, one entry per key, scores added) was right as written. Review fixes applied by Claude on request:
  - **Tie-break:** ties were broken by key twice (`Hit.Chunk.Key` and the dictionary key are the same value), never by distance. Now: RRF score, then the smaller cosine distance, then the key. Ties are real, not rounding: 3rd in vector only and 3rd in keyword only both score exactly 1/63, and the closer chunk is the better guess (and the one `/ask`'s gate looks at).
  - `public static`, so 4.6 and the tests can call it; `top` ≥ 1 and `k` ≥ 0 checked; the implicit usings removed; a header comment saying the result keeps the cosine distance as `Score`.
  - `RankFusionTests` (marked Me) written by Claude on request: 8 tests, the plan's six plus the distance check and bad input. The tie test fails on the original code. The "keeps 1/(k+rank)" case uses `k` = 0, where 1/1 and 1/2 + 1/2 tie exactly, so the distance has to decide. Offline suite: 341 tests, 303 pass, 38 skipped.
- **2026-11-04, 4.6:** `SearchModes` (Domain); `IVectorSearchService.SearchAsync(..., mode, ct)`: `vector` as before, `keyword` embeds the question then calls `IKeywordSearch`, `hybrid` takes the top 20 of each and merges with `RankFusion.Merge`. `GET /search?mode=` (empty = vector, unknown = 400). `Ask:SearchMode` (default `vector`, in Rag's appsettings; checked at startup). **AskService's gate** now takes the smallest distance in the list (and the second smallest for the log), not `hits[0]`: the one-line change for you to review in `AskService.cs`. `EvalFixture.SearchAsync` passes `Ask:SearchMode` by default, so the judges see the chunks `/ask` sent. **The optional `hybrid-heading` variant was not built** (time); noted for later. Offline suite: 348 tests, 310 pass, 38 skipped.
- **2026-11-04, 4.7–4.10** (the user asked Claude to finish the day; Haiku allowed up to $0.50):
  - **4.7:** `RetrievalEvalTests` runs every mode and a `Compare_modes` report (`eval-runs/retrieval/2026-11-04-comparison.md`), with hybrid at k = 10 and 100 merged in the test from the same two lists (no endpoint change). Hybrid: hit@1 0.667, hit@3 0.917, hit@5 0.958, MRR 0.787 (vector 0.583 / 0.833 / 1.000 / 0.734). G1 drops from 5 to 7.
  - **4.8:** the optional Haiku run with hybrid was made (`hybrid-2026-11-04-haiku`, $0.188, 13 cache hits only). H5 answered correctly 3/3 (was abstained); G1 lost the webhook version 3/3 and said it isn't documented (Groundedness still 5). By the plan's rule `Ask:SearchMode` stays `vector`. **The decision is the user's to confirm.**
  - **4.9:** `experiments.md` section 16 (tables, moved questions, the Haiku comparison, a conclusion marked as a draft for the user's own words), the eval plan's layer 1 (hit@1/@3, MRR measured), the week 5 review.
  - **4.10:** CLAUDE.md, README (`/search?mode=`, test counts), `docs/evals/hybrid-search-2026-11-04.md` for newcomers (linked from `docs/evals/README.md`), PR.
