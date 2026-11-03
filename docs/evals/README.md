# Evals in plain words

## What an eval is

An **eval** runs the real model on a fixed set of questions and scores what it does.

It's not the same as the tests in `AiLab.Rag.Tests`:

| | Tests (`AiLab.Rag.Tests`) | Evals (this folder) |
|---|---|---|
| The model | a fake (`ScriptedChatClient`) whose reply is written in advance | the real one (Haiku 5.5, llama3.2) |
| They check | that the code reacts correctly | how good the answers are |
| Example | a made-up citation `[7]` becomes `unverified` | llama3.2 answered "default 15 minutes" on D1; the docs give no number |
| Result | pass or fail, always the same | a rate (for example 21 of 25 right), varies between runs |
| Cost | free, offline | model calls; Ollama is free, Haiku costs a few cents |

Tests prove the code is right. Evals measure the model.

## Glossary

- **Gold answer:** the right answer, written by hand before any run. Example: A3's gold is `2026-03-18`.
- **Retrieval hit:** a chunk sent to the model that holds the answer. It counts when the chunk is from the gold file and its line range contains the gold line.
- **Recall@k:** did a hit appear in the top k chunks? Recall@5 = rows with a hit in the top 5 / answerable rows. Week 4: every English question had a hit in the top 4.
- **MRR (mean reciprocal rank):** the average of 1 / rank of the first hit. A hit at rank 1 scores 1, at rank 4 scores 0.25. Higher means the answer comes earlier.
- **Precision and recall of abstaining:** "abstain" means saying "not in the docs". Recall: of the questions that should abstain, how many did. Precision: of the times `/ask` abstained, how many were right to. Example: llama3.2 invented "90 minutes" for D2 instead of abstaining (a recall miss). It abstained on G2, which the docs do answer (a precision miss).
- **Faithfulness:** every claim in the answer is in the source it cites. llama3.2's B7 cited real chunks but invented rate limits: not faithful.
- **LLM judge:** a model that scores another model's answer. Cheaper than reading every answer yourself, but its verdicts have to be checked against your own labels first.
- **Threshold:** the minimum a metric must reach, as a count. Example: "at least 22 of 24 rows with a hit in the top 5".
- **Baseline:** the first full run of the set. Every later change is compared with it.
- **Regression:** a question that passed on the baseline and fails after a change.

## The three layers

Each layer is checked before the next, cheapest first.

```
question ──► 1. Retrieval ──► 2. Abstaining ──► 3. Answers
             found the right   answer or say    right, complete,
             chunk?            "not in docs"?   faithful?
```

| Layer | Question it answers | Needs a chat model? | Week 4 example of a failure |
|---|---|---|---|
| 1. Retrieval | Is the answer among the 5 chunks sent? | No: only embeddings and Postgres, free | H5 (French): the answering chunk wasn't in the top 10 |
| 2. Abstaining | Does it refuse what isn't in the docs, and only that? | Yes | D9: llama3.2 gave a general definition of multi-tenancy |
| 3. Answers | Are the facts right, complete, and supported by the cited chunks? | Yes | B7: llama3.2 invented limits and left out refresh |

## Where the MEAI evaluators fit

Tuesday's plan adds `Microsoft.Extensions.AI.Evaluation` (MEAI) in a new test project, `AiLab.Evals`. It runs only when `AILAB_EVAL=1` is set.

| Layer | MEAI evaluator (Haiku as judge, score 1–5) | Our own check (no model) |
|---|---|---|
| 1. Retrieval | `RetrievalEvaluator`: do the chunks look relevant to the question? | Recall@5 from the gold lines |
| 2. Abstaining | not covered | counted from `/ask`'s `grounding.status` |
| 3. Answers | `GroundednessEvaluator`: is the answer supported by the chunks sent? `RelevanceEvaluator`: does it address the question? | |

MEAI scores from 1 to 5, and a score below 4 counts as failed. Our eval plan scores pass/fail. So for now the MEAI scores are **recorded, not trusted**. First the judge's verdicts are compared with the hand labels from week 4. Only if they agree do they count.

## Reading order

1. This page.
2. [eval-plan.md](eval-plan.md): what is measured and the thresholds.
3. [ask-set-v1.md](ask-set-v1.md): the 30 questions and their gold answers.
4. [Tuesday's plan](../plans/2026-11-03-week-5-meai-evaluation.md): running the set with MEAI.
5. [Week 5 review](../reviews/week-5-review.md): what I learned and decided.
