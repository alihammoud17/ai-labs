# Bedrock Model Evals Day Plan: Compare Models with Your Own Evals

- **Date:** Thu 19 Nov 2026 (Week 7, theme Bedrock via MEAI), worked later
- **Branch:** `stage-7-bedrock-evals` (from `main`), not created yet
- **Status:** not started
- **Focus:** Compare models with your own evals: the 30-question `/ask` suite on two Bedrock models, and what one **correct** answer costs on each
- **Where:** `AiLab.Evals` (gold check, evaluator, summary, report, settings, fixture), `docs/evals/ask-set-v1.json` + `ask-set-v1.md` (match lists), `docs/evals/` and `experiments.md`. Nothing in `AiLab.Rag`, `AiLab.Application` or `AiLab.Infrastructure` changes.

**Tutor mode, strict.** I type every command and every line of code. Claude Code starts each task with a **push**, waits while I work, reviews, and ticks the box only after the check passes. It writes code only when I say "hand it over".

After each finished task, Claude adds a short **How we did it** note under it when I ask.

## How each task runs

1. **Push** (Claude Code, before I start): goal and "Done when" · the files to open · 1–3 things to read first (a doc section and similar ai-labs code) · the first step as a hint, in words · the likely trap.
2. **I do it**, following the task's numbered steps, and ask when stuck.
3. **Review** (Claude Code): runs the task's **Check**, compares with **Done when**, says what's wrong and where. It doesn't fix it.
4. **Tick**, with a one-line `> Result (date):`. Then the next push.

Every task below has the same parts: **You'll build** · **Open** · **Steps** · **Check** · **Done when** · **Why**.

## Where things stand

| Block | Tasks | Status |
|---|---|---|
| 0. Setup | 0.1 | done |
| 1. Understand the suite, write the rule | 1.1–1.4 | done |
| 2. The gold check: "correct" without a judge | 2.1–2.5 | done |
| 3. Answers from stored results + agreement with week 5 | 3.1–3.3 | done |
| 4. The cost-per-correct report → D2 (measured) | 4.1–4.4 | not done |
| 5. Bedrock as the answerer, no Anthropic judge → D1 | 5.1–5.4 | not done |
| 6. Smoke run on the emulator → D1 wiring | 6.1 | not done |
| 7. Projected Bedrock rows + Artificial Analysis → D2 | 7.1–7.2 | not done |
| 8. Real-account runbook + housekeeping | 8.1–8.3 | not done |

**Order:** as listed. Blocks 2–4 run fully offline on week 5's stored results, so D2's measured table is real today.

## Deliverables (adapted: no AWS account, the emulator runs no model)
- [ ] **D1. Eval suite run on 2 Bedrock models:** the suite answers with **Nova Lite** (`us.amazon.nova-lite-v1:0`) or **Claude Haiku 5.5 on Bedrock** (`us.anthropic.claude-haiku-5-5`) by environment variable, with no Anthropic judge, and is smoke-tested on aws-emulator. The full run (2 models × 30 questions × 3 runs) waits for the real account: on the emulator every Bedrock `/ask` ends in a 502 (section 22).
- [ ] **D2. Cost per correct answer table:** `docs/evals/cost-per-correct-2026-11-19.md` (dated with the plan's date, passed to the report as `2026-11-19`), generated from stored results. **Measured** today for week 5's four cells (HH, FH, HL, FL: 90 stored runs each, answers and citations included). **Projected** for the two Bedrock models, in a separate table, until the real run.

## Resources
- Microsoft Learn, Evaluate with caching and reporting (tutorial): https://learn.microsoft.com/en-us/dotnet/ai/evaluation/evaluate-with-reporting
- Microsoft Learn, The Microsoft.Extensions.AI.Evaluation libraries (evaluators, custom evaluators, reporting): https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries
- Artificial Analysis, model quality, price, speed: https://artificialanalysis.ai/
- Bedrock pricing: https://aws.amazon.com/bedrock/pricing/
- In the repo:
  - `docs/evals/README.md` (glossary), `docs/evals/eval-plan.md`, `docs/evals/ask-set-v1.json` (30 rows: `expected`, `check`, `items`, `gold`, `goldMode`, `goldAnswer`) and its human view `ask-set-v1.md`
  - `docs/evals/experiment-matrix-2026-11-05.md` (week 5: rule written first, Correct /12, cost per 1,000)
  - `eval-runs/meai/results/matrix-{heading,fixed}-{haiku,llama}/` (stored runs, git-ignored, local only)
  - `eval/results/2026-11-05-matrix-hand-scores.md` (week 5 hand verdicts: run 1 of the 12 hand-scored rows per cell, abstain recall per run; the rule was facts → support → citation). The answers they were read from are in `eval-runs/meai/matrix-{HH,FH,HL,FL}-{run1,abstain}.md` (their Pass columns are blank: the verdicts live in the hand-scores file).
  - `AiLab.Evals/Evaluators/RecallAtKEvaluator.cs` (a model-free `IEvaluator`: the pattern for the gold check; `IsHit` and `RetrievedChunk` for the citation step), `Evaluators/AskOutcome.cs`, `AskSet/AskSet.cs` (`AskSetRow`), `AskSetTests.cs`, `Summary/EvalSummary.cs` (`Parse`, `RunRecord`, `PriceOf`), `Summary/MatrixReport.cs` (+ `MatrixReportTests.cs`: the pattern for the report and for an env-gated writer, `AILAB_EVAL_MATRIX`), `Hosting/EvalSettings.cs` (+ tests), `Hosting/EvalFixture.cs`, `Judging/EvalReporting.cs`, `AskSetEvalTests.cs`

## Scope rules for today
- **No AWS account, no Anthropic calls.** `AILAB_EVAL_JUDGE=none` for every eval run today. Bedrock calls go to aws-emulator, after the safety check.
- **Correct = checked by code against the gold answer.** 16 of the 30 rows can be: 5 `exact`, 6 `all items`, 4 `abstain` + 1 gate. Code checks what week 5's hand rule checked, except support:
  - **Answer rows** (`exact`, `all items`): **facts** — every entry of the row's `match` list is found (any one of its alternatives) — **and citation** — the cited lines contain a gold line (`goldMode` `any`: one gold source cited; `all`: every gold source cited).
  - **Abstain rows:** status `abstained` **or** the answer says the docs don't cover it (a fixed phrase list, written before any run). Haiku cites nearby facts and gets status `grounded` while abstaining in words; week 5's rule was "ignore the /ask status".
  - **The gate row (E5):** correct only if the gate stopped it (`too_far` or `no_hits`).
  - The 14 `judge`/`partial` rows get **no** verdict from code (never counted wrong). Their hand pass stays a step of the real run, as in week 5.
- **Cost per correct answer** = `/ask`'s own cost over all checked runs (judges excluded, from the `Ask outcome` metadata, never from `chatDetails`) ÷ correct runs. Gated runs made no call and add $0. With 0 correct it's "n/a", never $0. Local llama is $0 per answer: say "free (local hardware not counted)".
- **Projected is labelled projected** and never shares a table with measured numbers. **"No data" never looks like "0% correct".**

---

## Block 0: Setup (10 min) — done

### Task 0.1 — Branch and a green start ✅
- **You'll build:** the branch, with this plan committed on it, and the emulator, Postgres and Ollama up.
- **Steps:**
  1. `git switch main && git pull && git switch -c stage-7-bedrock-evals`, then commit this plan (it is untracked on `main` and comes along): `git add docs/plans/2026-11-19-week-7-bedrock-model-evals.md && git commit -m "Week 7 Thursday plan: Bedrock model evals"`.
  2. `docker ps`: `aws-emulator` listed? If not, `docker/bedrock-kb/bedrock-kb-emulator.sh`.
  3. The safety check from `docker/aws-setup/README.md` (line 18) → `SAFE`.
  4. Postgres up (`docker compose up -d`), Ollama running.
  5. `dotnet test`, and write down the number of passing tests.
  6. `ls eval-runs/meai/results/`: the four `matrix-*` folders are there, 90 `.json` files each (Blocks 3–4 need them).
- **Check:** `git branch --show-current` → `stage-7-bedrock-evals`; `git log -1` is the plan commit; tests green.
- **Done when:** all six steps pass. If the `matrix-*` folders are missing, stop and tell Claude: D2's measured table depends on them.

> Result (2026-11-22): branch `stage-7-bedrock-evals`, plan committed as `50ac1e3`; `aws-emulator` and `ai-labs-vectors-1` running, safety check `SAFE`, Ollama answering; `dotnet test` 401 passed, 43 skipped, 0 failed (Evals 85, Api.Tests 225, Rag.Tests 91); four `matrix-*` folders, 90 `.json` each.

---

## Block 1: Understand the suite, write the rule (30 min) — done

### Task 1.1 — How one eval run works ✅
- **You'll build:** 5 lines in your notes.
- **Open:** `AiLab.Evals/AskSetEvalTests.cs` (`Ask_set_v1`), `Hosting/EvalFixture.cs`, `Judging/EvalReporting.cs`.
- **Steps:** answer in one line each:
  1. Who answers `/ask`? (Follow `fixture.Chat.Target`.)
     > **Answer:** `/ask` answers through `ScenarioChatClient` → `Chat.Target`, set per run (`AskSetEvalTests.cs:61`): Haiku 5.5 via the run's cached client (`run.ChatConfiguration.ChatClient`), or llama3.2 via `fixture.Ollama` (not cached).
  2. Who judges, and how is the judge created? (`EvalFixture.CreateJudge`, and the line in `Ask_set_v1` that first touches `fixture.Judge`.)
     > **Answer:** Haiku 5.5 (llama3.2 with `AILAB_EVAL_JUDGE=llama`, wiring checks only), built lazily by `CreateJudge` on the first `fixture.Judge` read (`AskSetEvalTests.cs:51`): Anthropic key from user-secrets, 60 s per attempt / 2 retries, wrapped in `HaikuJudgeOptionsClient` (drops Temperature/TopP/penalties, `MaxOutputTokens` ≥ 2000); `EvalReporting.Create` puts it in the `ChatConfiguration`, where MEAI caches it.
  3. Which evaluators run, and which ones need a model?
     > **Answer:** 5 evaluators (`AskSetEvalTests.cs:43-50`): Groundedness and Relevance (both wrapped in `AnsweredOnlyEvaluator`, so skipped with no model call when gated or abstained) and Retrieval (always) use the judge; Recall@5 + First hit rank (`RecallAtKEvaluator`, gold lines vs chunk line ranges) and Ask outcome (`AskOutcomeEvaluator`, status, gate, citations, model, tokens, timings) need no model.
  4. What is cached, and what's in the cache key? (The comment above `EvalReporting.Create`.)
     > **Answer:** Cached: every call through the run's `ChatConfiguration.ChatClient` (the judges, plus `/ask`'s answer when Haiku answers; llama3.2 via `fixture.Ollama`, `/search` and embeddings never). Key = request + options + model + scenario + iteration (+ `AILAB_EVAL_CACHE_KEY` when set), kept 14 days in `eval-runs/meai/cache/` (`EvalReporting.cs:19-21`). Same execution again = free re-score; a new cache key or a renamed scenario = fresh calls.
  5. Where does one run's result land on disk, and what's inside? (Open one `.json` under `eval-runs/meai/results/matrix-heading-llama/`: find `modelResponse`, the `Ask outcome` metadata with `model`, tokens and `citations`, and notice whose call the `chatDetails` turns are.)
     > **Answer:** `eval-runs/meai/results/<execution>/<scenario>/<iteration>.json`. Inside: the question (`messages`), `/ask`'s answer (`modelResponse`, empty when gated), every metric (`evaluationResult.metrics`); the Ask outcome's value is the status and its metadata holds gate, best distance, citations, model, input/output tokens, retrieval/generation ms; plus tags (`answers:`, `judge:`, `expected:`, `check:`, `category:`). `chatDetails` holds only calls through the cached client: in a llama cell, the 3 Haiku judge turns and no `/ask` turn, so `/ask`'s cost comes from the Ask outcome metadata.
