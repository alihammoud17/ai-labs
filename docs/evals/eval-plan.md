# /ask eval plan

How `POST /ask` is measured from week 5 on: which metrics, how each is scored, and the threshold each must reach. The input is the 30-question set in [ask-set-v1.md](ask-set-v1.md).

## What gets measured

`/ask` does four jobs, and each one gets its own metrics:

| Job | Where it can fail | Measured in |
|---|---|---|
| Finding the right chunk | the answer isn't among the sources sent | Layer 1: retrieval |
| Deciding whether to answer | it answers what it shouldn't, or refuses what it can answer | Layer 2: abstaining |
| Getting exact facts right | wrong name, number or path; missing list items | Layer 3: answers |
| Staying faithful to sources | invented claims, wrong citations, general knowledge | Layer 3: answers |

The layers run cheapest first. Retrieval needs no chat model, so a chunking or embedding change can be checked in seconds for free.

## Setup for every run

- Settings as on `main`: heading chunker, top 5, `MaxDistance` 0.50, `prompts/ask-v1.md`, `MaxOutputTokens` 2000, `nomic-embed-text` without task prefixes.
- **Haiku 5.5 is the model the thresholds apply to.** llama3.2 is run and reported, but it's the development model, so it doesn't pass or fail anything.
- **3 runs per question** for layer 3. One run gives an anecdote; three give a rate.
- Record the commit, settings, model, tokens, cost and timings with each run, as in week 4.

## Layer 1: retrieval (no chat model)

Run `GET /search?top=10&chunker=heading` for the 25 answerable rows.

**What counts as a hit:** a chunk from the gold file whose line range contains a gold answer line. This doesn't depend on the chunker, so the gold set stays valid when chunking changes.

| Metric | Meaning | Threshold |
|---|---|---|
| Doc hit@5 | a gold document is among the 5 sources sent | ≥ 23 of 24 English rows |
| Recall@5 | a gold chunk is in the top 5 | ≥ 22 of 24 English rows |
| Recall@10, both sources | for the 5 two-source rows, every needed chunk is in the top 10 | ≥ 4 of 5 |
| MRR | average of 1 / rank of the first gold chunk | reported, no threshold yet |

H5 (French) is reported on its own line. It's a known miss and doesn't count against the thresholds.

## Layer 2: abstaining

Here, "positive" means the question **shouldn't be answered**: the 4 `abstain` rows plus E5. Precision and recall are reported instead of a single accuracy, because accuracy hides which way `/ask` is wrong.

- **Recall:** of the runs that should abstain, how many did. Low recall means it answers what isn't in the docs.
- **Precision:** of the runs where `/ask` abstained, how many should have. Low precision means it refuses questions it could answer.

| Check | Threshold |
|---|---|
| E5 stopped at the gate, with no model call | 3 of 3 runs |
| Answerable rows stopped by the gate (best distance > 0.50) | 0 |
| `abstain` rows answered "not in the docs" (Haiku) | ≥ 11 of 12 runs |
| Answerable rows where Haiku abstained, in 2 or more of 3 runs | at most 1 row |

**Distance plot (separation of distributions):** plot the best distance of the 25 answerable rows against the 5 rows that shouldn't be answered. In week 4 the two ranges overlapped (answerable 0.26–0.48, not answerable 0.36–0.45). The plot shows why `MaxDistance` can only catch clearly unrelated questions, and whether a change to embeddings or chunking separates them better.

## Layer 3: answers

Each run is scored pass/fail by the row's `check`. A question passes when **2 of its 3 runs pass**.

| Metric | How it's scored | Threshold (Haiku) |
|---|---|---|
| Correctness | pass/fail per run against the gold answer | ≥ 21 of 25 answerable questions |
| Exact match | the gold string is in the answer (A3, F1, F4, F5, G2) | 5 of 5 |
| List completeness | every gold item is present (A15, B7, N4, B6, G1, N7) | 6 of 6 |
| Partial answers | says the number isn't documented and invents none (D1, D3) | 2 of 2 |
| Faithfulness | every claim is supported by the source it cites | ≥ 90% of runs with citations |
| Citation precision | cited sources that hold the claim / all cited sources | ≥ 90% |
| Format failures | a repair was needed, or a 502 | at most 1 of 90 runs |
| Cost and latency | total cost per run; p95 generation time | reported, no threshold yet |

### How faithfulness is scored

- **This week, by hand.** For each cited `[n]`, read the cited chunk and mark whether it supports the sentence that cites it. This is the check `CitationCheck` can't make: it confirms that `[n]` was sent, not that it holds the claim.
- **Then, with an LLM judge.** It gets one claim and one chunk, and answers supported or not supported, with a one-line reason. No 1–10 score.
- **The judge must earn trust first.** Run it on the week 4 runs that are already labelled. llama3.2's M1, M2 and M4 runs are the known positives. Only use it on its own if it agrees with the hand labels, with precision and recall of "not supported" both at about 0.8 or higher. Until then, keep scoring by hand.

## Regression rule

A change to the prompt, model, chunker or settings is accepted only if:
1. every threshold still holds, and
2. no question that passed on the baseline now fails 2 of 3 runs.

## What's deliberately not used

- **No word-overlap or embedding-similarity scores against the gold answers** (ROUGE, BLEU, METEOR, BERTScore). Good and bad answers score too close together to set a threshold. Week 4's negation pair at 0.94 similarity showed the same problem.
- **No 1–10 LLM grades.** Pass/fail with a reason is easier to check and easier to act on.
- **No accuracy on its own** for abstaining. Precision and recall show which way it's wrong.

## Notes on the numbers

- **The thresholds are provisional.** They come from week 4, where Haiku had 18 of 22 runs OK. Both of its real misses were the 800-token cut-off, which is fixed now. The first full run on this set is the baseline. Confirm or adjust the thresholds after it, then freeze them.
- **One question is 4% of the answerable set.** That's why thresholds are counts ("at most 4 wrong") rather than percentages, so one flaky question doesn't look like a trend.
- **Cost:** 90 Haiku calls at about 3,000 input and 300 output tokens each come to roughly $0.04 per full run. llama3.2 is free.
- **Time:** `Ask:PermitsPerMinute` is 10, so 90 calls take at least 9 minutes. Pace the runner about 6.5 seconds apart, or raise the limit for eval runs in Development.

## What this set doesn't cover

- multi-turn conversations
- a larger or messier corpus
- prompt injection inside documents or questions
- load and concurrency
- languages other than English and French

## Order of work

1. Turn [ask-set-v1.md](ask-set-v1.md) into a JSON file the runner can read.
2. Build the layer 1 runner and get the retrieval baseline.
3. Add layers 2 and 3. Run the baseline: 3 runs per question on Haiku and on llama3.2.
4. Score faithfulness by hand on the baseline, then build and calibrate the judge.
5. Review the thresholds against the baseline and freeze them.
