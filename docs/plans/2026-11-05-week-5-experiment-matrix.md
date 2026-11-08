# Thursday 5 Nov: the experiment matrix (2 chunkers × 2 models)

- **Date:** 2026-11-05
- **Branch:** `stage-5-experiment-matrix` (from `main`)
- **Status:** done (2026-11-07)
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

- [x] Add `AILAB_EVAL_CHUNKER` (`heading` | `fixed`, default: none = the host's `Ask:Chunker`) to `AiLab.Evals/Hosting/EvalSettings.cs`, validated against `ChunkerNames` (`lines` refused).
- [x] `AiLab.Evals/Hosting/EvalRagFactory.cs`: when set, `builder.UseSetting("Ask:Chunker", value)`. Nothing else changes: `EvalFixture.Ask` reads the host's `AskSettings`, so `/ask`, the judge's `/search` context and `RetrievalEvalTests` all follow.
- [x] Default execution name includes the chunker when set (e.g. `matrix-fixed-haiku`), so the 4 cells land in 4 result groups.
- [x] Update the env var list in `EvalSettings.cs`'s header comment and in `docs/evals/meai-evals.md`.

### Step 2 · Me (CC runs the commands) · Free checks before paying (~20 min)

- [x] Retrieval per chunker (free, ~1 s each): `AILAB_EVAL=1 AILAB_EVAL_CHUNKER=heading dotnet test AiLab.Evals --filter RetrievalEvalTests`, then `fixed`. Note hit@1, hit@3, hit@5, MRR for `vector`. Expectation from section 12: fixed misses the 3-line ADR (diluted in a 2,000-char window).

  **Where the numbers are.** Each run writes one markdown file per mode under `eval-runs/retrieval/` (git-ignored). For this step you only need the two `vector` files:

  | Chunker | File | Read |
  |---|---|---|
  | heading | `eval-runs/retrieval/2026-11-05-vector-heading.md` | "Overall" table, row "English answerable (24)" |
  | fixed | `eval-runs/retrieval/2026-11-05-vector-fixed.md` | same table and row |

  Each file has four parts:
  1. "Overall" holds the four numbers to copy.
  2. "Per category" shows where the two chunkers differ.
  3. "Per question" gives each row's first-hit rank and its top 5, with gold chunks in **bold**.
  4. The H5 line sits apart from the English rows.

  The `comparison-*.md` files put the modes side by side. Ignore them today, and **don't trust their header**: it says "Heading chunker" even in `comparison-fixed.md` (hard-coded in `RetrievalReport.cs:101`; CC fixes it).

  **What to do (Me):**
  1. In `experiments.md` section 17, add a subsection `### Retrieval before the runs (free)` with this table, filled from the two files:

     | Chunker | hit@1 | hit@3 | hit@5 | MRR | Misses at 5 (English) |
     |---|---|---|---|---|---|
     | heading | | | | | |
     | fixed | | | | | |

  2. Open the two "Per question" tables side by side. List the rows of **your 12-question subset** whose first hit is **above 5** in a chunker. Top 5 is what `/ask` sends, so such a row is almost certain to fail in both models of that chunker. Write them under the table, e.g. "fixed: B6 (first hit 9), G1 (7) not in the top 5". This is a prediction to check against step 5.
  3. Check the ADR expectation: did fixed lose a short `docs/decisions.md` section that heading finds? Write "confirmed" or "not seen", naming the row.
  4. Look at the H5 line in both files. Section 17 left H5 out of the subset because "it misses at retrieval with both chunkers". Check that this still holds for fixed, and if it doesn't, add one line to section 17 saying so. **Don't change the subset now:** you'd be choosing it after seeing results, the thing the rule was written first to avoid.
- [x] Gate check: with fixed chunks, does any answerable row's best distance go above `MaxDistance` 0.50 (would be gated = an automatic fail)? Read it from the retrieval report or `/search`.

  The retrieval reports don't show distances. Two ways to get them:
  - **Before any run (recommended):** start Rag (`dotnet run --project AiLab.Rag`, port 5074), then CC runs a loop calling `GET /search?q=<question>&top=1&chunker=<c>&mode=vector` for every row of `ask-set-v1.json` and prints `id, category, distance` for both chunkers.
  - **After the FL run** (step 4, free answers): the per-question table of `eval-runs/meai/matrix-fixed-llama-summary.md` has a "Best distance" column.

  **What to do (Me):** in section 17, write one line per chunker: "answerable rows above 0.50: none" (or name them), plus E5's distance (it must stay **above** 0.50 to be gated, which is rule 1's third bar). If an answerable row is above 0.50 with fixed, it will be gated in both fixed cells: note it as a predicted fail, and don't change `MaxDistance`.
