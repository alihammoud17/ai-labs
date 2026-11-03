# Tuesday 3 Nov: automated quality scoring in `dotnet test` (MEAI evaluation)

- **Date:** 2026-11-03
- **Branch:** `stage-5-meai-evals` (from `main` after the Task 1 docs commit)
- **Goal:** Run the 30-question set through `POST /ask` from an xUnit project, score every answer with MEAI's judge evaluators and our own recall@5, cache the model calls, and record the first baseline.
- **Status:** in-progress
- **Approval:** confirmed via /save-plan (user OK on 2026-11-03)

Tutor mode: **Me** steps are the user's to write. **CC** steps are Claude's (scaffolding, DI, caching, test data).

## Deliverables and what "done" means

| Deliverable | Done when |
|---|---|
| xUnit eval project with groundedness, relevance and retrieval evaluators | `AiLab.Evals` runs the 30 rows with `AILAB_EVAL=1`; each answered run has a Groundedness, Relevance, Retrieval and Recall@5 metric in the stored results; plain `dotnet test` is still offline and green |
| Response caching enabled | a second run with the same execution name makes no paid call: proved by `CacheHit` on every turn, near-zero time, and unchanged Anthropic usage |
| Baseline scores recorded | the HTML report is generated; `experiments.md` section 15 has per-question scores, per-category averages, tokens, cost, time; the judge is compared with the week 4 hand labels; a short summary is in `docs/evals/` |

## What the docs say (checked 2026-11-03, packages v10.10.0)

Names below are from learn.microsoft.com and the `dotnet/extensions` source at the commit the API docs link to.

| Evaluator | Metric name | Context class (pass in `additionalContext`) | Reads |
|---|---|---|---|
| `GroundednessEvaluator` | `Groundedness` (`GroundednessEvaluator.GroundednessMetricName`) | `GroundednessEvaluatorContext(string groundingContext)` | question, answer, context |
| `RelevanceEvaluator` | `Relevance` | none | question, answer |
| `RetrievalEvaluator` | `Retrieval` | `RetrievalEvaluatorContext(IEnumerable<string> retrievedContextChunks)` (or `params string[]`) | question and chunks only; **never reads the answer** |

- **Scale:** each returns a `NumericMetric` from 1 (poor) to 5 (excellent), with a `Reason`.
- **Built-in interpretation:** > 4 Exceptional, > 3 Good, > 2 Average, > 1 Poor, ≤ 1 Unacceptable. `Failed` = score below **4.0** (so a 3.5 is "Good" but failed). No score → Inconclusive and failed.
- **Missing context:** Groundedness and Retrieval make no model call; they add an error diagnostic and return no score. So a forgotten context shows up as a diagnostic, not as a low score.
- **The judges' own `ChatOptions`:** `Temperature = 0`, `TopP = 1`, `PresencePenalty = 0`, `FrequencyPenalty = 0`, `ResponseFormat = Text`, `MaxOutputTokens` 800 (Groundedness) and 1600 (Retrieval). See step 5.5 for why this matters with Haiku.
- **Tuned for GPT-4o.** The docs warn results "can be especially poor when a smaller / local model is used". Another reason not to judge with llama3.2.
- **Reporting:** `DiskBasedReportingConfiguration.Create(storageRootPath, evaluators, chatConfiguration, enableResponseCaching = true, timeToLiveForCacheEntries = null, cachingKeys = null, executionName = "Default", evaluationMetricInterpreter = null, tags = null)`. Then `await using ScenarioRun run = await config.CreateScenarioRunAsync(scenarioName, iterationName = "1", additionalCachingKeys, additionalTags)`, `run.ChatConfiguration!.ChatClient`, `await run.EvaluateAsync(messages, response, additionalContext)`. Results are written when the `ScenarioRun` is disposed.
- **Report tool:** `dotnet tool install --create-manifest-if-needed Microsoft.Extensions.AI.Evaluation.Console`, then `dotnet tool run aieval report --path <storageRootPath> --output report.html`.

## How caching works here (point 5)

