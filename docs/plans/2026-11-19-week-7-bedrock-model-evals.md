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
| 0. Setup | 0.1 | not done |
| 1. Understand the suite, write the rule | 1.1–1.4 | not done |
| 2. The gold check: "correct" without a judge | 2.1–2.5 | not done |
| 3. Answers from stored results + agreement with week 5 | 3.1–3.3 | not done |
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

## Block 0: Setup (10 min) — not done

### Task 0.1 — Branch and a green start
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

---

## Block 1: Understand the suite, write the rule (30 min) — not done

### Task 1.1 — How one eval run works
- **You'll build:** 5 lines in your notes.
- **Open:** `AiLab.Evals/AskSetEvalTests.cs` (`Ask_set_v1`), `Hosting/EvalFixture.cs`, `Judging/EvalReporting.cs`.
- **Steps:** answer in one line each:
  1. Who answers `/ask`? (Follow `fixture.Chat.Target`.)
  2. Who judges, and how is the judge created? (`EvalFixture.CreateJudge`, and the line in `Ask_set_v1` that first touches `fixture.Judge`.)
  3. Which evaluators run, and which ones need a model?
  4. What is cached, and what's in the cache key? (The comment above `EvalReporting.Create`.)
  5. Where does one run's result land on disk, and what's inside? (Open one `.json` under `eval-runs/meai/results/matrix-heading-llama/`: find `modelResponse`, the `Ask outcome` metadata with `model`, tokens and `citations`, and notice whose call the `chatDetails` turns are.)
- **Done when:** five lines written. Claude checks them.
- **Why:** Block 5 changes line 1 (who answers) and line 2 (no judge). Block 3 reads what you found in line 5; the `chatDetails` turns are the judge's, so cost must come from the `Ask outcome` metadata.

### Task 1.2 — The tutorial, the parts you'll use
- **Open:** the "Evaluate with caching and reporting" tutorial.
- **Steps:**
  1. Read the sections on `ReportingConfiguration`, scenario runs and iterations, and response caching.
  2. Read how `dotnet aieval report` builds the HTML report.
  3. Write down: what is a scenario, an iteration, an execution; what does a cache hit cost; which calls get cached (only those through `ScenarioRun.ChatConfiguration.ChatClient`).
- **Done when:** three definitions written, each with the ai-labs name it maps to (e.g. scenario = `AskSetEvalTests.<category>.<id>`), plus the caching sentence.

### Task 1.3 — The two definitions, with week 5's numbers
- **Open:** `docs/evals/experiment-matrix-2026-11-05.md`, "The result".
- **Steps:**
  1. Copy the scope rules' definitions of "correct" and "cost per correct answer" into your notes.
  2. HH: 12/12 correct at $0.44 per 1,000 answers. FH: 9/12 at $0.68. Work out cost per correct for each, per 1,000 correct answers.
  3. Compare the gap per answer with the gap per correct answer. One sentence.
  4. One line on the approximation: the correct rate is over the 12 hand-scored rows (run 1), the cost per answer over all 87 runs that reached the model. Block 4 removes it: both come from the same checked runs.
- **Done when:** both numbers and the two sentences written; Claude checks the arithmetic.
- **Why:** it's the whole point of D2: a model that's wrong more often costs more per correct answer than its price suggests.

### Task 1.4 — Write the decision rule, before any run
- **Steps:** under this task, in the plan, write:
  1. The bars a Bedrock model must pass (suggestion: at least 15/16 gold-checked correct on run 1, 10/12 on the hand pass, p95 under 5 s).
  2. The tie-break: the lower cost per correct answer.
  3. What Artificial Analysis would have to show for you to distrust your own result.
- **Done when:** the rule is in the plan and committed **before** Block 6.
- **Why:** week 5's lesson: a rule written first stops you from choosing the rule that favours the model you already like.

---

## Block 2: The gold check, "correct" without a judge (90 min) — not done