- [x] **Ollama context check** (CLAUDE.md "Watch"): fixed top 5 is ~2,200 tokens of chunks + the prompt. Send one FL question and compare `/ask`'s `usage.inputTokens` with llama3.2's context size (`ollama show llama3.2` / the `num_ctx` in use). If the prompt is near or over it, Ollama silently cuts it: record that, it decides whether FL is a fair cell.

  **Careful:** `ollama show llama3.2` prints `context length 131072`. That's the **most the model supports**, not what Ollama gives it. Ollama runs with a smaller window unless told otherwise (`OLLAMA_CONTEXT_LENGTH`, or `num_ctx` in the request), and `/ask` sets neither.

  **What to do (Me):**
  1. With Rag running on llama3.2 (the default), send the longest-context subset question with fixed chunks, e.g. B7 ("List every rate limit in the system."). Use `rag.http`, or ask CC to curl `POST /ask` once with `Ask__Chunker=fixed`. Note `usage.inputTokens`.
  2. Right after, run `ollama ps`. The `CONTEXT` column shows the window actually in use. Also check `echo $OLLAMA_CONTEXT_LENGTH`.
  3. Write in section 17: "FL prompt N tokens, Ollama window M → fits / cut". If N is within ~10% of M or over it, the FL cell is unfair. Raise the window for the run (`OLLAMA_CONTEXT_LENGTH=8192 ollama serve`) and say so in the setup line.

### Step 3 · CC · The matrix report (~45 min)

- [x] `AiLab.Evals/Summary/MatrixReport.cs`: reads the stored results of the 4 executions (same source as `EvalSummary`) and builds one markdown table: cell, runs reaching the model, gated, Groundedness avg, Recall@5, avg `/ask` tokens in/out, cost per answer and per 1,000 answers (`EvalSummary.Cost` / `ModelPrices`, `/ask` turns only), latency median and p95 (retrieval + generation ms). Columns for hand pass rate and abstain correct left as `?` for Me to fill.
- [x] Triggered like the summary: `AILAB_EVAL_MATRIX=matrix-heading-haiku,matrix-heading-llama,matrix-fixed-haiku,matrix-fixed-llama`, writes `eval-runs/meai/matrix-2026-11-05.md`.
- [x] Reuse `EvalSummary`'s run records and `Cost`; don't duplicate parsing.

### Step 4 · CC runs, Me watches · The four runs (~1 h wall time, ~$0.70)

Order: free cells first (wiring), then paid. All with `AILAB_EVAL=1 AILAB_EVAL_RUNS=3 AILAB_EVAL_CACHE_KEY=matrix-2026-11-05`:

| Cell | Env | Expected cost |
|---|---|---|
| HL | `AILAB_EVAL_CHUNKER=heading AILAB_EVAL_ANSWERS=llama` | ~$0.14 (judges) |
| FL | `AILAB_EVAL_CHUNKER=fixed AILAB_EVAL_ANSWERS=llama` | ~$0.15 (judges, longer context) |
| HH | `AILAB_EVAL_CHUNKER=heading AILAB_EVAL_ANSWERS=haiku` | ~$0.19 |
| FH | `AILAB_EVAL_CHUNKER=fixed AILAB_EVAL_ANSWERS=haiku` | ~$0.22 |

