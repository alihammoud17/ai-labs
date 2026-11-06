# How the /ask evals run (AiLab.Evals)

The first baseline and what it showed: [baseline-2026-11-03.md](baseline-2026-11-03.md).

`AiLab.Evals` runs the 30 questions of [ask-set-v1](ask-set-v1.md) through the real `POST /ask` and scores every answer. It uses `Microsoft.Extensions.AI.Evaluation` (MEAI) 10.11.0. New to the terms? See [README.md](README.md).

## What one question goes through

For each row of `ask-set-v1.json`, for each iteration (1 to `AILAB_EVAL_RUNS`, default 3):

```
1. ScenarioRun        name "AiLab.Evals.AskSetEvalTests.<category>.<id>", iteration "1" / "2" / "3"
2. GET  /search       the same question, top 5, heading chunker: the chunks /ask is about to send, with text and lines
3. POST /ask          the real endpoint (gate, prompt, repair, CitationCheck); Haiku or llama3.2 answers
4. EvaluateAsync      five evaluators, each reading the context it needs
5. dispose the run    the result is written to eval-runs/meai/results/<execution>/
```

`/ask` doesn't return the text of the chunks it sent, only the cited ones' file and lines. `/search` with the same settings returns the same chunks, so the judges get them from there. The test checks that every citation `/ask` returned is among the `/search` chunks; if not, the run is reported as a setup problem.

## The evaluators