- **Done when:** five lines written. Claude checks them.
- **Why:** Block 5 changes line 1 (who answers) and line 2 (no judge). Block 3 reads what you found in line 5; the `chatDetails` turns are the judge's, so cost must come from the `Ask outcome` metadata.

> Result (2026-11-22): five answers written under the steps and checked against `AskSetEvalTests.cs`, `EvalFixture.cs`, `EvalReporting.cs` and `matrix-heading-llama/…F1/1.json`.

### Task 1.2 — The tutorial, the parts you'll use ✅
- **Open:** the "Evaluate with caching and reporting" tutorial.
- **Steps:**
  1. Read the sections on `ReportingConfiguration`, scenario runs and iterations, and response caching.
  2. Read how `dotnet aieval report` builds the HTML report.
  3. Write down: what is a scenario, an iteration, an execution; what does a cache hit cost; which calls get cached (only those through `ScenarioRun.ChatConfiguration.ChatClient`).
     > **Answer (scenario, iteration, execution):** Scenario = one question, results grouped and averaged by its name (`EvalReporting.ScenarioName(row)` → `AiLab.Evals.AskSetEvalTests.<category>.<id>`, a folder under the execution). Iteration = one repeat of the same scenario (`iterationName: i.ToString()`, `AskSetEvalTests.cs:58`, runs 1–`AILAB_EVAL_RUNS`; each its own cache key and file `<n>.json`). Execution = one whole run of the set, the group compared with other runs (`settings.ExecutionName` from `AILAB_EVAL_EXECUTION` or `DefaultExecutionName`, e.g. `matrix-heading-haiku`; the top folder under `eval-runs/meai/results/`); the same name again re-scores from the cache, it adds no runs.
     > **Answer (cache hit cost):** nothing: no model call, `$0` and ~0 ms, the reply read from `eval-runs/meai/cache/`. The `chatDetails` turn gets `cacheHit: true` but keeps the original token usage (so `EvalSummary` shows billed vs uncached cost), and latency is understated (`MatrixReport` flags hits; HH had 89 in week 5), so a run whose latency counts needs a new `AILAB_EVAL_CACHE_KEY`. Week 5: Haiku baseline `$0.19`, cached re-run 3 s and `$0`.
     > **Answer (which calls get cached):** only calls through `ScenarioRun.ChatConfiguration.ChatClient`: MEAI puts its cache in front of the client passed as `ChatConfiguration` (`EvalReporting.Create`). In ai-labs: the judge calls (Groundedness, Relevance, Retrieval), plus `/ask`'s answer when Haiku answers (`Chat.Target` = that client, `AskSetEvalTests.cs:61`). Not cached: llama3.2 answers (`fixture.Ollama`, outside the configuration), `/search`, embeddings, and the no-model evaluators (no call to cache). With no `ChatConfiguration`, nothing is cached.
- **Done when:** three definitions written, each with the ai-labs name it maps to (e.g. scenario = `AskSetEvalTests.<category>.<id>`), plus the caching sentence.

> Result (2026-11-22): scenario, iteration and execution defined with their ai-labs names (`ScenarioName`, `iterationName`, `ExecutionName`); a cache hit is `$0` and ~0 ms with the original usage kept; only calls through `ScenarioRun.ChatConfiguration.ChatClient` are cached (judges, Haiku answers; never llama3.2).