- [x] After each: `AILAB_EVAL_SUMMARY=<execution>` for the per-question summary. Stop and look if a cell exceeds $0.40 or shows setup failures; re-run only the failed rows.
- [x] Then `AILAB_EVAL_MATRIX=...` → the matrix table.

### Step 5 · Me · Hand scoring (~1 h)

- [x] In `eval/results/2026-11-05-matrix-hand-scores.md`: for each cell, run 1 of the 12 subset questions: **pass / fail + one-line reason** against the gold answer in `ask-set-v1.md` (pass = the gold facts are there, nothing invented, cites the right source; partial lists fail). 48 lines.

  **Where the answers are.** One JSON file per question and run:
  `eval-runs/meai/results/<execution>/AiLab.Evals.AskSetEvalTests.<Category>.<ID>/<run>.json`
  (e.g. `.../matrix-fixed-haiku/AiLab.Evals.AskSetEvalTests.List.B7/1.json`). The answer text is at `modelResponse.messages[0].contents[0].text`. The `[n]` citations map to the sources in `evaluationResult`'s "Ask outcome" metric, metadata `citations` (`[1] README.md:61-87 ...`). Easier: ask CC to print run 1 of your 12 questions for one cell into one readable file (`eval-runs/meai/matrix-<cell>-run1.md`: question, gold answer, answer, citations), or open `dotnet aieval report --path eval-runs/meai --output eval-runs/meai/report.html`.

  **How to score (Me), one cell at a time, in this order:** HH, FH, HL, FL. Doing one cell at a time keeps the bar steady, and starting with Haiku calibrates you on good answers first.
  1. For each question, open its row in `docs/evals/ask-set-v1.md`: gold answer, gold source and lines, `passIf`, and for lists `items`.
  2. Read the answer and decide with the rule from section 17, in this order (stop at the first fail):
     - **Facts:** every gold fact present? A list with an item missing → fail.
     - **Support:** any claim the sources don't say (including general knowledge) → fail.
     - **Citation:** the gold fact cites a source whose file and lines contain the gold line → otherwise fail (M2).
     - Extra correct, cited detail is fine. Wording doesn't matter.
  3. Write one line in the file:

     ```
     | Cell | ID | Pass | Reason (one line) | Failure type |
     | FH | B6 | fail | PostgreSQL version missing; its chunk wasn't in the top 5 | retrieval miss |
     ```

     Failure type, the same labels as week 4's failure log: `retrieval miss` (the gold chunk wasn't sent: check the citations / step 2 list), `wrong chunk` (sent, but cited the wrong one), `generation` (invented, missing, wrong value), `should have refused`.
  4. At the end of each cell: `HH: 11/12` and so on.
- [x] Abstain correct: the 15 abstain runs per cell (D2, D9, INV, N9, E5 × 3): did the answer say the docs don't cover it? Count per cell.

  Section 17's rule splits this: **abstain recall** = the 4 "Not in the docs" rows (D2, D9, INV, N9) × 3 runs = 12 per cell, bar 11 / 12; **E5** is checked on its own (gated 3 / 3).
  1. Read all 3 runs (`1.json`, `2.json`, `3.json`) of D2, D9, INV and N9 per cell. **Correct** = the answer says the docs don't cover the question. Citing what the docs *do* say nearby is fine. Answering from general knowledge (D9's definition of multi-tenancy) is not. Don't use `/ask`'s status: Tuesday showed `grounded` on most correct abstains.
  2. E5: the "Ask outcome" metric's `gate` is `too_far` in all 3 runs (the summary's per-run table shows it too). No reading needed.
  3. Write in the same file: `| Cell | D2 | D9 | INV | N9 | Recall /12 | E5 gated /3 |`, each row cell like `3/3`.
