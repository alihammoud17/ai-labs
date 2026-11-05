# Thursday 5 Nov: the experiment matrix (2 chunkers × 2 models)

- **Date:** 2026-11-05
- **Branch:** `stage-5-experiment-matrix` (from `main`)
- **Status:** in-progress
- **Approval:** approved in plan mode (2026-11-05)
- **Goal:** Run `/ask` on the 30-question set in 4 configurations (heading vs fixed-2000 chunks × Haiku 5.5 vs llama3.2), put quality, cost per answer and latency in one table, and choose the configuration with a one-line justification.

Tutor mode: **Me** steps are yours (the decisions and the judgement calls). **CC** steps are Claude's (env plumbing, the report code, tests, docs).

## Context

Week 4 and this week left two settings chosen by habit, not by data: `Ask:Chunker = heading` (section 12 compared chunkers on 5 questions, retrieval only) and the chat provider for `/ask` (Haiku for answers, llama3.2 for development, still listed as open). Tuesday's baseline and Wednesday's retrieval runner already measure most of what's needed, but only for one configuration at a time. Today runs all four through the same harness and decides.

## Deliverables and what "done" means

| Deliverable | Done when |
|---|---|
| 2 chunk sizes × 2 models in one table | `experiments.md` section 17 has one row per cell with: quality (hand pass rate, Groundedness, Recall@5 / hit@1 / MRR, abstain correct), cost per answer, latency (median, p95), avg input tokens |
| Chosen configuration + one-line justification | written in section 17 and in `week-5-review.md`; `Rag/appsettings.json` changed if the choice differs from today's |

## The matrix

| | Haiku 5.5 | llama3.2 |
|---|---|---|
| **heading** (50 chunks, ~176 tokens avg) | cell HH | cell HL |
| **fixed-2000** (21 chunks, ~440 tokens avg) | cell FH | cell FL |

Held constant: the 30 rows of `ask-set-v1.json`, 3 runs each, top 5, `vector` search, `MaxDistance` 0.50, `prompts/ask-v1.md`, `MaxOutputTokens` 2000, Haiku as judge.

**Known confound (say it in the write-up):** heading vs fixed changes the boundary rule as well as the size. And top 5 means fixed sends ~2.5× more context than heading, so cost and quality differences are partly "more context", which is exactly the trade-off being decided.

---

## Background (~30 min)

### Readings