### Task 1.3 — The two definitions, with week 5's numbers ✅
- **Open:** `docs/evals/experiment-matrix-2026-11-05.md`, "The result".
- **Steps:**
  1. Copy the scope rules' definitions of "correct" and "cost per correct answer" into your notes.
     > **Answer (correct):** checked by code against the gold answer, on the 16 gold-checkable rows only (5 `exact`, 6 `all items`, 4 `abstain`, 1 gate). Answer rows: every fact of the row's `match` list found (any alternative) **and** the cited lines contain a gold line (`any`: one gold source cited; `all`: every one). Abstain rows: status `abstained` or the answer says the docs don't cover it. Gate row (E5): the gate stopped it (`too_far` / `no_hits`). The 14 `judge` / `partial` rows get no verdict (never counted wrong); support (nothing invented) stays a hand or judge step.
     > **Answer (cost per correct answer):** `/ask`'s own cost over all checked runs ÷ correct runs. Cost from the `Ask outcome` metadata only (judges excluded, never `chatDetails`); gated runs made no call and add `$0`. 0 correct → "n/a", never `$0`; llama3.2 → "free (local hardware not counted)".
  2. HH: 12/12 correct at `$0.44` per 1,000 answers. FH: 9/12 at `$0.68`. Work out cost per correct for each, per 1,000 correct answers.
     > **Answer:** cost per correct = cost per answer ÷ accuracy. HH: `$0.44` ÷ (12/12) = **`$0.44`** per 1,000 correct answers. FH: `$0.68` ÷ (9/12) = `$0.68` × 12/9 = **`$0.91`** per 1,000 correct answers (FH needs 1,333 answers for 1,000 correct ones).
  3. Compare the gap per answer with the gap per correct answer. One sentence.
     > **Answer:** per answer FH costs 1.55× HH (`$0.68` vs `$0.44`, +`$0.24`), but per correct answer it costs 2.06× (`$0.91` vs `$0.44`, +`$0.47`): a quarter of its answers are wrong, so its real price nearly doubles the gap its token price shows.
  4. One line on the approximation: the correct rate is over the 12 hand-scored rows (run 1), the cost per answer over all 87 runs that reached the model. Block 4 removes it: both come from the same checked runs.
     > **Answer:** the two numbers come from different runs: the accuracy (12/12, 9/12) is from run 1 of the 12 hand-scored rows, the cost per answer (`$0.44`, `$0.68`) from all 87 runs per cell that reached the model, so `$0.91` assumes FH is wrong as often on the other 18 rows and runs 2–3 as on those 12; Block 4 computes both over the same gold-checked runs.
- **Done when:** both numbers and the two sentences written; Claude checks the arithmetic.
- **Why:** it's the whole point of D2: a model that's wrong more often costs more per correct answer than its price suggests.

> Result (2026-11-22): both definitions copied from the scope rules; per 1,000 correct answers HH `$0.44` (÷ 12/12), FH `$0.91` (`$0.68` × 12/9); FH 1.55× HH per answer but 2.06× per correct answer; approximation noted (12 hand-scored rows vs 87 reached runs), arithmetic checked.