### Task 2.1 — Match lists in the ask set
- **You'll build:** a `match` field on the 11 `exact` and `all items` rows of `docs/evals/ask-set-v1.json`, `Match` on `AskSetRow`, the same lists in `ask-set-v1.md`, and a test in `AskSetTests.cs`.
- **Open:** `docs/evals/ask-set-v1.json` (A3, F1, F4, F5, G2, A15, B7, N4, B6, G1, N7), `AskSet/AskSet.cs`, `AskSetTests.cs` (`Exact_and_list_checks_have_items`).
- **Steps:**
  1. `match` is a list of facts; each fact is a list of alternatives, all literal text an answer may contain. One fact per entry of `items`, in the same order. Example, G2: `[["10 per minute", "10 requests per minute"]]`; B7: one fact per limit (`30 per minute` for refresh, `5 attempts per 5 minutes` for MFA, `monthly` for the Redis limiter, …).
  2. `items` stays as it is (the human description). `match` is what code looks for: short fragments, no paraphrase of the whole item.
  3. Add `IReadOnlyList<IReadOnlyList<string>>? Match = null` to `AskSetRow` (a named optional parameter at the end, so no caller breaks).
  4. Add a "Match" line to each of the 11 rows in `ask-set-v1.md` (the file header says: change both together).
  5. A test: every `exact` / `all items` row has `match` with as many facts as `items`, no empty alternative; every other row has none.
- **Check:** `dotnet test --filter AskSetTests`.
- **Done when:** green, and every alternative is something a correct answer could say word for word.
- **Why:** `items` were written for a human reader. B7's "register and login: 10 per minute, shared" never appears in an answer as written, so a plain "contains" fails every B7 run, right or wrong. The lists are written after seeing week 5's answers: say so in §24.

### Task 2.2 — A pure function that judges one answer
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

### Task 2.3 — Unit tests from real rows
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

### Task 2.4 — The evaluator around it
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

### Task 2.5 — Wire it into the suite
- **Open:** `AskSetEvalTests.cs`: the `evaluators` array, the `context` array and `SetupProblems`.
- **Steps:**
  1. Add `new GoldCheckEvaluator()` to `evaluators`.
  2. Add `new AskSetRowContext(row)` to `context`.
  3. Add "Gold check" to the metrics `SetupProblems` expects.
- **Check:** `dotnet test` (whole solution, offline: the eval tests still skip without `AILAB_EVAL`).
- **Done when:** green, test count = Task 0.1's count + the new tests.

---

## Block 3: Answers from stored results + agreement with week 5 (60 min) — not done

### Task 3.1 — Read the answer text and the citations into `RunRecord`
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

### Task 3.2 — Gold-check the four week 5 cells
- **You'll build:** a small summary (a method + a writer test gated by `AILAB_EVAL_GOLDCHECK=<execution>,<execution>,...`, like `MatrixReportTests.Write_matrix` and `AILAB_EVAL_MATRIX`) that loads the listed executions and runs `GoldCheck` on every run.
- **Steps:**
  1. For each run: find its `AskSetRow` by `Id`, call `GoldCheck` with `Answer`, `Status`, the `gate` from `Outcome` and `Citations`.
  2. Per cell: checked runs, correct, wrong, not checked. Run 1 only and all 3 runs.
  3. Write it to `eval-runs/meai/goldcheck-week5.md` (git-ignored, a working file), with each Wrong run's reason.
- **Check:** `AILAB_EVAL_GOLDCHECK=matrix-heading-haiku,matrix-fixed-haiku,matrix-heading-llama,matrix-fixed-llama dotnet test AiLab.Evals --filter <your test>`; the counts per cell add up to 90 runs (30 × 3); 48 checked runs per cell (16 rows × 3).
- **Done when:** the file exists with four rows.

### Task 3.3 — Does the gold check agree with your week 5 verdicts?
- **Open:** `eval/results/2026-11-05-matrix-hand-scores.md` (your hand verdicts), `eval-runs/meai/goldcheck-week5.md`.
- **Steps:**
  1. The rows in both: gold-checkable **and** hand-scored in week 5. Answer rows, run 1: A3, F5, G2, A15, B7, B6, G1 (7 rows × 4 cells = 28 comparisons; F1, F4, N4, N7 were never hand-scored). Abstain rows, all 3 runs: D2, D9, INV, N9 (4 × 3 × 4 = 48). E5 is left out: its week 5 "3/3" was filled in from the stored gate, not judged by hand.
  2. For each, compare the code's verdict with yours.
  3. For each disagreement: who's wrong, and why (a missing alternative in `match`? a missing abstention phrase? a support failure code can't see? a wrong hand label?).
  4. Fix the gold check or the match list if it's the code (back to 2.1/2.2, with a test for that case). Write it down if it's the label or a support failure.
- **Check:** Claude re-runs Task 3.2 after any fix.
- **Done when:** an agreement note: 76 comparisons, agreements, each disagreement with its cause. Goes into `experiments.md` §24 (Block 8).
- **Why:** you didn't trust the LLM judge until it was checked against your labels (0.63 / 0.71 in week 5). Same rule for your own checker.

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
