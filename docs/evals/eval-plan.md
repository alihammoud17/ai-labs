# /ask eval plan

How `POST /ask` is measured from week 5 on. The input is the 30 questions in [ask-set-v1.md](ask-set-v1.md). New to the words? Start with [README.md](README.md).

## Setup

- **Settings as on `main`:** heading chunker, top 5, `MaxDistance` 0.50, `prompts/ask-v1.md`, `MaxOutputTokens` 2000, `nomic-embed-text` without prefixes.
- **Haiku 5.5 is the model that's scored.** llama3.2 is run and reported, but it's only the development model, so it doesn't pass or fail anything.
- **3 runs per question.** One run is an anecdote; three give a rate. A question passes when **2 of its 3 runs pass**.
- **Every run records** the commit, settings, model, tokens, cost and time.

## Layer 1: retrieval (no chat model)

Checked with `GET /search?top=10&chunker=heading` on the 25 answerable rows. A **hit** is a chunk from the gold file whose line range contains a gold line.

| Metric | What it answers | How it's computed | Threshold |
|---|---|---|---|
| Doc hit@5 | Was the right file among the 5 chunks sent? | rows with a gold file in the top 5 | ≥ 23 of 24 English rows |
| Recall@5 | Was the answering chunk among the 5 sent? | rows with a hit in the top 5 | ≥ 22 of 24 English rows |
| Recall@10, two sources | For questions that need two chunks, were both found? | the 5 "both needed" rows with every gold source in the top 10 | ≥ 4 of 5 |
| Hit@1, hit@3 | Is the answering chunk first, or in the top 3? | rows whose first hit is at rank ≤ 1 / ≤ 3 | reported only |
| MRR | How high does the first hit rank? | average of 1 / rank of the first hit; a miss counts 0 | reported only |

H5 (French) is reported on its own line. It's a known miss and doesn't count.

For rows that need two sources (`all`), the first hit is where the **last** needed source appears. Run it with `AILAB_EVAL=1 dotnet test AiLab.Evals --filter RetrievalEvalTests` (free, about 1 s; reports in `eval-runs/retrieval/`).

Measured 2026-11-04 (`experiments.md` section 16):

| Mode | hit@1 | hit@3 | hit@5 | MRR |
|---|---|---|---|---|
| vector (what `/ask` uses) | 0.583 | 0.833 | 1.000 | 0.734 |
| hybrid | 0.667 | 0.917 | 0.958 | 0.787 |

## Layer 2: abstaining

"Should abstain" = the 4 `abstain` rows plus E5.

| Metric | What it answers | How it's computed | Threshold |
|---|---|---|---|
| Gate on E5 | Is the unrelated question stopped before the model? | runs where E5 had no model call | 3 of 3 |
| Gate on answerable rows | Does the gate wrongly stop a real question? | answerable rows with best distance > 0.50 | 0 |
| Abstain recall | Does it say "not in the docs" when it should? | `abstain` runs where Haiku abstained | ≥ 11 of 12 |
| Abstain precision | Does it refuse questions it could answer? | answerable rows where Haiku abstained in 2 of 3 runs | at most 1 row |
| Distance plot | Can the gate separate the two groups? | best distance of answerable vs not-answerable rows | reported only |

Week 4's distances overlapped (answerable 0.26–0.48, not answerable 0.36–0.45). That's why the gate can only catch clearly unrelated questions.

## Layer 3: answers

Each run is pass/fail, using the row's `check`.

| Metric | What it answers | How it's computed | Threshold (Haiku) |
|---|---|---|---|
| Correctness | Is the answer right? | pass/fail per run against the gold answer | ≥ 21 of 25 answerable questions |
| Exact match | Are names and numbers exact? | the gold string is in the answer (A3, F1, F4, F5, G2) | 5 of 5 |
| List completeness | Is every item there? | every gold item present (A15, B7, N4, B6, G1, N7) | 6 of 6 |
| Partial answers | Does it say the number isn't documented, without inventing one? | D1, D3 | 2 of 2 |
| Faithfulness | Does each cited chunk support its claim? | runs where every citation holds its claim | ≥ 90% of runs with citations |
| Citation precision | Are the cited chunks the right ones? | cited chunks that hold the claim / all cited chunks | ≥ 90% |
| Format failures | Did the reply need a repair, or fail with a 502? | count | at most 1 of 90 runs |
| Cost and latency | What does a run cost, and how long does it take? | total cost; p95 generation time | reported only |

**Faithfulness is scored by hand** until an LLM judge has been checked against the hand labels from week 4. The judge must reach about 0.8 precision and recall on "not supported" before it's used on its own.

## Regression rule

A change to the prompt, model, chunker or settings is accepted only if:
1. every threshold still holds, and
2. no question that passed on the baseline now fails 2 of 3 runs.

## The thresholds are provisional

They come from week 4 (Haiku: 18 of 22 runs OK). The first full run of the set is the baseline. Confirm or adjust the thresholds after it, then freeze them.

They're counts, not percentages: with 25 answerable questions, one question is 4%. A count keeps one flaky question from looking like a trend.

## Deliberately not used

- **Word-overlap or embedding-similarity scores** (ROUGE, BLEU, BERTScore) against the gold answers. Good and bad answers score too close together. Week 4's negation pair scored 0.94 similarity.
- **1–10 LLM grades.** Pass/fail with a reason is easier to check and act on.
- **Accuracy alone for abstaining.** It hides which way `/ask` is wrong.

## Not covered by this set

Multi-turn conversations, a bigger or messier corpus, prompt injection, load, and languages other than English and French.
