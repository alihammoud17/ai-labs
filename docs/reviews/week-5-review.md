# Week 5 review: evals

- **Week:** 2026-11-02 to 2026-11-07 (roadmap week 5)
- **Focus:** why evals matter, building a test set, then measuring `/ask` against it

## Monday 2 Nov: why evals matter, and the test set

### Done

- [x] Read Hamel Husain, *Your AI Product Needs Evals*
- [x] Read Eugene Yan, *Task-Specific LLM Evals that Do & Don't Work*
- [x] Test set: 30 questions with gold answers and source documents → [docs/evals/ask-set-v1.md](../evals/ask-set-v1.md)
- [x] Eval plan: metrics, scoring and thresholds → [docs/evals/eval-plan.md](../evals/eval-plan.md)

### What the readings changed

- **Tests and evals are different things.** `AiLab.Rag.Tests` proves the code handles a reply correctly, using a scripted fake model. An eval measures what the real model does. `/ask` already has good level 1 assertions (`CitationCheck`, the gate, the repair call, the typed parse). They've just never been run on a fixed question set against a real model.
- **Week 4's failure log was already an eval**, a manual level 2 one: traces read and labelled by hand. Its main finding, that generation fails rather than retrieval, came from looking at the data, which is the step Hamel puts before any metric.
- **The hardest failures pass every assertion.** Invented claims, wrong citations, left-out items and general knowledge instead of abstaining (M1–M4) all look like normal grounded answers. Catching them needs a human or a judge model, and the judge has to be checked against human labels first.
- **`/ask` isn't one task.** In Eugene Yan's terms, deciding whether to answer is classification, exact facts are extraction, and faithfulness is the factual-consistency check from summarization. Translation, copyright and toxicity don't apply to this corpus.
- **Separation of distributions explains the gate.** Answerable and not-answerable questions have overlapping best distances (0.26–0.48 vs 0.36–0.45), so no `MaxDistance` value can be a quality filter. The gate saves cost on clearly unrelated questions; it doesn't decide quality.
- **Metrics to avoid:** word-overlap scores (ROUGE, BLEU, BERTScore) against gold answers, 1–10 LLM grades, and accuracy on its own for abstaining.

### Decisions

- Gold answers are labelled by file and answer line, not by chunk key, so the set survives chunking changes.
- Haiku 5.5 is the model the thresholds apply to. llama3.2 is reported but not gated.
- 3 runs per question; a question passes when 2 of 3 runs pass.
- The thresholds are provisional until the first baseline run.

### Open

- The week 4 recap mentions a 78-question set in `eval/ask-v1.md` that doesn't exist. [ask-set-v1.md](../evals/ask-set-v1.md) replaces it, and the recap needs correcting. **Resolved:** the recap now points to [docs/evals/ask-set-v1.md](../evals/ask-set-v1.md).
- Still carried from week 4: the conclusions of sections 12–14 in my own words, and the default chat provider for `/ask`.

### Next (Tuesday)

Turn the set into JSON, build the retrieval runner (layer 1, no model), and get the first baseline numbers.
