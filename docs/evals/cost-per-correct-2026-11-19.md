# Cost per correct answer, 2026-11-19

## Measured

From stored eval results only. **Correct** = `GoldCheck` (facts from the row's `match` list plus a citation that holds a gold line; abstains by status or phrase; E5 by the gate) over the gold-checkable runs (16 rows × 3 runs = 48 per complete execution); judge-scored rows are left out. **Cost** = `/ask`'s own call (model and tokens from the `Ask outcome` metadata, judges excluded) over those runs; gated runs made no call and add $0. **Cost per correct** = cost ÷ correct. Cost per 1,000 and latency are over the checked runs that reached the model. Latency = retrieval + generation.

| Execution | Runs | Checked | Correct | Accuracy | Run 1 correct / checked | Reached model | `/ask` model | `/ask` cost (checked runs) | Cost per correct | Cost per 1,000 answers | Latency median | Latency p95 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| matrix-heading-haiku | 90 | 48 | 48 | 100% | 16 / 16 | 45 | `claude-haiku-5-5` | $0.020521 | $0.000428 | $0.46 | 1.43 s | 3.47 s |
| matrix-fixed-haiku | 90 | 48 | 39 | 81% | 13 / 16 | 45 | `claude-haiku-5-5` | $0.031613 | $0.000811 | $0.70 | 1.65 s | 4.79 s |
| matrix-heading-llama | 90 | 48 | 25 | 52% | 9 / 16 | 45 | `llama3.2` | $0 (local) | $0 (local) | $0 (local) | 1.35 s | 3.13 s |
| matrix-fixed-llama | 90 | 48 | 23 | 48% | 7 / 16 | 45 | `llama3.2` | $0 (local) | $0 (local) | $0 (local) | 1.41 s | 3.51 s |
| bedrock-nova-lite | missing | | | | | | | | | | | |
| bedrock-haiku-5-5 | missing | | | | | | | | | | | |

Check before using:

- **matrix-heading-haiku: 89 cached call(s)** (answers or judges; the results don't say which). Cost uses the stored token counts either way; latency may be understated.
- **matrix-fixed-haiku: 88 cached call(s)** (answers or judges; the results don't say which). Cost uses the stored token counts either way; latency may be understated.
- **matrix-fixed-llama: 4 cached call(s)** (answers or judges; the results don't say which). Cost uses the stored token counts either way; latency may be understated.
- **bedrock-nova-lite: no stored results.** Nothing measured; not 0 correct and not $0.
- **bedrock-haiku-5-5: no stored results.** Nothing measured; not 0 correct and not $0.

## Projected (not measured)

Prices × the `/ask` tokens of a measured execution (`matrix-heading-haiku`): what the same answers would cost. Nothing here was run, so correctness is **pending**; these rows are not results.

| Model | Price in / out per 1M | Avg `/ask` tokens in / out | Source execution | Projected cost per answer | Projected cost per 1,000 | Correctness |
|---|---|---|---|---|---|---|
| `us.amazon.nova-lite-v1:0` | $0.06 / $0.24 | 3,245 / 263 | matrix-heading-haiku | $0.000258 | $0.26 | pending |
| `us.anthropic.claude-haiku-5-5` | $0.11 / $0.55 | 3,245 / 263 | matrix-heading-haiku | $0.000502 | $0.50 | pending |

The tokens are `matrix-heading-haiku`'s, counted by its model's tokenizer. A model with another tokenizer (Nova Lite) will count the same prompt and answer differently, and may answer longer or shorter, so its projection is an estimate of price, not of cost.

## Artificial Analysis (sanity check, not a result)

Source: artificialanalysis.ai model pages ([Claude Haiku 5.5](https://artificialanalysis.ai/models/claude-haiku-5-5), [Nova Lite](https://artificialanalysis.ai/models/nova-lite), [Llama 3.2 Instruct 3B](https://artificialanalysis.ai/models/llama-3-2-instruct-3b)), read 2026-11-23. Their quality number is the Artificial Analysis Intelligence Index v5.0.1, a general benchmark; "my accuracy" is the gold check above on this app's 48 checked runs. Hand-written: regenerating this report (`AILAB_EVAL_COST`) drops this section, so copy it back.

| Model | Their Intelligence Index (v5.0.1) | Their price in / out per 1M | Their output speed | My accuracy |
|---|---|---|---|---|
| Claude Haiku 5.5, Anthropic API | 25 at the default effort (medium); 19 low … 36 max | $0.10 / $0.50 | 219 tok/s (medium) | 100% (48/48, heading; measured) |
| Claude Haiku 5.5 on Bedrock (`us.`) | not listed (Anthropic API only) | not listed | not listed | pending |
| Nova Lite (`us.amazon.nova-lite-v1:0`) | not scored (N/A; deprecated) | $0.06 / $0.24 | 118 tok/s (Amazon Bedrock) | pending |
| llama3.2 (3B, local Ollama) | not scored (N/A; deprecated) | no public API | not listed | 52% (25/48, heading; measured) |

Their ranking can't contradict mine: Haiku 5.5 is the only one of the four they score, so there is no order to compare (week 5, and the table above: Haiku ≫ llama3.2), and the rule's reopening condition (opposite ranking within noise) can't be met. Their prices match `ModelPrices` exactly (Haiku $0.10 / $0.50, Nova Lite $0.06 / $0.24), so no price re-check. For this app I trust my own numbers: they're on these questions, these chunks and this prompt, with citations checked; a general index says nothing about whether a model fills `/ask`'s forced tool (Nova Lite on the emulator didn't).
