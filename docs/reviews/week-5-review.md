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

### Next (Tuesday)

Turn the set into JSON, run it from `dotnet test` with the MEAI evaluators, and record the first baseline. Plan: [2026-11-03-week-5-meai-evaluation.md](../plans/2026-11-03-week-5-meai-evaluation.md).