| Metric | Evaluator | Model? | Context it reads | Answers |
|---|---|---|---|---|
| Groundedness | `GroundednessEvaluator` | judge | `GroundednessEvaluatorContext`: the 5 chunks, numbered with file and lines | Is the answer supported by what was sent? |
| Relevance | `RelevanceEvaluator` | judge | none (question + answer) | Does the answer address the question? |
| Retrieval | `RetrievalEvaluator` | judge | `RetrievalEvaluatorContext`: the 5 chunk texts, best first | Do the chunks look relevant, best first? It never reads the answer. |
| Recall@5, First hit rank | `RecallAtKEvaluator` | no | `GoldSourcesContext` (the row's gold lines) and `RetrievedChunksContext` (file and lines per rank) | Was a gold chunk sent, and at which rank? |
| Ask outcome | `AskOutcomeEvaluator` | no | `AskOutcomeContext` (the whole `/ask` response) | Status, gate, best distance, citations, model, tokens, times |

- **Judge scores** run from 1 to 5. MEAI's own reading: above 4 Exceptional, above 3 Good, above 2 Average, above 1 Poor. Below **4.0** counts as failed, so a 3.5 is "Good" and still failed.
- **Recall@5:** a hit is a chunk from the gold file whose line range contains a gold line. For rows where one source is enough (`goldMode any`): 1 or 0. For B6 and G1, where both are needed (`all`): the share of gold sources found, so one of two is 0.5 and failed. Rows with no gold (`abstain`, `gate`) get no value and don't fail.
- **First hit rank** feeds MRR later (1 / rank, averaged).
- **Ask outcome** has no pass/fail. Whether abstaining was right depends on the row, and the status alone can mislead: in week 4 Haiku said "not in the docs" on D2 with status `grounded`, because it cited what the chunks did say.

### When /ask abstains

Groundedness and Relevance are wrapped in `AnsweredOnlyEvaluator`. When the gate stopped the question (E5) or the model said "not in the docs", there is no answer to rate. The wrapper returns an empty metric marked "not judged" and makes no model call. Retrieval and Recall@5 still run, because the chunks were found either way.

### What makes the test fail

Only a broken setup: an HTTP error from `/search` or `/ask`, a missing metric, an evaluator error (a missing context, or a judge reply without a readable score), or a citation outside the `/search` chunks. With `AILAB_EVAL_JUDGE=llama` (the free wiring check), a judge reply without a readable score is only a warning: llama3.2 can't score, and the check is about everything else working. **Low scores never fail the test.** The thresholds come after the baseline, as in the [eval plan](eval-plan.md). Problems are collected across the iterations and reported together, so one bad run doesn't hide the others.

## The judge

- **Haiku 5.5** by default. Its scores are the ones that count.
- The MEAI evaluators send `Temperature 0`, `TopP 1`, zero penalties and `MaxOutputTokens` 800 (Groundedness) or 1600 (Retrieval). Haiku refuses any temperature but its default, and its thinking counts toward the output limit. **`HaikuJudgeOptionsClient`** sits between MEAI and the Anthropic client. It drops those sampling options, raises the output limit to at least 2000, and leaves the caller's options untouched, because MEAI builds the cache key from them.
- **Self-grading:** with Haiku answering and judging, Haiku rates its own answers. It may miss its own kind of mistakes. Running with llama3.2 answers, and comparing the judge with the week 4 hand labels, are the first checks.
- **Blind spot:** Groundedness gets all 5 chunks as one context. A right fact cited to the wrong chunk (M2) still counts as grounded.
- **Timeout:** 60 s per attempt, 2 retries. Under the SDK's default (10 minutes per attempt), one stuck call held the first llama3.2 run for about 35 minutes.

## Caching

- Every call through `scenarioRun.ChatConfiguration.ChatClient` is cached on disk under `eval-runs/meai/cache/<scenario>/<iteration>/` (git-ignored). The cache covers the judges' calls, and `/ask`'s own answer when Haiku answers: the eval host's chat client is swapped per iteration (`ScenarioChatClient`) to point at that cached client.
- **The key** is built from the request messages, the options, the model and endpoint, the scenario name and the iteration name (plus `AILAB_EVAL_CACHE_KEY` if set). Entries expire after **14 days**.
- **Iterations are separate entries.** The first run of an execution makes 3 real answers per question. Running it again re-scores from the cache.
- **llama3.2 answers aren't cached** (they don't go through the run's client). On a re-run, llama3.2 gives a new answer, so the two judges that read the answer (Groundedness and Relevance) miss the cache. Retrieval, which reads only the question and the chunks, hits. Seen on the first dry run: Retrieval answered from the cache in 7 ms, and the other two went back to the model.
- **Shared across executions:** the key has the scenario and iteration but not the execution name. So the llama3.2 run's Retrieval calls (question and chunks only, the same whoever answers) were served from the Haiku run's cache: 91 free calls.
- **Not cached at all:** embeddings and the Postgres search. A cached run still needs Ollama and Postgres up.
- **Proof of a cached run:** every turn in the result JSON has `"cacheHit": true` (under `chatDetails.turnDetails`), the run takes seconds instead of minutes, and the Anthropic usage doesn't move.

## How to run it

Requirements: Ollama running, Postgres up (`colima start`, then `docker compose up -d`), the corpus ingested (`POST /dev/ingest?chunker=heading`), and the Anthropic key in user-secrets for a Haiku judge.

| Variable | Values | Default |
|---|---|---|
| `AILAB_EVAL` | any value turns the eval tests on | off: plain `dotnet test` skips them |
| `AILAB_EVAL_ANSWERS` | `haiku` / `llama`: who answers `/ask` | `haiku` |
| `AILAB_EVAL_JUDGE` | `haiku` / `llama`. `llama` is free but only checks the wiring; it needs `AILAB_EVAL_ANSWERS=llama` | `haiku` |
| `AILAB_EVAL_RUNS` | iterations per question | 3 |
| `AILAB_EVAL_EXECUTION` | the name results are grouped under | `baseline-2026-11-03-<answers>`, or `dev-llama-judge-llama` |
| `AILAB_EVAL_CACHE_KEY` | any new value forces fresh model calls | none |
| `AILAB_EVAL_CHUNKER` | `heading` / `fixed`: the chunks `/ask`, the judges' context and the retrieval runner use (sets the host's `Ask:Chunker`; both must be ingested). Sets the default execution name to `matrix-<chunker>-<answers>` and adds `-<chunker>` to the retrieval report names | none: the host's `Ask:Chunker` (`heading`) |
| `AILAB_EVAL_SUMMARY` | an execution name: writes `eval-runs/meai/<execution>-summary.md` (per run, per question, per category) from the stored results; no model call | none |
| `AILAB_EVAL_MATRIX` | execution names, comma-separated: writes `eval-runs/meai/matrix-<date>.md`, one row per execution with runs, gated, E5 gated, Groundedness, Recall@5, `/ask` tokens, cost per answer and per 1,000 (`/ask`'s call only, gated runs excluded; `?` for a model without a price), latency median and p95 (retrieval + generation), cache hits; hand-scored columns left `?`. A missing execution is listed, not skipped; cache hits are flagged because cached answers make latency look ~0 | none |

```bash
# free check of the wiring: llama3.2 answers and judges, one question, one run
AILAB_EVAL=1 AILAB_EVAL_ANSWERS=llama AILAB_EVAL_JUDGE=llama AILAB_EVAL_RUNS=1 dotnet test AiLab.Evals --filter 'DisplayName~A3'

# the Haiku baseline (paid, about $0.16), then the same command again to prove the cache
AILAB_EVAL=1 AILAB_EVAL_ANSWERS=haiku dotnet test AiLab.Evals --filter AskSetEvalTests

# the experiment matrix table from four stored executions (no model call)
AILAB_EVAL_MATRIX=matrix-heading-haiku,matrix-heading-llama,matrix-fixed-haiku,matrix-fixed-llama dotnet test AiLab.Evals --filter 'FullyQualifiedName~MatrixReportTests.Write_matrix'

# the HTML report
dotnet aieval report --path eval-runs/meai --output eval-runs/meai/report.html
```

The report groups scenarios by the dots in their names: test class, then category, then question.

## First dry run (2026-11-03, llama3.2 answering and judging, free)

- **The wiring works end to end.** A3 was answered by llama3.2 through the real `/ask` (grounded, cites `[1] docs/contracts.md:43-65`, 1,751 input / 29 output tokens). Recall@5 was 1 (gold line 45 at rank 1). E5 was gated: Groundedness and Relevance were "not judged" with no model call, and only Retrieval ran.
- **llama3.2 can't judge.** All three judge replies said "Score" with no number, so MEAI couldn't parse a score and the test reported it as an evaluator error. Since then, llama-judge mode turns that one error into a warning, so the wiring check passes when the rest works. This matches the MEAI docs' warning about small local models, and it's why Haiku is the judge.
- **Judge prompt sizes** (llama3.2 tokens; Haiku counts will differ a little): Groundedness ~1,900 input, Relevance ~950, Retrieval ~4,100. Retrieval is bigger than the plan's estimate of ~2,800.

## Files

| File | What it holds |
|---|---|
| `AskSetEvalTests.cs` | the eval: one theory row per question, iterations, contexts, setup checks |
| `Evaluators/RecallAtKEvaluator.cs` | Recall@5, first hit rank, and the two contexts they read |
| `Evaluators/AskOutcome.cs` | `AskOutcomeContext`, `AskOutcomeEvaluator`, `AnsweredOnlyEvaluator` |
| `Judging/HaikuJudgeOptionsClient.cs` | the options fix for Haiku as judge |
| `Judging/EvalReporting.cs` | storage, caching, execution name, scenario names, tags |
| `Hosting/` | `EvalRagFactory` (the real Rag host), `ScenarioChatClient`, `EvalFixture` (`/search` and `/ask` helpers, judge, Ollama), `EvalSettings` (the variables above) |
| `AskSet/AskSet.cs` | loads `docs/evals/ask-set-v1.json` |
| `AskSetTests.cs`, `RecallAtKEvaluatorTests.cs`, `HaikuJudgeOptionsClientTests.cs` | offline tests, run by plain `dotnet test` |
| `EvalHostSmokeTests.cs` | free checks of the eval host (`AILAB_EVAL` only) |
