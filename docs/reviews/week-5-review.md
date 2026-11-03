# Week 5 review: evals

- **Week:** 2026-11-02 to 2026-11-07 (roadmap week 5)
- **Focus:** why evals matter, building a test set, then measuring `/ask` against it

## Monday 2 Nov: why evals matter, and the test set

### Done

- [x] Read Hamel Husain, *Your AI Product Needs Evals*
- [x] Read Eugene Yan, *Task-Specific LLM Evals that Do & Don't Work*
- [x] Test set: 30 questions with gold answers and sources → [ask-set-v1.md](../evals/ask-set-v1.md)
- [x] Eval plan: metrics, scoring and thresholds → [eval-plan.md](../evals/eval-plan.md)

### What the readings changed

- **Tests and evals are different.** `AiLab.Rag.Tests` checks the code with a fake model. An eval checks the real model. `/ask` already has automatic checks (`CitationCheck`, the gate, the repair call, the typed parse), but they've never been run on a fixed question set with a real model.
- **Week 4's failure log was already an eval**, done by hand. Its main finding came from reading the runs: `/ask` fails at generation, not at retrieval. Hamel says to look at the data like this before picking metrics.
- **The worst failures look like good answers.** An invented claim with a real citation (M1), a right fact cited to the wrong chunk (M2), a missing list item (M3), general knowledge instead of "not in the docs" (M4): all pass every automatic check. Only a human or a judge model catches them, and the judge must be checked against my labels first.
- **`/ask` is several tasks.** Deciding whether to answer is a yes/no classification. Exact names and numbers are extraction. Staying faithful to the sources is the "factual consistency" check from summarization. Translation, copyright and toxicity checks don't matter for these docs.
- **The gate can't judge quality.** Answerable and not-answerable questions have overlapping best distances (0.26–0.48 vs 0.36–0.45). So no `MaxDistance` value separates them. The gate only saves the cost of clearly unrelated questions, like E5 (0.59).
- **Metrics to avoid:** word-overlap scores (ROUGE, BLEU, BERTScore), 1–10 grades from a model, and a single accuracy number for abstaining.

### Decisions

- Gold answers point to a file and line, not a chunk, so the set still works after a chunking change.
- Haiku 5.5 is the model that's scored. llama3.2 is reported only.
- 3 runs per question. A question passes when 2 of the 3 runs pass.
- The thresholds are provisional until the first baseline run.

### Open

- Carried from week 4: the conclusions of sections 12–14 in my own words, and the default chat provider for `/ask`.
- (Resolved: the week 4 recap now points to the 30-question set instead of a "78-question set" that didn't exist.)

## Tuesday 3 Nov: automated scoring in `dotnet test`

Plan: [2026-11-03-week-5-meai-evaluation.md](../plans/2026-11-03-week-5-meai-evaluation.md). How it works: [meai-evals.md](../evals/meai-evals.md).

### Done

- [x] The test set as data: `docs/evals/ask-set-v1.json`, with offline checks against the `.md` and the corpus. They found three wrong gold lines in `decisions.md` (N3, N5, N6, each off by 90), now fixed.
- [x] `AiLab.Evals`: the 30 questions through the real `/ask`, scored by Groundedness, Relevance, Retrieval (MEAI, Haiku as judge) and Recall@5 (no model). Off unless `AILAB_EVAL` is set; plain `dotnet test` stays offline.
- [x] Response caching under `eval-runs/meai/` (git-ignored), with Haiku's answers in the same cache as the judges' calls.
- [x] Baseline scores recorded: `experiments.md` section 15, summary for newcomers in [baseline-2026-11-03.md](../evals/baseline-2026-11-03.md)
- [x] The judge checked against the week 4 hand labels: precision 0.63, recall 0.71

### Learned

- **A custom evaluator is just code.** `IEvaluator` returns metrics. Recall@5 needs no model: compare file and line ranges with the gold lines.
- **Each judge needs its own context.** Without it, Groundedness and Retrieval make no call and report a diagnostic. A forgotten context shows up as an error, not as a quiet low score.
- **The iteration name is part of the cache key**, so 3 runs are 3 real answers, and a re-run of the same execution is free.
- **What isn't cached changes what is.** llama3.2's answers bypass the cache, so the judges that read the answer miss it on a re-run.
- **llama3.2 can't judge.** It answered "Score" with no number. The judge has to be a stronger model.
- **A cached re-run is the cheap way to re-score.** The Haiku baseline: 6 min 37 s and $0.19 the first time, 3 s and $0 the second, same scores.
- **Retrieval isn't the weak point on this corpus.** Every English question's gold chunk was among the 5 sent (MRR 0.77). Only the French question missed.
- **Judge scores are signals, not verdicts.** Groundedness separates Haiku (4.93) from llama3.2 (4.15) and catches invented numbers. It misses general-knowledge answers, and it lowers scores for missing details. Relevance gives 3 to correct "not documented" answers.
- **A judge must earn trust.** Against the week 4 labels it reached 0.63 precision and 0.71 recall, below the 0.8 bar. Faithfulness stays scored by hand.
- **`/ask`'s status isn't the whole story.** Haiku said "not in the docs" on all 12 abstain runs, but the status was `abstained` on only 3: it cites what the chunks do say.
- **Set timeouts on judges too.** One stuck call held a run for 35 minutes under the SDK's 10-minute default.

### Next (Wednesday)

Score layer 3 pass/fail against the gold answers (correctness, exact match, list items). Try a per-claim faithfulness judge and check it against the same labels. Then review the provisional thresholds.