- **What's cached:** every call made through `scenarioRun.ChatConfiguration.ChatClient`. MEAI wraps our client in a `ResponseCachingChatClient` backed by a disk cache under `storageRootPath`. That covers the judges' calls always, and `/ask`'s own generation **only if** `/ask` uses that same client (step 5.4 makes it).
- **Cache key:** a hash of the request messages, the `ChatOptions`, the client's metadata (provider, model, endpoint), the reporting configuration's `cachingKeys`, and always the **scenario name and iteration name**. Change any of them and it's a miss. The docs warn the keys aren't stable across library versions, so upgrading the package empties the cache in practice.
- **Expiry:** 14 days by default (`timeToLiveForCacheEntries` changes it). After that the call goes to the model again.
- **What isn't cached:** embeddings (Ollama, free) and the Postgres search. They run every time, so a cached run still needs Ollama and Postgres up.
- **Proof that a second run is cached:** run the same execution name twice and compare: (1) every stored turn has `CacheHit = true` in the results JSON (`ChatTurnDetails`; verify the property name in step 5.6), (2) wall time drops from minutes to seconds, (3) the Anthropic console's usage for the day doesn't move, (4) cost per run in our summary is $0 for cached turns. Record all four in section 15.

## Repeated runs vs the cache (point 6)

- The iteration name is part of the cache key. So `iterationName: "1"`, `"2"`, `"3"` for the same question are **three different cache entries**: the first time, they are three real model calls. Runs 2 and 3 don't get run 1's answer.
- So the baseline is: one test row per question, looping over iterations 1..`AILAB_EVAL_RUNS` (default 3), each with its own `ScenarioRun`. First run = 3 real answers per question. Re-running the **same** execution name later re-scores from the cache for free. A **new** baseline (after a prompt change) gets real calls anyway, because the prompt is in the request and so in the key.
- To force fresh answers without changing anything else: a new `AILAB_EVAL_CACHE_KEY` value, passed as an extra caching key.

## Judge model (point 7)

- **Haiku 5.5 judges**, through our `ChatClientFactory.Create("anthropic", "claude-haiku-5-5", key)`, key from the Rag host's configuration (same user-secrets). llama3.2 is too weak to judge, and the docs say so too.
- **Risk 1, temperature:** the judges send `Temperature = 0` and `TopP = 1`; Haiku accepts only the default temperature (CLAUDE.md). → `HaikuJudgeOptionsClient : DelegatingChatClient` clones the options and removes `Temperature`, `TopP`, `PresencePenalty`, `FrequencyPenalty`.
- **Risk 2, cut-off judges:** Haiku thinks by default and the thinking counts toward `MaxOutputTokens`. Groundedness allows only 800, the limit that cut off B7 in week 4. → the same client raises `MaxOutputTokens` to at least 2000. A cut-off judge would show as a missing score with a diagnostic, so step 5.6 also counts those.
- **Risk 3, response format:** the judges ask for `ResponseFormat.Text` and parse a tagged score from the text, not JSON, so Haiku's JSON quirk doesn't apply. Keep the format as is.
- **Order of the wrappers:** cache (MEAI) → `HaikuJudgeOptionsClient` → Anthropic client. The cache key uses the evaluator's original options, which is fine.
- **Limitation, self-grading:** Haiku judges Haiku's own answers. It may be blind to its own kind of mistakes. Noted, not solved today; the llama3.2 run and the hand-label comparison give a first check.

**Cost estimate for one Haiku baseline** ($0.10 in / $0.50 out per 1M):

| Part | Calls | Tokens per call (in / out, thinking included) | Cost |
|---|---|---|---|
| `/ask` answers | 29 rows × 3 (E5 is gated, no call) = 87 | ~3,500 / ~400 | ~$0.05 |
| Groundedness | 87 | ~3,000 / ~400 | ~$0.05 |
| Relevance | 87 | ~800 / ~300 | ~$0.02 |
| Retrieval | 87 | ~2,800 / ~400 | ~$0.04 |
| **Total** | ~350 | | **~$0.16** |

llama3.2 answers with Haiku judging: ~$0.11. Judge-vs-labels check (44 week 4 runs, Groundedness only): ~$0.02. All three: **under $0.30**. A cached re-run: $0.

## The 1–5 scores vs the eval plan (point 8)

- The eval plan scores pass/fail. MEAI scores 1–5. Today we **record** the scores and their built-in interpretation (rating, `Failed`), and nothing asserts on them: an eval test fails only for a broken setup (HTTP error, missing metric, diagnostics).
- Then we check whether the judge agrees with me. The week 4 failure log has 44 hand-labelled runs. Groundedness is run on those recorded answers (the chunks are fetched again with `/search`: same corpus, same embeddings, same top 5). Positives = runs I labelled M1 or M4 (claim not in the sent chunks). Compare with `Failed` (score < 4): precision and recall of "not grounded", the eval plan's bar is about 0.8 for both.
- Expected blind spot: **M2 (wrong citation)**. Groundedness gets all sent chunks as one context, so a fact cited to the wrong chunk still counts as grounded. llama3.2's M2 runs are reported separately as "the judge can't see this", not as judge misses.