- [x] Where the hand score and Groundedness disagree, note the question: those are the judge's blind spots for this setup.

  The Groundedness score per question and run is in `eval-runs/meai/matrix-<cell>-summary.md` (per-run table, column G). A disagreement is **pass with G < 4**, or **fail (generation) with G ≥ 4**. List them under a heading `Judge vs me`, e.g. "HL D1: I fail (invented default), G = 5". Retrieval-miss fails with G = 5 aren't the judge's fault (the claim *is* supported by what was sent), so mark those separately.

### Step 6 · Me · The table and the decision (~30 min)

- [x] `experiments.md` section 17: setup line, the rule (from step 0), the full table (CC pastes the generated part, you fill the hand columns), expected vs actual (costs, the ADR miss, the context check), the questions that moved between cells.

  Under `### Results`, in this order:
  1. **The table.** CC pastes `eval-runs/meai/matrix-2026-11-05.md`. You replace the `?` in "Hand pass /12", "Abstain recall /12" and "E5 gated /3" with your counts from step 5, and add the retrieval columns (hit@1, MRR) from step 2: the same value for both models of a chunker.
  2. **Expected vs actual.** One line each for the "What I expect" bullets already in section 17 (retrieval, tokens and cost, latency, llama on fixed, winner): what you expected, what happened, and the number. Include the step 2 predictions: did the rows you flagged as "not in the top 5" fail?
  3. **Questions that moved.** From the hand-scores file, the subset questions whose pass/fail differs between cells, each with the reason. Group by axis: "chunker changed the result" (same model, different chunker) and "model changed the result" (same chunker, different model). That shows which knob matters more.
  4. **Judge vs me.** The disagreement list from step 5, one line of conclusion: did the judge's blind spots change from Tuesday's?
- [x] Apply the rule. Write **the chosen configuration and its one-line justification**, e.g. "heading + Haiku: 12/12, $0.0005 per answer, p95 2.9 s; fixed + Haiku ties on quality at 2× the tokens." Your words, your numbers.

  Go through the rule literally and write each step down, so a reader can check it:
  1. **Bars:** a small table, cell × bar (pass ≥ 10/12, abstain ≥ 11/12, E5 3/3, p95 ≤ 5 s), ✓ or ✗ with the number. Cells with any ✗ are out.
  2. **Highest pass rate** among the cells left.
  3. **Tie-break** only if two survivors are within 1 pass: cheaper cost per answer, then lower p95.
  4. **No survivor:** heading + Haiku stays, and you write which bar each cell missed (rule 4).

  Then the deliverable, as a bold line starting `**Chosen:**`, in one sentence: the cell, the numbers that decided it, and the runner-up and why it lost. Write it in your words. The plan's example only shows the shape.
- [x] Add a short "vs Artificial Analysis" note: does the general ranking hold on your task?

  2–4 sentences: the gap between Haiku 5.5 and Llama 3.2 3B on their quality index vs the gap in your hand pass rate. Their price vs your measured cost per answer: their blended price assumes a fixed input/output mix, while your `/ask` calls are ~93% input. Their output speed vs your latency: yours includes retrieval, and llama runs on your Mac. End with one line on what a general leaderboard can and can't tell you for this choice.
- [x] Copy the `**Chosen:**` line into `docs/reviews/week-5-review.md` (Thursday entry) and tell CC whether `Ask:Chunker` or the provider changes (step 7).

### Step 7 · CC · Apply and record (~20 min)

- [x] If the choice differs from today's `Ask:Chunker` / provider: change `AiLab.Rag/appsettings.json` (Development still allowed on llama via `Chat__Provider`).
- [x] Close the open item "default chat provider for `/ask`" in CLAUDE.md and `week-5-review.md` (Thursday entry: done, learned, next); update CLAUDE.md's current step; newcomer page `docs/evals/experiment-matrix-2026-11-05.md` if worth it.
- [x] Commit after each step (conventions).

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

