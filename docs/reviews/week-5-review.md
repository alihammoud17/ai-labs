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

### Done so far

- [x] The test set as data: `docs/evals/ask-set-v1.json`, with offline checks against the `.md` and the corpus. They found three wrong gold lines in `decisions.md` (N3, N5, N6, each off by 90), now fixed.
- [x] `AiLab.Evals`: the 30 questions through the real `/ask`, scored by Groundedness, Relevance, Retrieval (MEAI, Haiku as judge) and Recall@5 (no model). Off unless `AILAB_EVAL` is set; plain `dotnet test` stays offline.
- [x] Response caching under `eval-runs/meai/` (git-ignored), with Haiku's answers in the same cache as the judges' calls.
- [ ] Baseline scores recorded

### Learned so far

- **A custom evaluator is just code.** `IEvaluator` returns metrics. Recall@5 needs no model: compare file and line ranges with the gold lines.
- **Each judge needs its own context.** Without it, Groundedness and Retrieval make no call and report a diagnostic. A forgotten context shows up as an error, not as a quiet low score.
- **The iteration name is part of the cache key**, so 3 runs are 3 real answers, and a re-run of the same execution is free.
- **What isn't cached changes what is.** llama3.2's answers bypass the cache, so the judges that read the answer miss it on a re-run.
- **llama3.2 can't judge.** It answered "Score" with no number. The judge has to be a stronger model.