- **Artificial Analysis** (https://artificialanalysis.ai/): look up Claude Haiku 5.5 and Llama 3.2 3B. Note their quality index, price per 1M tokens (blended) and output speed (tokens/s). Today's point: those are *general* benchmarks; your table is *your* task on *your* docs. Write down where they agree or disagree with what you measure.
- **Eugene Yan, Patterns for Building LLM-based Systems** (https://eugeneyan.com/writing/llm-patterns/): read the Evals, RAG and Caching sections at least. Note 3 takeaways in the plan's "Takeaways" section (e.g. evals before changes, retrieval quality vs generation quality, caching as a cost lever).

### Three metric definitions for the table

- **Cost per answer** = the `/ask` call's own tokens × `ModelPrices` (judge calls excluded: they're the cost of measuring, not of serving). Average over the runs that reached the model (E5 gated runs cost $0 and are excluded, say so). Also show "per 1,000 answers" so the numbers are readable.
- **Latency** = `retrievalMs + generationMs` from `/ask`'s `timings`, median and p95 over the runs. **Trap:** a cache hit returns in ~0 ms, so every cell must make fresh model calls (`AILAB_EVAL_CACHE_KEY=matrix-2026-11-05`). llama3.2 runs on your Mac, Haiku over the network: say so next to the numbers.
- **Quality**: no single number is trusted alone (the judge scored 0.63 / 0.71 against your labels). So the table shows:
  - **Hand pass rate** (the headline): pass/fail against the gold answer on a fixed 12-question subset, run 1 of each cell (48 reads).
  - **Groundedness** (judge, all 90 runs): a signal, as on Tuesday.
  - **Retrieval** (no model): Recall@5, hit@1, MRR. Depends only on the chunker, so the same for both models.
  - **Abstain correct**: the 4 "Not in the docs" rows + E5: did the answer say the docs don't cover it (read by hand, 3 runs each = 15 per cell; same rule as Tuesday).

---

## Steps

### Step 0 · Me + CC · Set up and write the decision rule first (~20 min)

- [x] CC: create branch `stage-5-experiment-matrix`; check `docker compose ps` (Postgres up), Ollama up, `GET /dev/chunks?chunker=fixed` returns 21 chunks and `heading` 50 (re-ingest with `POST /dev/ingest?chunker=both` if not).
- [x] Me: **before any run**, write the decision rule in `experiments.md` section 17 ("Rule, written before the runs"). Suggested shape, edit the numbers to yours:
  1. Drop any cell with hand pass rate below 10/12 or abstain correct below 12/15.
  2. Among the rest, pick the lowest cost per answer.
  3. If two cells are within 1 pass of each other, the cheaper wins; if cost ties, the lower p95 wins.
  4. p95 latency above 5 s disqualifies.

  Why first: if you choose the rule after seeing the numbers, you'll write the rule that picks the cell you already like.
- [x] Me: pick the 12-question hand-scored subset. Suggested (one+ per category): A3, N1 (single fact), F5, G2 (exact), B7 (list), B6, G1 (two sources), C3 (false premise), H6 (typo), D1 (partial), D9 (not in docs), H5 (non-English). Write it in section 17.

### Step 1 · CC · Let the eval choose the chunker (~30 min)

- [ ] Add `AILAB_EVAL_CHUNKER` (`heading` | `fixed`, default: none = the host's `Ask:Chunker`) to `AiLab.Evals/Hosting/EvalSettings.cs`, validated against `ChunkerNames` (`lines` refused).
- [ ] `AiLab.Evals/Hosting/EvalRagFactory.cs`: when set, `builder.UseSetting("Ask:Chunker", value)`. Nothing else changes: `EvalFixture.Ask` reads the host's `AskSettings`, so `/ask`, the judge's `/search` context and `RetrievalEvalTests` all follow.
- [ ] Default execution name includes the chunker when set (e.g. `matrix-fixed-haiku`), so the 4 cells land in 4 result groups.
- [ ] Update the env var list in `EvalSettings.cs`'s header comment and in `docs/evals/meai-evals.md`.

### Step 2 · Me (CC runs the commands) · Free checks before paying (~20 min)

- [ ] Retrieval per chunker (free, ~1 s each): `AILAB_EVAL=1 AILAB_EVAL_CHUNKER=heading dotnet test AiLab.Evals --filter RetrievalEvalTests`, then `fixed`. Note hit@1, hit@3, hit@5, MRR for `vector`. Expectation from section 12: fixed misses the 3-line ADR (diluted in a 2,000-char window).
- [ ] Gate check: with fixed chunks, does any answerable row's best distance go above `MaxDistance` 0.50 (would be gated = an automatic fail)? Read it from the retrieval report or `/search`.
- [ ] **Ollama context check** (CLAUDE.md "Watch"): fixed top 5 is ~2,200 tokens of chunks + the prompt. Send one FL question and compare `/ask`'s `usage.inputTokens` with llama3.2's context size (`ollama show llama3.2` / the `num_ctx` in use). If the prompt is near or over it, Ollama silently cuts it: record that, it decides whether FL is a fair cell.

### Step 3 · CC · The matrix report (~45 min)

- [ ] `AiLab.Evals/Summary/MatrixReport.cs`: reads the stored results of the 4 executions (same source as `EvalSummary`) and builds one markdown table: cell, runs reaching the model, gated, Groundedness avg, Recall@5, avg `/ask` tokens in/out, cost per answer and per 1,000 answers (`EvalSummary.Cost` / `ModelPrices`, `/ask` turns only), latency median and p95 (retrieval + generation ms). Columns for hand pass rate and abstain correct left as `?` for Me to fill.
- [ ] Triggered like the summary: `AILAB_EVAL_MATRIX=matrix-heading-haiku,matrix-heading-llama,matrix-fixed-haiku,matrix-fixed-llama`, writes `eval-runs/meai/matrix-2026-11-05.md`.
- [ ] Reuse `EvalSummary`'s run records and `Cost`; don't duplicate parsing.

### Step 4 · CC runs, Me watches · The four runs (~1 h wall time, ~$0.70)

Order: free cells first (wiring), then paid. All with `AILAB_EVAL=1 AILAB_EVAL_RUNS=3 AILAB_EVAL_CACHE_KEY=matrix-2026-11-05`:

| Cell | Env | Expected cost |
|---|---|---|
| HL | `AILAB_EVAL_CHUNKER=heading AILAB_EVAL_ANSWERS=llama` | ~$0.14 (judges) |
| FL | `AILAB_EVAL_CHUNKER=fixed AILAB_EVAL_ANSWERS=llama` | ~$0.15 (judges, longer context) |
| HH | `AILAB_EVAL_CHUNKER=heading AILAB_EVAL_ANSWERS=haiku` | ~$0.19 |
| FH | `AILAB_EVAL_CHUNKER=fixed AILAB_EVAL_ANSWERS=haiku` | ~$0.22 |

- [ ] After each: `AILAB_EVAL_SUMMARY=<execution>` for the per-question summary. Stop and look if a cell exceeds $0.40 or shows setup failures; re-run only the failed rows.
- [ ] Then `AILAB_EVAL_MATRIX=...` → the matrix table.

### Step 5 · Me · Hand scoring (~1 h)

- [ ] In `eval/results/2026-11-05-matrix-hand-scores.md`: for each cell, run 1 of the 12 subset questions: **pass / fail + one-line reason** against the gold answer in `ask-set-v1.md` (pass = the gold facts are there, nothing invented, cites the right source; partial lists fail). 48 lines.
- [ ] Abstain correct: the 15 abstain runs per cell (D2, D9, INV, N9, E5 × 3): did the answer say the docs don't cover it? Count per cell.
- [ ] Where the hand score and Groundedness disagree, note the question: those are the judge's blind spots for this setup.

### Step 6 · Me · The table and the decision (~30 min)

- [ ] `experiments.md` section 17: setup line, the rule (from step 0), the full table (CC pastes the generated part, you fill the hand columns), expected vs actual (costs, the ADR miss, the context check), the questions that moved between cells.
- [ ] Apply the rule. Write **the chosen configuration and its one-line justification**, e.g. "heading + Haiku: 12/12, $0.0005 per answer, p95 2.9 s; fixed + Haiku ties on quality at 2× the tokens." Your words, your numbers.
- [ ] Add a short "vs Artificial Analysis" note: does the general ranking hold on your task?

### Step 7 · CC · Apply and record (~20 min)

- [ ] If the choice differs from today's `Ask:Chunker` / provider: change `AiLab.Rag/appsettings.json` (Development still allowed on llama via `Chat__Provider`).
- [ ] Close the open item "default chat provider for `/ask`" in CLAUDE.md and `week-5-review.md` (Thursday entry: done, learned, next); update CLAUDE.md's current step; newcomer page `docs/evals/experiment-matrix-2026-11-05.md` if worth it.
- [ ] Commit after each step (conventions).

## Tests

- **Step 1, unit (offline):** `EvalSettings` parses `AILAB_EVAL_CHUNKER` (heading, fixed, unset → null, `lines` and unknown → error naming the variable); default execution name includes the chunker. In `AiLab.Evals` next to the existing settings tests. `dotnet test` (offline).
- **Step 3, unit (offline):** `MatrixReport` on fake run records: cost per answer excludes judge turns and gated runs; median and p95 on a known list; a missing execution is reported, not silently skipped; llama cost shows $0, an unknown model shows `?` (not $0). `dotnet test`.
- **Steps 2 and 4, evals (online, opt-in):** `RetrievalEvalTests` and `AskSetEvalTests` with `AILAB_EVAL=1` and the env vars above. Not part of plain `dotnet test`.
- **Whole solution:** plain `dotnet test` stays green and offline after steps 1 and 3.

## Decisions & trade-offs

- **Heading vs fixed-2000** rather than two fixed sizes: both already ingested, no new chunking code, and the result directly answers "keep `Ask:Chunker = heading`?". Cost: size and boundary rule are confounded.
- **Haiku vs llama3.2**: both wired, settles the open provider question; llama is free to answer, the judge stays Haiku.
- **Fresh calls for every cell** (new cache key): latency must be real, and all four cells measured on the same day. Costs ~$0.70 instead of ~$0.35.
- **Hand pass rate is the headline quality number**, judge scores are signals: the judge hasn't reached the 0.8 bar.
- **Decision rule written before the runs**, so the data decides.
- **Equal top 5 for both chunkers**, not equal tokens: it's the setting `/ask` actually uses.

## Alternatives considered

- Fixed-800 vs fixed-2000 (cleaner single variable, needs a configurable size and new chunker names): rejected for today's time budget.
- Haiku vs Sonnet 5.5 (~$2 for the matrix): rejected, llama is the real alternative being weighed.
- Hand-scoring all 360 answers: too long; 12-question subset × run 1 plus the judge over all runs.

## Out of scope

- Hybrid search in the matrix (stays `vector`, decided Wednesday).
- Top-k as a third axis, weighted RRF, the per-claim faithfulness judge, full layer 3 scoring.
- Changing the prompt.

## Changes

- **2026-11-05, step 0:** section 17 (written from the recommendations, before any run) changed two things from this plan. The hand-scored subset swaps H5 → A15 and D9 → N5: H5 misses at retrieval with both chunkers, so it can't tell the cells apart, and D9 belongs to the abstain count. That leaves 12 answerable questions. Abstain recall uses the eval plan's bar, at least 11 of 12 over the 4 "not in the docs" rows, with E5 checked on its own (gated 3/3 in every cell) instead of a combined 12/15. Checked: Postgres up, Ollama has `llama3.2` and `nomic-embed-text`, the `corpus` table has 21 fixed and 50 heading chunks.

## Takeaways from the readings

(Fill in during step 0.)