### Task 1.4 — Write the decision rule, before any run ✅
- **Steps:** under this task, in the plan, write:
  1. The bars a Bedrock model must pass (suggestion: at least 15/16 gold-checked correct on run 1, 10/12 on the hand pass, p95 under 5 s).
  2. The tie-break: the lower cost per correct answer.
  3. What Artificial Analysis would have to show for you to distrust your own result.

  > **The rule (written 2026-11-22, before any Bedrock run):**
  >
  > **Bars: a Bedrock model is out if it misses any of these.**
  > 1. **Complete:** all 90 runs (30 rows × 3) are stored and no run has a setup error. Missing runs fail the model; a bar is never passed on 0 of 0.
  > 2. **Gold check, run 1:** ≥ 14/16 correct.
  > 3. **Gold check, all 3 runs:** ≥ 40/48 correct (83%, week 5's 10/12 share, held across runs).
  > 4. **Abstains:** ≥ 11/12 correct on D2, D9, INV, N9 × 3; E5 gated 3/3 (the gate runs before the model: a setup check, it can't separate models).
  > 5. **Hand pass, run 1:** ≥ 10/12 on the 12 `judge` answerable rows.
  > 6. **Latency:** p95 ≤ 5 s on an uncached run (new `AILAB_EVAL_CACHE_KEY`); a cached run doesn't count.
  >
  > **Winner:** among the models over every bar, the most correct on run 1 (gold check + hand pass, out of 28). A tie (equal totals) goes to the lower cost per correct answer. If neither model clears every bar, `/ask` stays on Haiku through the Anthropic API (HH).
  >
  > **When I'd distrust my own result:** only if Artificial Analysis ranks the two models the other way round **and** my gap is within noise (≤ 2 of 28 on run 1, or the leader changes between runs 1–3). Then I hand-check all 3 runs before deciding. Their index measures general benchmarks, not this corpus, so it never overturns a clear result. If their listed price differs from `ModelPrices` by more than 10%, I re-check the price before computing cost per correct.
- **Done when:** the rule is in the plan and committed **before** Block 6.
- **Why:** week 5's lesson: a rule written first stops you from choosing the rule that favours the model you already like.

> Result (2026-11-23): rule written under the steps and committed before any Bedrock run: six bars (complete 90 runs, gold check ≥ 14/16 run 1 and ≥ 40/48 all runs, abstains ≥ 11/12 + E5 3/3, hand pass ≥ 10/12, p95 ≤ 5 s uncached), winner = most correct of 28 on run 1, tie → lower cost per correct, neither passes → stay on HH; Artificial Analysis only reopens a result within noise (≤ 2 of 28).

---

## Block 2: The gold check, "correct" without a judge (90 min) — done

### Task 2.1 — Match lists in the ask set ✅
- **You'll build:** a `match` field on the 11 `exact` and `all items` rows of `docs/evals/ask-set-v1.json`, `Match` on `AskSetRow`, the same lists in `ask-set-v1.md`, and a test in `AskSetTests.cs`.
- **Open:** `docs/evals/ask-set-v1.json` (A3, F1, F4, F5, G2, A15, B7, N4, B6, G1, N7), `AskSet/AskSet.cs`, `AskSetTests.cs` (`Exact_and_list_checks_have_items`).
- **Steps:**
  1. `match` is a list of facts; each fact is a list of alternatives, all literal text an answer may contain. One fact per entry of `items`, in the same order. Example, G2: `[["10 per minute", "10 requests per minute"]]`; B7: one fact per limit (`30 per minute` for refresh, `5 attempts per 5 minutes` for MFA, `monthly` for the Redis limiter, …).
     > **Draft (2026-11-23), the 11 lists** (case-insensitive; the first alternative of every fact is gold-line text, checked against `data/corpus`; the others are rephrasings a correct answer may use):
     >
     > | Row | `match` |
     > |---|---|
     > | A3 | `[["2026-03-18"]]` |
     > | F1 | `[["b82b6991c877ed60e6a337a1928478d323770ca01846828fa0a77c424b19d8ae"]]` (hex only, so a dropped `sha256=` prefix still counts) |
     > | F4 | `[["POST /api/internal/billing/subscription-events"]]` (keeps `POST`: a bare URL stays wrong) |
     > | F5 | `[["Jwt__Secret"]]` |
     > | G2 | `[["10 per minute", "10 requests per minute"]]` |
     > | A15 | `[["contract version is supported", "supported contract version"], ["required fields and timestamps"], ["API-owned records"], ["stated tenant"], ["target plan"], ["already been applied", "already have been applied", "already applied"]]` |
     > | B7 | `[["register and login"], ["30 per minute", "30 requests per minute"], ["account-recovery", "account recovery"], ["5 attempts per 5 minutes"], ["monthly limit"]]` |
     > | N4 | `[["X-Tenant-Webhook-Contract-Version"], ["X-Tenant-Webhook-Timestamp"], ["X-Tenant-Webhook-Delivery"], ["X-Tenant-Webhook-Idempotency-Key"], ["X-Tenant-Webhook-Signature"]]` |
     > | B6 | `[["5 attempts per 5 minutes"], ["PostgreSQL 16"]]` |
     > | G1 | `[["2026-03-18"], ["2026-04-13"]]` |
     > | N7 | `[["dev.sh bootstrap"], ["dev.sh seed"], ["dev.sh run"]]` |
     >
     > Against week 5 (facts only, run 1, 7 overlapping rows × 4 cells = 28): agrees with the hand verdicts on 25; the 3 others (HL A3, FL A3, FL F5: right value, wrong chunk) are the citation step's job (Task 2.2). Known limits for §24: B7's two "10 per minute" limits (register and login, account recovery) are checked by their subject, not their number (a substring can't bind a number to its subject; G2 checks the 10); G2's "saying it's shared with register and login is a fail" is a support check code can't see.
     >
     > `ask-set-v1.md` line format: `**Match:** 10 per minute / 10 requests per minute` (facts separated by `;`, alternatives by `/`).
  2. `items` stays as it is (the human description). `match` is what code looks for: short fragments, no paraphrase of the whole item.
  3. Add `IReadOnlyList<IReadOnlyList<string>>? Match = null` to `AskSetRow` (a named optional parameter at the end, so no caller breaks).
  4. Add a "Match" line to each of the 11 rows in `ask-set-v1.md` (the file header says: change both together).
  5. A test: every `exact` / `all items` row has `match` with as many facts as `items`, no empty alternative; every other row has none.
- **Check:** `dotnet test --filter AskSetTests`.
- **Done when:** green, and every alternative is something a correct answer could say word for word.
- **Why:** `items` were written for a human reader. B7's "register and login: 10 per minute, shared" never appears in an answer as written, so a plain "contains" fails every B7 run, right or wrong. The lists are written after seeing week 5's answers: say so in §24.

**Done (handed over):** `match` added to the 11 `exact` / `all items` rows of `docs/evals/ask-set-v1.json` (the draft above, unchanged; one fact per item, in order). `AskSetRow` gets `IReadOnlyList<IReadOnlyList<string>>? Match = null` as its last parameter, with a comment (no caller changed; `JsonSerializerDefaults.Web` maps `match`). `ask-set-v1.md`: a paragraph under "Gold answers" explaining the Match line, and a `- Match (code, \`GoldCheck\`):` line in each of the 11 row blocks (facts `;`, alternatives `/`). `AskSetTests.Exact_and_list_checks_have_one_match_fact_per_item_and_the_others_none`: match count = items count, no empty fact or alternative, no match on the other 19 rows.

> Result (2026-11-23): `dotnet test --filter AskSetTests` 17/17 green; 11 rows with `match` (A3, F1, F4, F5, G2 one fact each; A15 6, B7 5, N4 5, B6 2, G1 2, N7 3), each first alternative found in `data/corpus`.

### Task 2.2 — A pure function that judges one answer ✅
- **You'll build:** `AiLab.Evals/Evaluators/GoldCheck.cs`: a static method that takes an `AskSetRow`, the answer text, the `/ask` status, the gate and the cited chunks (`IReadOnlyList<RetrievedChunk>`), and returns a verdict (`Correct`, `Wrong`, `NotChecked`) with a reason.
- **Open:** `AskSet/AskSet.cs` (`AskSetRow`: `Check`, `Match`, `Gold`, `GoldMode`, `Expected`), `Evaluators/AskOutcome.cs` (status and gate strings), `Evaluators/RecallAtKEvaluator.cs` (`IsHit`, `RetrievedChunk`), `eval/results/2026-11-05-matrix-hand-scores.md` (the week 5 rule and its notes on Haiku's abstains).
- **Steps:**
  1. Define the verdict type: an enum plus a record `(Verdict, Reason)`.
  2. A `Normalize` helper: lowercase, strip backticks and `*`, collapse whitespace.
  3. Answer rows (`exact`, `all items`), **facts**: every fact of `Match` needs one alternative contained in the normalised answer. Reason: the facts missing, by their first alternative.
  4. Answer rows, **citation** (only when the facts pass): `RecallAtKEvaluator.IsHit(gold, cited)` against the cited chunks. `goldMode` `any`: at least one gold source cited; `all`: every gold source cited. Reason: the gold line no citation contains.
  5. `abstain` rows: correct if the status is `abstained` **or** the normalised answer contains one of a fixed list of abstention phrases (a `const` array, written now, e.g. "doesn't cover", "does not cover", "not in the docs", "notindocs", "doesn't define", "doesn't state"). Wrong otherwise, with the start of the answer as the reason.
  6. The gate row (`expected` `gate`): correct only if the gate is `too_far` or `no_hits`. A model abstain on E5 is wrong (the gate should have stopped it).
  7. `judge` and `partial` rows: `NotChecked`, reason "hand-scored row".
  8. No I/O, no model, no `AskResponse` type: plain values in (`RetrievedChunk` is a plain record), verdict out. That's what lets Block 3 reuse it on stored results.
- **Check:** `dotnet build`.
- **Done when:** it builds and the method signature takes only plain values.
- **Why:** one function used in two places (live evaluator, stored results) means the live run and the report can never disagree. Facts and citation are the two steps of week 5's rule that code can check; support (nothing invented) stays a hand or judge step.

**Done (handed over):** `AiLab.Evals/Evaluators/GoldCheck.cs`: `GoldVerdict` (Correct / Wrong / NotChecked), `GoldCheckResult(Verdict, Reason)`, `GoldCheck.Evaluate(AskSetRow row, string answer, string status, string gate, IReadOnlyList<RetrievedChunk> citations)`. Dispatch on `Expected == "gate"` first (E5's `Check` is `abstain`), then `Check`. Answer rows: facts from `Match` (each fact needs one normalised alternative in the normalised answer; reason lists the missing facts by their first alternative), then citation with `RecallAtKEvaluator.IsHit` (`any`: one gold source cited, `all`: every one), stopping at the first fail; an answer row without `Match` throws (the guard test of 2.1 prevents it). Abstain rows: status `abstained`, or one of `AbstainPhrases` (18 phrases, public; "not explicitly stated" left out on purpose). Gate row: `too_far` / `no_hits`. Others: NotChecked "hand-scored row". `Normalize` also straightens curly apostrophes (`'`), so "doesn't" matches either way.

> Result (2026-11-23): `dotnet build AiLab.Evals` succeeded, no warnings; signature takes only plain values. Phrase list vs week 5 abstain verdicts: 47/48 agree (the miss: FL D2 run 3, status abstained with an unrelated sentence, for 3.3).

### Task 2.3 — Unit tests from real rows ✅
- **You'll build:** `AiLab.Evals/GoldCheckTests.cs`.
- **Open:** `RecallAtKEvaluatorTests.cs` (how rows are loaded with `AskSetData.Row(id)`).
- **Steps:** one `[Fact]` or `[Theory]` row each:
  1. A3 with "The version is `2026-03-18` [1]", citing `docs/contracts.md:43-65` → Correct.
  2. A3 with "2026-03-17" → Wrong, reason names the missing fact.
  3. A3 with the right value but citing `docs/contracts.md:3-5` → Wrong, reason names the gold line (week 5's HL A3).
  4. G2 with "10 requests per minute", cited correctly → Correct (an alternative).
  5. One `all items` row with one fact missing → Wrong, reason names exactly that fact.
  6. The same row with all facts in another order and other casing → Correct.
  7. G1 (`goldMode` `all`) citing only the billing contract → Wrong.
  8. An `abstain` row: status abstained → Correct; status grounded with "The documentation doesn't cover how long…" → Correct; status grounded with an invented answer → Wrong.
  9. E5: gate `too_far` → Correct; gate `passed`, status abstained → Wrong.
  10. A `judge` row → NotChecked.
- **Check:** `dotnet test --filter GoldCheckTests`.
- **Done when:** all green, offline, no environment variables.

**Done (handed over):** `AiLab.Evals/GoldCheckTests.cs`, 15 tests (22 cases) on real rows loaded with `AskSetData.Row(id)`; each asserts the verdict and only the part of the reason that matters. The 10 steps: A3 correct (`docs/contracts.md:43-65` holds line 45), `2026-03-17` → "missing: 2026-03-18", week 5's HL A3 (`3-5`) → names `docs/contracts.md:45`; G2 both alternatives; N7 missing `dev.sh seed` (exact reason) and all three in another order, casing, spacing and backticks; G1 citing only the billing contract → names line 128, not 45; D2 by status, in words (straight and curly apostrophe, `notInDocs`) and an invented "7 days" → Wrong quoting it; E5 `too_far` / `no_hits` correct, `passed` + abstained wrong; A1 (judge) and D1 (partial) NotChecked. Added: no citation → Wrong; G1 with both sources → Correct; "not explicitly stated, but … 30 days" → Wrong (the phrase left out on purpose); an answer row with `Match = null` throws, naming the row.

> Result (2026-11-23): `dotnet test AiLab.Evals --filter GoldCheckTests`: 22 passed, 0 failed, 26 ms, no `AILAB_*` variables set.

### Task 2.4 — The evaluator around it ✅
- **You'll build:** `Evaluators/GoldCheckEvaluator.cs`, an `IEvaluator` with one `BooleanMetric` named "Gold check", and a small `AskSetRowContext` carrying the row.
- **Open:** `RecallAtKEvaluator.cs` (copy its shape: metric names, contexts, diagnostics; `GoldSourcesContext` carries only sources and mode, not the row), the MEAI libraries page, section on custom evaluators.
- **Steps:**
  1. `AskSetRowContext(AskSetRow row)`: an `EvaluationContext` like `GoldSourcesContext`, its text the row's ID, check and match list.
  2. Get the row from `AskSetRowContext`; status, gate and citations from `AskOutcomeContext.Response` (`Grounding.Status`, `Retrieval.Gate`, `Citations` → `RetrievedChunk`s); the answer text from `modelResponse.Text`.
  3. Call `GoldCheck`. `Correct` → value `true`; `Wrong` → `false` with `failed: true`; `NotChecked` → no value, interpretation `Inconclusive`, `failed: false` (exactly like `RecallAtK` does for abstain rows).
  4. Put the reason in the metric's `Reason`.
  5. A missing context is a diagnostic error, not a `false`.
- **Check:** `dotnet test --filter GoldCheck`: add 2 tests calling `EvaluateAsync` directly (one Correct, one NotChecked) and 1 with a context missing (diagnostic error).
- **Done when:** green.

**Done (handed over):** `AiLab.Evals/Evaluators/GoldCheckEvaluator.cs`: `GoldCheckEvaluator` (one `BooleanMetric` "Gold check", `GoldCheckMetricName`) and `AskSetRowContext(AskSetRow row)` (context name "Ask-set row (Gold check)", text e.g. `A3, exact, match: 2026-03-18`; facts `;`, alternatives `/`, as in ask-set-v1.md). It reads the row from `AskSetRowContext`, status and gate from `AskOutcomeContext` through its existing `Status`/`Gate` helpers (the same lowercase strings the stored metadata holds), citations as `RetrievedChunk(Source, StartLine, EndLine)`, the answer from `modelResponse.Text`, and calls `GoldCheck.Evaluate`. Correct → `true`, Exceptional; Wrong → `false`, Unacceptable, `failed: true`; NotChecked → no value, Inconclusive, `failed: false`; the reason in `Reason` and the interpretation. Either context missing → a diagnostic error naming it, no value. Tests in `GoldCheckEvaluatorTests.cs`: Correct (A3), Wrong (HL A3's `3-5` citation, added), NotChecked (A1), missing `AskSetRowContext`.

> Result (2026-11-23): `dotnet test AiLab.Evals --filter GoldCheck`: 26 passed (22 GoldCheckTests + 4 GoldCheckEvaluatorTests), 0 failed, no build warnings.

### Task 2.5 — Wire it into the suite ✅
- **Open:** `AskSetEvalTests.cs`: the `evaluators` array, the `context` array and `SetupProblems`.
- **Steps:**
  1. Add `new GoldCheckEvaluator()` to `evaluators`.
  2. Add `new AskSetRowContext(row)` to `context`.
  3. Add "Gold check" to the metrics `SetupProblems` expects.
- **Check:** `dotnet test` (whole solution, offline: the eval tests still skip without `AILAB_EVAL`).
- **Done when:** green, test count = Task 0.1's count + the new tests.

**Done (handed over):** `AskSetEvalTests.cs`: `new GoldCheckEvaluator()` last in `evaluators`, `new AskSetRowContext(row)` last in `context`, `GoldCheckEvaluator.GoldCheckMetricName` added to the metrics `SetupProblems` expects (so a missing "Gold check" metric or its missing-context diagnostic fails the run as a setup error, never as a wrong answer); the class comment lists the new metric. The evaluator reads `/ask`'s own citations (from `AskOutcomeContext`), not `/search`'s chunks: correct means the answer cited the gold line. No live run: the eval run rule is `AILAB_EVAL_JUDGE=none`, which arrives in 5.2; the first run that produces a "Gold check" metric is the smoke run in 6.1.

> Result (2026-11-23): `dotnet test` (whole solution, no `AILAB_*` variables): 428 passed, 43 skipped, 0 failed (Evals 112, Api.Tests 225, Rag.Tests 91) = Task 0.1's 401 + 27 new (1 guard test in 2.1, 22 in 2.3, 4 in 2.4); skipped unchanged at 43.

---

## Block 3: Answers from stored results + agreement with week 5 (60 min) — done

### Task 3.1 — Read the answer text and the citations into `RunRecord` ✅
- **You'll build:** `Answer` and `Citations` properties on `RunRecord`, filled by `EvalSummary.Parse` from the stored `modelResponse` and the `Ask outcome` metadata.
- **Open:** `Summary/EvalSummary.cs` (`Parse`, `RunRecord`), one stored `.json` (you looked at it in 1.1), `EvalSummaryTests.cs`, `MatrixReportTests.cs` (the one other `new RunRecord(...)`, line 27).
- **Steps:**
  1. In `Parse`, read `modelResponse.messages[*].contents[*]` where `$type` is `text`, and join the texts.
  2. Parse the `citations` metadata (`[1] docs/contracts.md:43-65 [2] …`) into `RetrievedChunk`s; empty string → empty list.
  3. Add `string Answer` and `IReadOnlyList<RetrievedChunk> Citations` to `RunRecord` (keep the record's other positions; update the `new RunRecord(...)` in `MatrixReportTests.cs`).
  4. Tests: parse a small hand-written JSON with a known answer and two citations → both read back; a JSON without `modelResponse` or `citations` → empty string and empty list, no exception.
- **Check:** `dotnet test --filter "EvalSummaryTests|MatrixReportTests"`.
- **Done when:** green.
- **Why:** the stored week 5 runs already hold every answer and citation. With them in `RunRecord`, D2's measured table needs no model call at all. Every stored run that reached the model has answer text, model and tokens (87 per cell); the 3 gated E5 runs have none, by design.

**Done (handed over):** `Summary/EvalSummary.cs`: `RunRecord` gains `string Answer` and `IReadOnlyList<RetrievedChunk> Citations`, appended after `Order` (no existing position moves; the one other caller, `MatrixReportTests.cs`, passes `"", []`). `Parse` joins the `text` parts of `modelResponse.messages[*].contents[*]` (other `$type`s such as reasoning skipped; `chatDetails` never read for it, it holds judge calls), `TryGetProperty` all the way, so no `modelResponse` → `""`. New public `EvalSummary.ParseCitations(string)`: splits on the `[n] ` markers, each piece at its last `:` and the `-` after it; empty or blank → empty list; a piece that doesn't parse throws `FormatException` (a dropped citation would turn a correct answer into a wrong one). Class made `partial` for the `[GeneratedRegex]`. Tests in `EvalSummaryTests.cs`: answer from two text parts (reasoning part ignored) + two citations; the existing `A3Run` (no `modelResponse`, no `citations`) → `""` and none; blank citations → none; last-colon split (`notes/a:b.md:7-9`); three malformed pieces throw.

> Result (2026-11-23): `dotnet test --filter "EvalSummaryTests|MatrixReportTests"`: 21 passed, 2 skipped (the env-gated writers), 0 failed; AiLab.Evals 120 passed. Stored week 5 runs: all 360 `citations` values match `source:start-end` pieces; 87 of 90 runs per cell have answer text (the 3 E5 runs gated), as the Why expects.

### Task 3.2 — Gold-check the four week 5 cells ✅
- **You'll build:** a small summary (a method + a writer test gated by `AILAB_EVAL_GOLDCHECK=<execution>,<execution>,...`, like `MatrixReportTests.Write_matrix` and `AILAB_EVAL_MATRIX`) that loads the listed executions and runs `GoldCheck` on every run.
- **Steps:**
  1. For each run: find its `AskSetRow` by `Id`, call `GoldCheck` with `Answer`, `Status`, the `gate` from `Outcome` and `Citations`.
  2. Per cell: checked runs, correct, wrong, not checked. Run 1 only and all 3 runs.
  3. Write it to `eval-runs/meai/goldcheck-week5.md` (git-ignored, a working file), with each Wrong run's reason.
- **Check:** `AILAB_EVAL_GOLDCHECK=matrix-heading-haiku,matrix-fixed-haiku,matrix-heading-llama,matrix-fixed-llama dotnet test AiLab.Evals --filter <your test>`; the counts per cell add up to 90 runs (30 × 3); 48 checked runs per cell (16 rows × 3).
- **Done when:** the file exists with four rows.

**Done (handed over):** `Summary/GoldCheckReport.cs`: `Check(RunRecord)` finds the row by `Id` and calls `GoldCheck.Evaluate` with `Answer`, `Status`, `Outcome["gate"]` and `Citations`; `Count` → `Counts(Runs, Checked, Correct, Wrong, NotChecked)`; `Markdown(cells)`: one row per cell (all runs; run 1 correct / checked; answer rows run 1 /11; abstain rows all runs /12; E5 gated /3), then per cell every checked row's three verdicts and each Wrong run's reason. A cell without results prints "no data" with a note that it is not 0 correct; a cell that isn't 90 runs / 48 checked is flagged incomplete. `GoldCheckReportTests.cs`: 4 offline tests (a stored run checked, counts, "no data", wrong reason + incomplete flag) and `Write_goldcheck`, gated by `AILAB_EVAL_GOLDCHECK`, writing `eval-runs/meai/goldcheck-week5.md`. The first real C# pass of 3.1's `Answer`/`Citations` over the 360 stored runs: no `FormatException`.

> Result (2026-11-23): all four cells 90 runs, 48 checked, 42 not checked. Correct / checked, all runs: HH 48/48, FH 39/48, HL 25/48, FL 23/48. Run 1: HH 16/16, FH 13/16, HL 9/16, FL 7/16 (rule bar 2: ≥ 14/16). Answer rows run 1: 11, 8, 5, 4 of 11; abstain rows all runs: 12, 12, 6, 8 of 12; E5 gated 3/3 everywhere. Counts unchanged after 3.3's match fix.

### Task 3.3 — Does the gold check agree with your week 5 verdicts? ✅
- **Open:** `eval/results/2026-11-05-matrix-hand-scores.md` (your hand verdicts), `eval-runs/meai/goldcheck-week5.md`.
- **Steps:**
  1. The rows in both: gold-checkable **and** hand-scored in week 5. Answer rows, run 1: A3, F5, G2, A15, B7, B6, G1 (7 rows × 4 cells = 28 comparisons; F1, F4, N4, N7 were never hand-scored). Abstain rows, all 3 runs: D2, D9, INV, N9 (4 × 3 × 4 = 48). E5 is left out: its week 5 "3/3" was filled in from the stored gate, not judged by hand.
  2. For each, compare the code's verdict with yours.
  3. For each disagreement: who's wrong, and why (a missing alternative in `match`? a missing abstention phrase? a support failure code can't see? a wrong hand label?).
  4. Fix the gold check or the match list if it's the code (back to 2.1/2.2, with a test for that case). Write it down if it's the label or a support failure.
- **Check:** Claude re-runs Task 3.2 after any fix.
- **Done when:** an agreement note: 76 comparisons, agreements, each disagreement with its cause. Goes into `experiments.md` §24 (Block 8).
- **Why:** you didn't trust the LLM judge until it was checked against your labels (0.63 / 0.71 in week 5). Same rule for your own checker.

**Done (handed over):** compared `goldcheck-week5.md` with `eval/results/2026-11-05-matrix-hand-scores.md`: answer rows A3, F5, G2, A15, B7, B6, G1 on run 1 (28), abstain rows D2, D9, INV, N9 on all runs (48; per-run hand verdicts from the counts plus the run notes under "Abstain recall"). Then read the stored answer behind every Wrong reason, and scanned all B6/B7/G2/C3 answers for other wordings of the rate-limit and PostgreSQL facts. One code fix (back to 2.1): HL B6 run 1 says "5 attempts **every** 5 minutes" and "PostgreSQL **version** 16", so the code failed it on facts while the hand failed it on citation. Same verdict, wrong reason. Added the alternatives `5 attempts every 5 minutes` (B6, B7) and `PostgreSQL version 16` (B6) in `ask-set-v1.json` and `.md`, plus the test `Facts_in_other_words_reach_the_citation_step` (that answer word for word → Wrong, "no citation holds README.md:38"). The scan found no other variant. 3.2 re-run.

> Result (2026-11-23): **75 of 76 agree.** Answer rows 28/28: the 3 citation cases that facts alone missed (HL A3, FL A3, FL F5 cited a chunk without the gold line) are caught by the citation step, and every code reason matches the hand failure type (retrieval miss / generation → "missing: …", wrong chunk → "no citation holds …"). After the fix HL B6 also agrees on the reason. Abstain rows 47/48: the one disagreement is **FL D2 run 3**, status `abstained` with an unrelated sentence ("An unknown email costs the same as a wrong password…"). Code: Correct (status abstained); hand: wrong. Cause: a failure code can't see. The status rule trusts `/ask`'s abstain, and a sentence that says nothing is not something code can tell from a short abstain. Kept, because a stricter status rule would also fail FL N9 run 1 (a bare `notInDocs`, counted correct by the hand). It overstates FL's abstain count by 1 (8/12 vs hand 7/12); no Haiku run is affected. Week 5's other close call (HL D2 runs 2–3, "not explicitly stated" then invented) agrees, because that phrase is excluded. For `experiments.md` §24 (Block 8).

---

## Block 4: The cost-per-correct report → D2, measured (60 min) — not done

### Task 4.1 — The report, measured table
- **You'll build:** `Summary/CostPerCorrectReport.cs`, a static `Markdown(cells, date)` like `MatrixReport`, and an exact-ID price lookup in `EvalSummary.PriceOf`.
- **Open:** `Summary/MatrixReport.cs` (the cell type, "missing" rows, notes, `Median`, `Percentile`, the private `Long`/`Double` metadata readers), `EvalSummary.PriceOf`, `AiLab.Application/Services/ModelPrices.cs`.
- **Steps:**
  1. `PriceOf`: try `ModelPrices.Cost(model, …)` with the exact ID first (that's how the Bedrock IDs are keyed), then the existing prefix match for dated Anthropic IDs. Still `null` for an unknown model.
  2. Make `MatrixReport`'s `Long` and `Double` `internal` so the new report reuses them (no copies).
  3. One row per execution, columns: runs · checked (gold-checkable runs) · correct · **accuracy** (correct ÷ checked).
  4. `/ask` cost of the checked runs: model, input and output tokens from the `Ask outcome` metadata only (never `chatDetails`: those turns are the judge's), priced with `PriceOf`. Gated runs made no call: $0, not unpriced.
  5. **Cost per correct** = that cost ÷ correct. 0 correct → "n/a". An unpriced model → `?`. llama → "$0 (local)".
  6. Cost per 1,000 answers, latency median / p95 (`MatrixReport.Median` / `Percentile`, over the checked runs that reached the model).
  7. A "missing" row when an execution has no results (as `MatrixReport` does).
- **Check:** `dotnet build`; `dotnet test --filter MatrixReportTests` still green after the `PriceOf` change.
- **Done when:** it builds and the matrix tests pass.

### Task 4.2 — Tests with hand-made runs
- **You'll build:** `CostPerCorrectReportTests.cs`.
- **Open:** `MatrixReportTests.cs` (how it builds `RunRecord`s by hand).
- **Steps:**
  1. 4 checked runs, 3 correct, known tokens on `claude-haiku-5-5` → write the expected cost per correct **as a calculation in a comment**, then assert it.
  2. 0 correct → "n/a" in the row, not "$0".
  3. An unpriced model → `?`.
  4. A run on `us.amazon.nova-lite-v1:0` is priced (the exact-ID lookup), not `?`.
  5. A gated checked run (E5) adds $0 and doesn't make the cell unpriced.
  6. An empty execution → a "missing" row.
  7. `NotChecked` runs aren't in "checked" or "cost".
- **Check:** `dotnet test --filter CostPerCorrectReportTests`.
- **Done when:** green.

### Task 4.3 — A projected section (code only, filled in Block 7)
- **Steps:**
  1. A second table under its own heading, "Projected (not measured)": model · price in / out per 1M · average `/ask` tokens in / out · source execution · projected cost per answer and per 1,000 · correctness "pending".
  2. The average tokens come from a measured execution you pass in (e.g. HH); the prices from `ModelPrices` by exact ID.
  3. A note under the table: the tokens are the source execution's; a model with another tokenizer (Nova Lite) will count differently.
  4. A test: projected rows never appear in the measured table, and their correctness reads "pending".
- **Check:** `dotnet test --filter CostPerCorrectReportTests`.
- **Done when:** green.

### Task 4.4 — Generate D2's measured table
- **Steps:**
  1. A writer test gated by `AILAB_EVAL_COST=<execution>,<execution>,...` (like `AILAB_EVAL_MATRIX`) that builds the report for the listed executions and writes `docs/evals/cost-per-correct-2026-11-19.md` (date `2026-11-19`).
  2. Run it for the four week 5 cells; read the file.
  3. Sanity check: HH's cost per 1,000 answers should be close to week 5's $0.44 (`eval-runs/meai/matrix-2026-11-06.md`; that figure was over all 87 runs that reached the model, this one over the checked runs, so small differences are expected).
- **Check:** `AILAB_EVAL_COST=matrix-heading-haiku,matrix-fixed-haiku,matrix-heading-llama,matrix-fixed-llama dotnet test AiLab.Evals --filter <your writer test>`.
- **Done when:** the file exists with four measured rows; HH's per-answer cost is within a few cents per 1,000 of week 5's. ✅ **D2 measured**

---

## Block 5: Bedrock as the answerer, no Anthropic judge → D1 (75 min) — not done

### Task 5.1 — `AILAB_EVAL_ANSWERS=bedrock` and `AILAB_EVAL_MODEL`
- **Open:** `Hosting/EvalSettings.cs`, `EvalSettingsTests.cs`.
- **Steps:**
  1. Accept `bedrock` in the answers check.
  2. Add `Model` to the record (a named optional parameter at the end), read from `AILAB_EVAL_MODEL`: required for `bedrock`, refused for the others, and only the two IDs from D1 allowed.
  3. `DefaultExecutionName`: a `none` judge gets its own names, before the baseline case: `bedrock-nova-lite` / `bedrock-haiku-5-5` (plus `-<chunker>` when set) for Bedrock, `dev-llama-judge-none` (plus `-<chunker>`) for llama. Two models never share results or cache entries, and a no-judge run never lands in `baseline-…` or `matrix-…`.
  4. Update the header comment that lists the variables.
  5. Tests: bedrock + valid model; bedrock without model; model without bedrock; unknown model; the two execution names differ and neither starts with `baseline-`.
- **Check:** `dotnet test --filter EvalSettingsTests`.
- **Done when:** green.

### Task 5.2 — `AILAB_EVAL_JUDGE=none`
- **Open:** `EvalSettings.cs`, `EvalFixture.cs` (`CreateJudge`, the `Judge` property), `EvalReporting.Create`, `AskSetEvalTests.cs` (`evaluators`, line 51 `EvalReporting.Create(…, fixture.Judge, …)`, line 61 `run.ChatConfiguration!`, `JudgedMetrics`, `SetupProblems`).
- **Steps:**
  1. Accept `none` in the judge check. Rules: `bedrock` answers need `none` (today's runs have no Anthropic judge, and the Bedrock client takes the configuration's place, step 4); `haiku` answers can't use `none` (they go through the judge's client).
  2. With `none`: leave out `GroundednessEvaluator`, `RelevanceEvaluator` **and** `RetrievalEvaluator` (it's AI-based too); keep Recall@k, Ask outcome and Gold check.
  3. `SetupProblems` expects the judged metrics only when there's a judge.
  4. The run's chat client, one rule in one place (`EvalReporting.ConfigurationClient(settings, judge, bedrock)`, where `judge` is a `Func<IChatClient>` so it's only called when needed): judge `none` + `bedrock` → the Bedrock client (cached, see 5.3); judge `none` + `llama` → `null`; otherwise → the judge. `EvalReporting.Create` takes that `IChatClient?` and passes `null` as no `ChatConfiguration` (`DiskBasedReportingConfiguration.Create` accepts it when no evaluator is AI-based; nothing is cached then).
  5. `Ask_set_v1` never touches `fixture.Judge` with `none` (it would run `CreateJudge`, whose non-llama branch reads `Anthropic:ApiKey`), and picks `fixture.Chat.Target` without `!`: the configuration's client for `haiku` and `bedrock`, `fixture.Ollama` for `llama`.
  6. Tests in `EvalSettingsTests` for `none` with each answers value (llama ok, bedrock ok, haiku refused), and for `ConfigurationClient`'s three cases with a judge factory that throws if called.
- **Check:** `dotnet test --filter EvalSettingsTests`; a run with `none` never reads `Anthropic:ApiKey` (Claude checks the code path).
- **Done when:** green.

### Task 5.3 — The fixture creates the Bedrock client
- **Open:** `EvalFixture.cs` (`InitializeAsync`, `_ollama`), `AiLab.Infrastructure/Providers/ChatClientFactory.cs` and `BedrockClientOptions.cs` (Wednesday), `AiLab.Infrastructure/DependencyInjection.cs` (`BedrockOptions`, line 85: private, read it as the model).
- **Steps:**
  1. When answers are `bedrock`: build a `BedrockClientOptions` from the host's `IConfiguration` (`Bedrock:Region`, `Bedrock:ServiceUrl`, `Bedrock:EmulatorCaPath`: the same three keys `BedrockOptions` reads; no resilience values, as in the Rag host) and create the client with `ChatClientFactory.Create("bedrock", settings.Model, bedrock: options)`. Keep it in a `_bedrock` field, exposed like `Ollama`, disposed in `DisposeAsync`.
  2. Pass it to `EvalReporting.ConfigurationClient` (5.2), so `fixture.Chat.Target` becomes `run.ChatConfiguration.ChatClient`: Bedrock answers are cached like Haiku's (re-scoring is free, latency ~0 ms on a hit). The run whose latency counts uses a new `AILAB_EVAL_CACHE_KEY`.
- **Check:** `dotnet build`; `dotnet test` offline still green.
- **Done when:** green.

### Task 5.4 — A test that proves the wiring offline
- **Steps:** one test (no host, no network) that builds settings `bedrock` + Nova Lite + judge `none`, creates the Bedrock client the way the fixture does (fake region, no call made), passes it through `EvalReporting.ConfigurationClient`, and asserts the result is that client, whose `ChatClientMetadata` says `aws.bedrock` and `us.amazon.nova-lite-v1:0`, and that the judge factory was never called.
- **Check:** `dotnet test --filter <your test>`.
- **Done when:** green.

---

## Block 6: Smoke run on the emulator → D1 wiring (20 min) — not done

### Task 6.1 — One row, one run, each model
- **Steps:**
  1. Safety check → `SAFE`.
  2. Nova Lite:
     ```bash
     source ~/.aws/emulator/ai-lab-kb.env
     AILAB_EVAL=1 AILAB_EVAL_ANSWERS=bedrock AILAB_EVAL_MODEL=us.amazon.nova-lite-v1:0 AILAB_EVAL_JUDGE=none AILAB_EVAL_RUNS=1 \
     Bedrock__ServiceUrl=https://127.0.0.1:9297 Bedrock__EmulatorCaPath=$HOME/.aws/emulator/9297/ca.pem \
     dotnet test AiLab.Evals --filter "FullyQualifiedName~Ask_set_v1&DisplayName~A3"
     ```
  3. The same with `AILAB_EVAL_MODEL=us.anthropic.claude-haiku-5-5`.
  4. Confirm in the eval host's log (`AiLab.Evals/bin/Debug/net10.0/logs/rag-<date>.log`) that the calls went to the emulator, and that no Anthropic call was made.
  5. Run the Block 4 report on these two executions: `AILAB_EVAL_COST=bedrock-nova-lite,bedrock-haiku-5-5 dotnet test AiLab.Evals --filter <your writer test>` (it overwrites the D2 file; regenerate it with all six executions in Block 7).
- **Expected:** Nova Lite → `/ask` `502 reply_unreadable` (the emulator's `{}` tool input), reported by the fixture as a setup error. Haiku 5.5 → `ValidationException` (not in the emulator's catalog) → `502 provider_unavailable`. Neither run reaches `EvaluateAsync`, so no result is stored.
- **Done when:** both results recorded, and the report shows these executions as **missing**, not "0 correct". If it shows 0%, fix the report (Block 4) first.

---

## Block 7: Projected Bedrock rows + Artificial Analysis → D2 (40 min) — not done

### Task 7.1 — Projected rows
- **Steps:**
  1. Feed Task 4.3's projected section: Nova Lite and Haiku 5.5 on Bedrock, with HH's average `/ask` tokens (same prompt, same chunks) and the prices in `ModelPrices` (exact IDs).
  2. Regenerate `docs/evals/cost-per-correct-2026-11-19.md` with the four week 5 cells and the two Bedrock executions (shown as missing).
- **Check:** projected cost per answer for Haiku 5.5 (`us.`) = HH's × 1.1 (Bedrock's US-profile price is exactly 10% above the Anthropic API's); Nova Lite's row carries the tokenizer note.
- **Done when:** the file has both tables, clearly separated.

### Task 7.2 — Artificial Analysis as a sanity check
- **Steps:**
  1. On artificialanalysis.ai, find Nova Lite and Claude Haiku 5.5 (and llama3.2 if listed): the quality index, the listed price, the speed. Note the page and the date; a model that isn't listed reads "not listed".
  2. Add a short table under the report: model · their quality index · their price · your accuracy (measured or "pending").
  3. One sentence: does their ranking agree with yours (week 5: Haiku ≫ llama3.2)? If not, which one you trust for *this* app, and why.
- **Done when:** the table and sentence are in the file, with the source and date. ✅ **D2**

---

## Block 8: Real-account runbook + housekeeping (40 min) — not done

### Task 8.1 — Runbook: "Run the eval suite on Bedrock"
- **Where:** `docs/evals/README.md`, a new section.
- **Steps:** write, in order:
  1. Fresh terminal; the real-account safety check (as `ai-lab-dev`, no emulator variables: `unset AWS_ENDPOINT_URL AWS_CA_BUNDLE`).
  2. Per model: `AILAB_EVAL=1 AILAB_EVAL_ANSWERS=bedrock AILAB_EVAL_MODEL=<id> AILAB_EVAL_JUDGE=none AILAB_EVAL_RUNS=3 AILAB_EVAL_CACHE_KEY=<new> dotnet test AiLab.Evals --filter Ask_set_v1`.
  3. Hand-score the 12 `judge` answerable rows (run 1), as in week 5, into a new hand-scores file under `eval/results/`.
  4. Regenerate the report with `AILAB_EVAL_COST=` (four week 5 cells + `bedrock-nova-lite,bedrock-haiku-5-5`); the projected rows become measured; apply Task 1.4's rule.
  5. Expected cost, worked out from Task 7.1 (2 models × 90 answers), written before running.
- **Done when:** the section exists and every command in it was run once on the emulator (with the emulator variables) or marked "real account only".

### Task 8.2 — Record
- `experiments.md` section 24, "Two Bedrock models with our own evals": the rule (1.4), the gold check (match lists written after week 5's answers, facts + citation, abstain by status or phrase) and its agreement with your labels (3.3), week 5 measured cost per correct (4.4), the smoke results (6.1), the projected rows and Artificial Analysis (7.x), what waits for the real account.
- `CLAUDE.md`: Week 7 Thursday entry (`AILAB_EVAL_ANSWERS=bedrock`, `AILAB_EVAL_MODEL`, `AILAB_EVAL_JUDGE=none`, `EvalReporting.ConfigurationClient`, `AskSetRow.Match`, `GoldCheck`/`GoldCheckEvaluator`/`AskSetRowContext`, `RunRecord.Answer`/`Citations`, `PriceOf` exact-ID lookup, `CostPerCorrectReport`, `AILAB_EVAL_GOLDCHECK`, `AILAB_EVAL_COST`).
- `README.md`: one line in the evals section.
- **Done when:** the three are written; Claude checks the numbers against the generated files.

### Task 8.3 — Ship
- **Steps:** `dotnet test` (green, count = start + new tests) → commit → open a PR.
- **Done when:** PR opened.

---

## End of day
- [ ] D1 (ready + smoke) and D2 (measured + projected) checked
- [ ] `docker/bedrock-kb/bedrock-kb-emulator.sh stop`
- [ ] Ask Claude for today's recap PDF (for OneDrive)

> **Short on time?** Blocks 2 → 3 → 4 are the day's real result: a checker you've verified and a cost-per-correct table from real runs. Then 5, then the rest.