- **2026-11-05, step 1:** two changes the plan didn't list. `EvalSettings.From(Func<string, string?>)` was added (`FromEnvironment()` calls it), so the tests pass their own values instead of setting environment variables, which every test in the process shares. `RetrievalEvalTests` adds `-<chunker>` to its report names when `AILAB_EVAL_CHUNKER` is set, so the heading and fixed reports don't overwrite each other, and it compares vector ranks with Tuesday's only on heading chunks (Tuesday's chunker), where a different chunker would otherwise fail the test. Tests: `AiLab.Evals/EvalSettingsTests.cs` (12 cases, offline). Wiring checked: `AILAB_EVAL_CHUNKER=fixed` retrieval writes `eval-runs/retrieval/2026-11-05-vector-fixed.md` with `chunker=fixed` in the query.

- **2026-11-05, Me steps detailed** (steps 2, 5 and 6): for each step, where its numbers and answers are stored, the order to work in, and what to write in section 17 and the hand-scores file. Found while doing it, with nothing changed: the `vector-fixed` report shows B6 (first hit 9) and G1 (7) outside the top 5 with fixed chunks, and H5 at rank 3, which contradicts step 0's reason for leaving H5 out. The `comparison-*.md` header always says "Heading chunker" (`RetrievalReport.cs:101`); CC fixes it.

- **2026-11-06, step 3:** `MatrixReport` (`AiLab.Evals/Summary/`) and its trigger `MatrixReportTests.Write_matrix` (`AILAB_EVAL_MATRIX`). Four differences from the plan. (1) Cost and tokens come from `/ask`'s own usage (the "Ask outcome" metadata), not from `Turns`: llama's answers never go through the cached client, so they aren't in `Turns`, and judge turns aren't serving cost. (2) "E5 gated /3" is counted automatically from the stored `gate`, not left `?`. (3) `EvalSummary.PriceOf` (nullable) was split out of `Cost`, so an unpriced model shows `?` and `Cost` keeps its old behaviour. (4) The file is named after the day it's written (`matrix-<date>.md`), not fixed at 11-05. Added a cache-hit count and note: Tuesday's stored Haiku baseline is the cached re-run, and the wiring check on it showed a latency of 0.01 s, the trap the plan warned about. Tests: `AiLab.Evals/MatrixReportTests.cs` (11 offline, plus the opt-in writer).