## Steps

- [x] **5.0** (CC) Branch `stage-5-meai-evals`, this plan's status to in-progress, CLAUDE.md current step.
- [x] **5.1** (CC) The test set as data: `docs/evals/ask-set-v1.json`, next to the `.md` it comes from, read with `RepoPaths`. One object per row: `id`, `category`, `question`, `expected` (`answer` / `partial` / `abstain` / `gate`), `check` (`exact` / `all items` / `judge` / `abstain`), `gold`: `[{ file, lines: [..] }]`, `goldMode` (`any`: one source answers alone; `all`: both needed), `goldAnswer`, `items` (for `exact` and `all items` checks). Copied from the `.md` without changing a value.
- [x] **5.2** (CC) `AiLab.Evals` project: xUnit v3 (same versions as `AiLab.Rag.Tests`), references `AiLab.Rag`, `AiLab.Infrastructure`, `AiLab.Domain`; packages `Microsoft.Extensions.AI.Evaluation`, `.Quality`, `.Reporting` (10.10.0, matching `Microsoft.Extensions.AI` 10.11); `.config/dotnet-tools.json` with `Microsoft.Extensions.AI.Evaluation.Console`; added to `AiLab.slnx`; `eval-runs/` is already git-ignored.
- [x] **5.3** (CC) `EvalRagFactory : WebApplicationFactory<Program>`: Development, the real Postgres store and Ollama embeddings (nothing faked), `Ask:PermitsPerMinute` raised to 1000 for the run, and the keyed `"provider"` chat client replaced by a `ScenarioChatClient` (see 5.4). A fixture checks once that `/search` answers (Postgres up, corpus ingested) and fails with a clear message if not.
- [x] **5.4** (CC) Route `/ask`'s generation through the cache: `ScenarioChatClient` is a `DelegatingChatClient`-style forwarder whose target is set per iteration (an `AsyncLocal`). With `AILAB_EVAL_ANSWERS=haiku` the target is `scenarioRun.ChatConfiguration.ChatClient` (cached). With `llama` it's the Ollama client from `ChatClientFactory` (free, not cached). Program.cs's `UseLogging` still wraps it, as in `RagFactory`.
- [x] **5.5** (CC) `HaikuJudgeOptionsClient` (drops `Temperature`, `TopP` and the two penalties, raises `MaxOutputTokens` to ≥ 2000, keeps the rest, never mutates the caller's options) and the reporting setup: `DiskBasedReportingConfiguration.Create(storageRootPath: eval-runs/meai/, evaluators: [Groundedness, Relevance, Retrieval, Recall@5], chatConfiguration: new(cached Haiku judge), enableResponseCaching: true, executionName: AILAB_EVAL_EXECUTION or "baseline-2026-11-03-<answers model>", cachingKeys: [AILAB_EVAL_CACHE_KEY if set])`. Scenario name: `{TestContext.Current.TestClass.TestClassName}.{category}.{id}`, so the report groups by category (it splits names on `.`). Tags: `expected`, `check`, answers model.
- [ ] **5.6** (Me) The evaluator wiring in the test. `[Theory]` over the JSON ids, `Assert.SkipWhen(AILAB_EVAL is null, ...)`. Per iteration: create the `ScenarioRun`, call `GET /search` (same `q`, `top` 5, `chunker=heading` as `/ask` uses) for the chunk texts and line ranges, call `POST /ask`, build the `ChatResponse` from `answer`, then `EvaluateAsync([user question], response, additionalContext: [GroundednessEvaluatorContext(sent chunks joined), RetrievalEvaluatorContext(chunk texts in rank order), our recall context])`. Skip the judges for gated or abstained runs (no answer to judge), but still score retrieval. Assert only on setup errors.
- [ ] **5.7** (Me) `RecallAtKEvaluator : IEvaluator`, no model call. A hit = a retrieved chunk from the gold file whose `StartLine..EndLine` contains a gold line. Metric `Recall@5` as a `NumericMetric`: `goldMode any` → 1 if any gold source is hit, else 0; `all` → share of gold sources hit. Also report the rank of the first hit (for MRR later). No value and no failure for rows without gold (`abstain`, `gate`). Interpretation: 1 = Good, below 1 = failed.
- [ ] **5.8** (CC) A small results summary: after the run, read the stored results and write `eval-runs/meai/<execution>/summary.md` (one line per question and iteration: scores, `Failed`, recall, tokens, cache hit; averages per category; totals for tokens, cost, time). Saves copying 90 rows by hand from the report.
- [ ] **5.9** (Me) Baseline on Haiku: `AILAB_EVAL=1 AILAB_EVAL_ANSWERS=haiku dotnet test AiLab.Evals`, then the same command again to prove the cache (see "How caching works"). Then `llama`. Then `dotnet tool run aieval report --path eval-runs/meai --output eval-runs/meai/report.html`.
- [ ] **5.10** (CC data, Me verdict) Judge vs hand labels: `eval/results/2026-10-31-labelled-runs.json` (the 44 week 4 runs: id, model, question, top, answer word for word, my codes), copied from the failure log. A second eval test runs Groundedness only on those and writes the confusion table. I read it and decide whether the judge can be trusted.
- [ ] **5.11** (Me) `experiments.md` section 15 (expected vs actual, scores per question, averages per category, tokens, cost, time, cache proof, judge vs labels) and a short summary in `docs/evals/` (the baseline's numbers next to the eval plan's provisional thresholds). CC may draft the llama3.2 tables (as in earlier sections); the Haiku numbers and the conclusion are mine.
- [ ] **5.12** (CC) Docs and PR: CLAUDE.md (new project in Stack, `AILAB_EVAL*` variables, cache folder, current step), README (project list, how to run the evals and the report), `docs/evals/README.md` (where the MEAI scores fit), PR.

Commit after each step.

## Tests

Plain `dotnet test` must stay offline and green. Today: 229 tests, 228 pass, 1 skipped (Postgres). After this plan: the offline ones below pass, and the eval tests show as skipped (30 rows + the judge-check rows).

- **5.1, unit (`AiLab.Evals/AskSetTests.cs`, offline):** the JSON has 30 rows with unique ids; the per-category counts match the table in `ask-set-v1.md` (25 answerable, 5 not); every `answer` / `partial` row has a gold source; every gold line exists in its `data/corpus/` file; `abstain` / `gate` rows have none. Run: `dotnet test --filter AskSetTests`.
- **5.5, unit (`AiLab.Evals/HaikuJudgeOptionsClientTests.cs`, offline, a recording fake inner client):** `Temperature`, `TopP`, both penalties are removed; `MaxOutputTokens` 800 → 2000, 3000 stays 3000, null → 2000; `ResponseFormat` and messages are passed unchanged; the caller's `ChatOptions` object isn't changed; streaming gets the same treatment. Run: `dotnet test --filter HaikuJudgeOptionsClientTests`.
- **5.7, unit (`AiLab.Evals/RecallAtKEvaluatorTests.cs`, offline):** hit when a chunk's range contains a gold line; miss when the file matches but the range doesn't; `all` mode with one of two sources → 0.5; first-hit rank; a row without gold → no value, not failed. Written by me with the evaluator.
- **5.6 / 5.9, eval (`AiLab.Evals/AskSetEvalTests.cs`, needs `AILAB_EVAL=1`, Ollama, Postgres with the corpus ingested, and the Anthropic key):** one theory row per question, 3 iterations each. Run: `AILAB_EVAL=1 AILAB_EVAL_ANSWERS=haiku dotnet test AiLab.Evals --filter AskSetEvalTests`.
- **5.10, eval (`AiLab.Evals/JudgeAgreementTests.cs`, same requirements):** Groundedness on the 44 labelled runs. Run: `AILAB_EVAL=1 dotnet test AiLab.Evals --filter JudgeAgreementTests`.
- **5.3 / 5.4, eval smoke (`AiLab.Evals/EvalHostSmokeTests.cs`, added during implementation, see Changes; needs `AILAB_EVAL=1`, Ollama and Postgres, free):** `/search` returns the 5 heading chunks from Postgres, including A3's gold line; `/ask` answers through the `ScenarioChatClient` with llama3.2 (`usage.model`); `/ask` with no target set is a setup error. Run: `AILAB_EVAL=1 dotnet test AiLab.Evals --filter EvalHostSmokeTests`.
- **Cache proof (manual, 5.9):** the same eval command twice with the same execution name; compare cache hits, time, usage.

## Decisions & trade-offs

- **Chunk texts come from `GET /search`, not from `/ask`.** `/ask` returns only the cited sources' metadata, not the text of what it sent. Calling `/search` with the same question, top and chunker gives the same chunks (same embedding, same store), with text and line ranges, and needs no change to the production response.
- **Recall@5 is our own evaluator.** `RetrievalEvaluator` asks an LLM whether the chunks look relevant; it doesn't know our gold lines. The two answer different questions, so both are kept.
- **No assertions on scores yet.** The tutorial itself advises tracking trends in the report rather than failing builds on single scores. Thresholds come after the baseline and the judge check.
- **The JSON lives next to its `.md`** (`docs/evals/`), so a change to one is seen next to the other. The `.md` stays the human view; the JSON is what the tests read.
- **Same Rag host as production**, through `WebApplicationFactory`, so the eval measures the real `/ask` code path (gate, prompt, repair, `CitationCheck`).
- **Fixed execution name per baseline**, not a timestamp: the tutorial's timestamp would never let a re-run reuse the stored results under the same name.

## Alternatives considered

- **Running against a live `dotnet run` host over HTTP:** no cache for `/ask`'s answers (the client lives in the other process). Rejected.
- **Adding sent-chunk texts to `AskResponse`:** changes the public contract for a test need. Rejected for now; `/search` gives the same data.
- **llama3.2 as the judge:** free, but too weak, and the docs say the prompts suit GPT-4o-class models. Rejected.
- **One `ScenarioRun` with all 3 runs inside:** the iteration name is the documented way to keep runs apart in the cache and the report. Rejected.
- **MEAI's `CompletenessEvaluator` / `EquivalenceEvaluator` against the gold answers:** closer to the eval plan's correctness, but more 1–5 judge scores. Left for after the judge check.

## Risks

- **Haiku rejects the judges' options or cuts them off** → 5.5's client and its tests; count missing scores in the summary.
- **Self-grading** (Haiku judging Haiku) → noted; the llama3.2 run and the hand labels are the check.
- **The judge disagrees with my labels** → then its scores are recorded but not trusted; faithfulness stays scored by hand (as the eval plan says).
- **Ollama silent truncation** (CLAUDE.md "Watch") → the judges run on Haiku, so only llama3.2's own answers are at risk; `/ask` logs prompt tokens.
- **The `/search` chunks differ from what `/ask` sent** (e.g. if `Ask:*` and the query parameters drift) → both read the same settings; 5.6 asserts that every cited `[n]` source/lines appear in the `/search` top 5.
- **Cache keys change with a package upgrade** → a re-run costs real calls again; noted in the docs.
- **Rate limiter** → raised in `EvalRagFactory` only, not in `appsettings.json`.
- **Cost** → under $0.30 for everything above; a cached re-run is free.

## Out of scope

- Failing `dotnet test` on scores, and freezing thresholds (after the baseline).
- A per-claim faithfulness judge (one claim, one chunk, supported or not) as the eval plan describes. Groundedness is a coarser first step.
- Fixing self-grading (a different judge model).
- Layer 2 metrics beyond what falls out of the run (precision/recall of abstaining and the distance plot come from the same stored results, later this week).
- CI, safety evaluators, Azure storage.

## Changes

- **2026-11-03, 5.1:** three gold lines in `ask-set-v1.md` were off by 90, all in `docs/decisions.md` (the file has 63 lines): N3 111 → 21, N5 125 → 35, N6 137 → 47. The quoted sentences are on the new lines. Fixed in the `.md` and the JSON with the user's OK; every other gold line was checked against the corpus and matches.
- **2026-11-03, 5.2:** the evaluation packages and the `aieval` tool are **10.11.0** (latest, same as `Microsoft.Extensions.AI`), not 10.10.0. The tool manifest is `dotnet-tools.json` at the repo root (where this SDK puts it), not `.config/`. `aieval` commands: `report`, `clean-results`, `clean-cache`.
- **2026-11-03, 5.3–5.5:** added `EvalHostSmokeTests` (3 tests, free) to prove the eval host before any paid run: it starts with the shared user-secrets, reads Postgres, and routes `/ask` through the `ScenarioChatClient`. The `ScenarioChatClient` uses a plain field, not an `AsyncLocal`: TestServer doesn't flow the test's execution context into the request by default, and iterations run one after another. HTTP helpers (`EvalFixture.SearchAsync` / `AskAsync`) are in the fixture, so 5.6 is only the evaluation itself. Offline after 5.5: 258 tests, 254 pass, 4 skipped (Postgres + 3 smoke).
