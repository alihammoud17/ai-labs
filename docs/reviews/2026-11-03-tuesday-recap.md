# Tuesday 3 Nov recap: automated quality scoring in `dotnet test`

- **Roadmap:** week 5 (evals), day 2
- **Plan:** `docs/plans/2026-11-03-week-5-meai-evaluation.md` (steps 5.0–5.12, all done)
- **Branch:** `stage-5-meai-evals`, rebase-merged into `main` (PR #7) and deleted; follow-ups on `main` up to `24a460b`
- **Full results:** `experiments.md` section 15 and `docs/evals/baseline-2026-11-03.md`

## The day in one paragraph

On Monday, `/ask` had a 30-question test set but no way to score it except by reading every answer. By Tuesday evening, one command runs all 30 questions three times through the real `/ask` and scores every answer. Microsoft's evaluation library handles the scoring, with Haiku as the judge, and a small evaluator of our own checks retrieval. Every model call is cached. The first baseline cost $0.19 and took under 7 minutes; running it again cost nothing and took 3 seconds. The baseline confirmed week 4's finding that retrieval is not the weak point. It also showed that the judge's scores are useful signals but can't yet be trusted as verdicts.

## Deliverables

- [x] **xUnit eval project with groundedness, relevance and retrieval evaluators:** `AiLab.Evals`
- [x] **Response caching enabled:** disk cache in `eval-runs/meai/` (git-ignored)
- [x] **Baseline scores recorded:** `experiments.md` section 15

## What was built

| Piece | What it does |
|---|---|
| `docs/evals/ask-set-v1.json` | The 30 questions as data, read by the tests. Offline checks compare it with the `.md` and the corpus. |
| `AiLab.Evals` (xUnit v3) | Runs each question through the real `/ask` (Postgres, embeddings, prompt). Off unless `AILAB_EVAL=1`, so plain `dotnet test` stays offline. |
| Groundedness, Relevance, Retrieval | Microsoft's evaluators (MEAI 10.11.0), scored 1–5 by Haiku 5.5. Below 4 counts as failed. |
| `RecallAtKEvaluator` | Our own evaluator, no model: was a chunk holding the gold line among the 5 sent? Also gives the rank for MRR. |
| `AskOutcomeEvaluator`, `AnsweredOnlyEvaluator` | Record `/ask`'s status, gate, tokens and times; skip the judges when there's no answer to judge. |
| `HaikuJudgeOptionsClient` | Removes the settings Haiku refuses (temperature, top-p, penalties) and gives the judges at least 2000 output tokens. |
| `EvalSummary` | Writes per-run, per-question and per-category tables, with tokens and cost, from the stored results. |
| `JudgeAgreementTests` | Runs Groundedness on the 40 hand-labelled week 4 answers and compares it with my labels. |

## Baseline numbers

| | Haiku answers | llama3.2 answers |
|---|---|---|
| Groundedness (avg) | **4.93**, no run below 4 | **4.15**, 10 runs below 4 |
| Relevance (avg) | 4.19 | 3.72 |
| Retrieval (avg) | 3.83 | 3.83 (same chunks) |
| Recall@5 | 24 of 24 English questions (French missed) | same |
| MRR (English) | 0.77 | same |
| Cost, time | $0.19, 6 min 37 s | $0.06, 45 min (one stuck call) |
| Same run from the cache | $0.00, 3 s, identical scores | |

Against the eval plan's provisional thresholds:
- **Retrieval:** passes, with every English answer sent.
- **Abstaining:** all four checks hold. Haiku said "not in the docs" on 12 of 12 runs of the abstain questions. The gate stopped E5 on all 3 runs.
- **Answers:** not scored pass/fail yet. The judge scores are recorded only.

## What I learned

1. **A custom evaluator is just code.** `IEvaluator` returns metrics. Recall@5 needs no model, only file names and line ranges compared with the gold lines.
2. **Each judge needs its context.** Groundedness needs the chunks sent, Retrieval the chunks retrieved. Without them, the evaluator makes no call and reports a diagnostic instead of a low score.
3. **The cache key includes the iteration name.** So 3 runs give 3 real answers, and re-running the same execution is free. Only calls that go through the scenario's chat client are cached. llama3.2's answers bypass it, so on a re-run the judges that read those answers miss the cache too.
4. **A small model can't judge.** llama3.2 replied "Score" with no number. Microsoft's docs say the judge prompts are tuned for GPT-4o-class models.
5. **Judge scores are signals, not verdicts.**
   - Groundedness separates the two models and caught llama3.2's invented numbers: "60 minutes" (D1), "5 attempts per 5 minutes" for password reset (G2).
   - It missed general-knowledge answers: llama3.2's definition of multi-tenancy scored 4.
   - It also marks answers down for missing details, not only for unsupported ones.
   - Relevance gives 3 to correct "the docs don't say" answers, so it can't decide pass or fail on partial or abstain questions.
6. **A judge has to earn trust.** Against my week 4 labels it reached 0.63 precision and 0.71 recall, below the 0.8 bar, so faithfulness stays scored by hand. It can't see a right fact cited to the wrong chunk (M2), because it gets all the chunks as one context.
7. **`/ask`'s status isn't the whole story.** Haiku said "not in the docs" on all 12 abstain runs, but only 3 came back with status `abstained`. The rest are `grounded`, because they cite what the docs do say on the topic.
8. **Judges need timeouts too.** One Haiku call hung for about 35 minutes under the SDK's 10-minute default. The judge now has a 60-second timeout per attempt.

## Fixed along the way

- **Three wrong gold lines** in the test set, all in `decisions.md`, each off by 90: N3 111 → 21, N5 125 → 35, N6 137 → 47. The offline checks on the JSON found them.
- **A stuck judge call:** 60 s timeout per attempt, 2 retries.
- **Postgres down on the first dry run:** the fixture's probe stops the run with one clear message instead of 30 failures.

## Known limits

- **Self-grading:** Haiku judges Haiku's own answers.
- **Abstained runs aren't judged,** including llama3.2's mixed answers that answer part and say the rest isn't covered (B6, C3, N7).
- **Library upgrades change the cache keys,** so the next run after an upgrade pays for its calls again.

## Run it

```bash
# free wiring check (llama3.2 answers and judges, one question)
AILAB_EVAL=1 AILAB_EVAL_ANSWERS=llama AILAB_EVAL_JUDGE=llama AILAB_EVAL_RUNS=1 dotnet test AiLab.Evals --filter 'DisplayName~A3'

# the Haiku baseline (~$0.19 the first time, free from the cache)
AILAB_EVAL=1 dotnet test AiLab.Evals --filter AskSetEvalTests

# summary and HTML report
AILAB_EVAL_SUMMARY=baseline-2026-11-03-haiku dotnet test AiLab.Evals --filter Write_summary
dotnet aieval report --path eval-runs/meai --output eval-runs/meai/report.html
```

## Next (Wednesday)

- Score layer 3 pass/fail against the gold answers: correctness, exact match, every list item.
- Try a narrower faithfulness judge (one claim, one chunk, supported or not) and check it against the same hand labels.
- Review the provisional thresholds against the baseline, then freeze them.

## Housekeeping

- The free wiring check (`AILAB_EVAL_JUDGE=llama`) now passes: llama3.2's unreadable judge scores are warnings, not failures (`24a460b`).
- Still carried from week 4: the conclusions of sections 12–14 in my own words, and the default chat provider for `/ask`.
