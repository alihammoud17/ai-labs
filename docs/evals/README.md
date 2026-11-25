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

How it runs, what each evaluator reads and how the cache behaves: [meai-evals.md](meai-evals.md).

MEAI scores from 1 to 5, and a score below 4 counts as failed. Our eval plan scores pass/fail. So for now the MEAI scores are **recorded, not trusted**. First the judge's verdicts are compared with the hand labels from week 4. Only if they agree do they count.

## Run the eval suite on Bedrock

The week 7 runbook for the real account (plan: [2026-11-19-week-7-bedrock-model-evals.md](../plans/2026-11-19-week-7-bedrock-model-evals.md)). It turns the **projected** Bedrock rows of [cost-per-correct-2026-11-19.md](cost-per-correct-2026-11-19.md) into measured ones: Nova Lite and Claude Haiku 5.5 answer `/ask` through Bedrock; there is **no judge** (`AILAB_EVAL_JUDGE=none`, no Anthropic call), so "correct" is the gold check (`GoldCheck`: the row's `match` facts plus a citation that holds a gold line) on the 16 code-checkable rows, plus a hand pass on the 12 judge rows. Each command below was run once on aws-emulator (2026-11-23, with the emulator variables) or is marked **real account only**.

**Before you start:** Postgres up with the corpus ingested (heading chunks, nomic: the eval host still embeds with Ollama), Ollama running, model access and the `AiLabBedrockInvoke` policy in place ([`docker/aws-setup/README.md`](../../docker/aws-setup/README.md), "On the real account").

1. **Expected cost, written before any run.** From the projected table (HH's `/ask` tokens × Bedrock prices), 90 answers per model (30 rows × 3 runs; 87 reach the model, the 3 E5 runs are gated):

   | Model | Projected per answer | × 90 answers |
   |---|---|---|
   | `us.amazon.nova-lite-v1:0` | $0.000258 | ≈ $0.023 |
   | `us.anthropic.claude-haiku-5-5` | $0.000502 | ≈ $0.045 |
   | **Both** | | **≈ $0.07** |

   No judge cost (none) and no embedding cost (Ollama). Nova Lite's tokenizer counts differently, so its real figure may differ; anything above ≈ $0.20 for both means something re-ran. The $5 budget alarm (week 7 Monday) is far away.

2. **Safety check, reversed: you must be on the real account.** A fresh terminal (one that sourced `ai-lab-kb.env` carries `AWS_ENDPOINT_URL`, which the SDK copies into `ServiceURL`):
   ```bash
   unset AWS_ENDPOINT_URL AWS_CA_BUNDLE AWS_SHARED_CREDENTIALS_FILE AWS_CONFIG_FILE SSL_CERT_FILE
   export AWS_PROFILE=ai-lab-dev AWS_REGION=us-east-1
   [ "$(aws sts get-caller-identity --query Account --output text)" != 000000000000 ] && echo REAL || echo STOP
   ```
   `REAL` to go on. **Real account only** for `REAL`; on the emulator (sourced terminal, 2026-11-23) the same test printed `STOP`, which is the point.

3. **One full run per model**, a new cache key so the answers (and their latency) are fresh:
   ```bash
   KEY=real-$(date +%Y%m%d-%H%M)
   AILAB_EVAL=1 AILAB_EVAL_ANSWERS=bedrock AILAB_EVAL_MODEL=us.amazon.nova-lite-v1:0 AILAB_EVAL_JUDGE=none \
   AILAB_EVAL_RUNS=3 AILAB_EVAL_CACHE_KEY=$KEY dotnet test AiLab.Evals --filter Ask_set_v1
   AILAB_EVAL=1 AILAB_EVAL_ANSWERS=bedrock AILAB_EVAL_MODEL=us.anthropic.claude-haiku-5-5 AILAB_EVAL_JUDGE=none \
   AILAB_EVAL_RUNS=3 AILAB_EVAL_CACHE_KEY=$KEY dotnet test AiLab.Evals --filter Ask_set_v1
   ```
   Results land in `eval-runs/meai/results/bedrock-nova-lite/` and `bedrock-haiku-5-5/`; delete any `bedrock-*` folder an emulator run left there first. A row that fails with an HTTP error is a setup problem, not a wrong answer: it stores nothing, while E5 (gated, no model call) always completes. **If Nova Lite answers `{}`** (502 `reply_unreadable`, as on the emulator: the package's forced tool), the run is incomplete, the report shows it as **incomplete** with no accuracy or cost, and the rule fails it on bar 1. Write that down; the fix is a code change (Bedrock structured output), not a re-run. On the emulator (with `source ~/.aws/emulator/ai-lab-kb.env`, `Bedrock__ServiceUrl=https://127.0.0.1:9297` and `Bedrock__EmulatorCaPath=$HOME/.aws/emulator/9297/ca.pem` added, safety check `SAFE` before each) both commands ran: 29 of 30 rows 502 (Nova Lite `reply_unreadable`, Haiku 5.5 `provider_unavailable`: not in the emulator's catalog), and E5 passed, so each execution stored E5's 3 runs and nothing else (the folders were deleted afterwards). **Real account only** for real answers.

4. **Hand pass on the 12 judge rows, run 1**, as in week 5: copy the table of [`eval/results/2026-11-05-matrix-hand-scores.md`](../../eval/results/2026-11-05-matrix-hand-scores.md) into `eval/results/<date>-bedrock-hand-scores.md`, one section per model, same rule (facts → support → citation, stop at the first fail). The answers are in the stored results (`dotnet aieval report --path eval-runs/meai --output eval-runs/meai/report.html`). **Real account only** (nothing to read on the emulator).

5. **Regenerate D2 with the measured rows**, then apply the decision rule (plan, Task 1.4):
   ```bash
   AILAB_EVAL_COST=matrix-heading-haiku,matrix-fixed-haiku,matrix-heading-llama,matrix-fixed-llama,bedrock-nova-lite,bedrock-haiku-5-5 \
   dotnet test AiLab.Evals --filter Write_cost_per_correct
   ```
   The `bedrock-*` rows move from missing to measured; leave out `AILAB_EVAL_COST_PROJECTED` once both are measured. Regenerating **drops the hand-written Artificial Analysis section**: copy it back from git. Then the rule: complete (90 runs, no setup errors), gold run 1 ≥ 14/16, gold all runs ≥ 40/48, abstains ≥ 11/12 plus E5 3/3, hand pass ≥ 10/12, p95 ≤ 5 s (uncached: the new key); the winner has the most correct of 28 on run 1, a tie goes to the lower cost per correct, and if neither Bedrock model passes, Rag stays on heading + Haiku through the Anthropic API. Ran on the emulator (2026-11-23) on step 3's E5-only folders: at first both rows read "3 / 3 correct, 100%, $0.000000"; the report was fixed to show an execution with fewer than 90 runs as **incomplete: 3 of 90** with no accuracy or cost, and re-run. With no folder at all a row reads **missing**. The committed D2 restored afterwards with `git checkout`.

## Reading order

1. This page.
2. [eval-plan.md](eval-plan.md): what is measured and the thresholds.
3. [ask-set-v1.md](ask-set-v1.md): the 30 questions and their gold answers.
4. [Tuesday's plan](../plans/2026-11-03-week-5-meai-evaluation.md): running the set with MEAI.
5. [meai-evals.md](meai-evals.md): how the eval project works and how to run it.
6. [baseline-2026-11-03.md](baseline-2026-11-03.md): the first baseline and what it showed.
7. [hybrid-search-2026-11-04.md](hybrid-search-2026-11-04.md): retrieval measured on its own (hit rate@k, MRR) and hybrid search tried.
8. [Week 5 review](../reviews/week-5-review.md): what I learned and decided.
9. [cost-per-correct-2026-11-19.md](cost-per-correct-2026-11-19.md): cost per correct answer (week 7), measured from week 5's runs plus projected Bedrock rows; how to measure them: [Run the eval suite on Bedrock](#run-the-eval-suite-on-bedrock).
10. [managed-vs-own-rag-2026-11-24.md](managed-vs-own-rag-2026-11-24.md): the golden set on a Bedrock Knowledge Base (aws-emulator stand-in) next to pgvector, same scoring (week 8).