- **2026-11-06, step 4:** the four cells ran (HL $0.142, FL $0.170, HH ~$0.11 billed, FH ~$0.13 billed; the Retrieval judge doesn't read the answer, so in the Haiku cells it was served from the llama cells' cache). **Bug found:** every Haiku repair failed. The repair sent Haiku's first reply back with its signed thinking block, Anthropic refused it ("Invalid `signature` in `thinking` block"), and `/ask` answered 502. That cost HH F1 run 3 and all of FH's D1 and N2. Fixed in `AskService` (the repair sends only the reply's text), with an offline test (`The_repair_sends_back_only_the_reply_text_not_its_thinking`, failing before the fix). Only runs that need a repair take that path, so the rest of the data stays valid. Re-ran only the missing questions. D1 and N2 (FH) made real calls. F1's re-run came back entirely from the cache (0 ms), so F1 run 3 was made fresh under its own cache key (`matrix-2026-11-05-f1`) and runs 1–2 were put back from a backup with their original latencies. F1 run 3 took 10.8 s: a real, slow Haiku call, and it's in HH's p95 (3.30 s). None of the re-runs needed a repair, so the fix is proven by the offline test, not yet by a live repair. Matrix: `eval-runs/meai/matrix-2026-11-06.md`, 90 runs per cell.

- **2026-11-06, CC parts of steps 5 and 6:** the answers to read for each cell are in two git-ignored tables: `eval-runs/meai/matrix-<cell>-run1.md` (the 12 subset questions, run 1: question, gold, source, answer, citations, Recall@5 / rank, blank Pass / Reason / Failure type) and `matrix-<cell>-abstain.md` (D2, D9, INV, N9 and E5 × 3 runs). The hand-scores file `eval/results/2026-11-05-matrix-hand-scores.md` is set up as a template: one table per cell, the abstain table with E5 filled in (3/3 in every cell, from the stored `gate`), and the "Judge vs me" headings. Section 17 `### Results` has the matrix with hit@1 and MRR added and the hand columns still `?`. Still the user's: the scoring, the bars, the decision, the takeaways from the readings. Step 7 waits for the decision.

- **2026-11-07, steps 5–7 (done by Claude at the user's request; the user chose the options):** hand scores in `eval/results/2026-11-05-matrix-hand-scores.md`, strict reading on the two close calls (HH H6 fails without `pending_verification`; FL N9 run 1 "notInDocs" counts as an abstain). Hand pass HH 11, FH 9, HL 6, FL 1; abstain recall 12 / 12 / 6 / 7; E5 3/3 everywhere. Rule applied: only HH clears every bar. **Chosen: heading + Haiku.** Step 7 differs from the plan's wording: instead of Haiku everywhere with a `Chat__Provider` override for local runs, `appsettings.json` is Haiku and a new `appsettings.Development.json` keeps llama3.2 (user's choice), checked live: Development `/ask` answers with `llama3.2`. Newcomer page `docs/evals/experiment-matrix-2026-11-05.md`. Takeaways from the readings were written at the end, not before step 4. Offline suite: Evals 85 / 40 skipped, Api.Tests 168 / 2, Rag.Tests 83.

- **2026-11-08, repair fix checked live:** a Rag instance on Haiku with a test copy of `ask-v1.md` (scratchpad only) that asked for a prose first reply. Run 1: the repair call reached Anthropic and came back after 3.5 s, with no signature error (before the fix: 400 → 502 `provider_unavailable`). It ended in 502 `reply_unreadable` only because the test prompt also forced prose on the second reply. Run 2: the prompt allowed JSON when asked, and the result was 200 `grounded`, log "Ask repair worked", `repaired true`, 7,827 in / 760 out, 4.3 s. Cost about $0.003. The Rag instance on 5074 was restarted from `main`.

## Takeaways from the readings

Written at the end (2026-11-07), by Claude at the user's request. They weren't read before step 4 as planned.

- **Artificial Analysis (Haiku 5.5, looked up by the user on 2026-11-08):** Intelligence Index 25, $0.10 / $0.50 per 1M input / output tokens, 214 output tokens/s, 11.35 s to the first answer token, reasoning model; the page doesn't show which effort level it measured. Llama 3.2 3B: deprecated, no current data (2026-11-06). Prices match `ModelPrices`. Their time to first token is about 8× my median total time, because their prompts make it think longer than my short lookups at medium effort. The comparison is in section 17.
- **Eugene Yan, evals → this matrix:** his advice is to build task-specific evals before changing the system, and to prefer your own test set over general benchmarks. The matrix did exactly that: a fixed 30-question set, a rule written first, and the leaderboard used only for context.
- **Eugene Yan, RAG → the chunker result:** he notes that retrieval quality caps generation quality, and that chunk size is a trade-off between context and precision. Fixed-2000 windows confirmed it here: fewer distinct places reached the top 5, and B6, G1 and B7 lost a fact before the model ever saw it.
- **Eugene Yan, caching → the cost column:** caching cuts cost and latency for repeated inputs. It doesn't help `/ask` today, because every question is different. It did matter for *measuring*: the eval's response cache would have faked latency, which is why every cell used a fresh cache key.

Original instructions (Me, ~30 min, before step 4):

- **Artificial Analysis:** for Claude Haiku 5.5 and Llama 3.2 3B, write down quality index, blended price per 1M tokens and output speed (tokens/s), with the date you looked. Step 6's comparison note uses them.
- **Eugene Yan:** 3 bullets, each "pattern → what it means for `/ask` today". Read at least Evals, RAG and Caching. For example: which of his eval advice this matrix already follows (fixed set, the decision rule first), what his RAG section says about chunk size and hybrid retrieval vs your step 2 numbers, and whether caching would change the cost column (it wouldn't for `/ask` today: every question is different).
