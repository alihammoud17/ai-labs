# Cost per correct answer, 2026-11-19

## Measured

From stored eval results only. **Correct** = `GoldCheck` (facts from the row's `match` list plus a citation that holds a gold line; abstains by status or phrase; E5 by the gate) over the gold-checkable runs (16 rows × 3 runs = 48 per complete execution); judge-scored rows are left out. **Cost** = `/ask`'s own call (model and tokens from the `Ask outcome` metadata, judges excluded) over those runs; gated runs made no call and add $0. **Cost per correct** = cost ÷ correct. Cost per 1,000 and latency are over the checked runs that reached the model. Latency = retrieval + generation.

| Execution | Runs | Checked | Correct | Accuracy | Run 1 correct / checked | Reached model | `/ask` model | `/ask` cost (checked runs) | Cost per correct | Cost per 1,000 answers | Latency median | Latency p95 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| matrix-heading-haiku | 90 | 48 | 48 | 100% | 16 / 16 | 45 | `claude-haiku-5-5` | $0.020521 | $0.000428 | $0.46 | 1.43 s | 3.47 s |
| matrix-fixed-haiku | 90 | 48 | 39 | 81% | 13 / 16 | 45 | `claude-haiku-5-5` | $0.031613 | $0.000811 | $0.70 | 1.65 s | 4.79 s |
| matrix-heading-llama | 90 | 48 | 25 | 52% | 9 / 16 | 45 | `llama3.2` | $0 (local) | $0 (local) | $0 (local) | 1.35 s | 3.13 s |
| matrix-fixed-llama | 90 | 48 | 23 | 48% | 7 / 16 | 45 | `llama3.2` | $0 (local) | $0 (local) | $0 (local) | 1.41 s | 3.51 s |

Check before using:

- **matrix-heading-haiku: 89 cached call(s)** (answers or judges; the results don't say which). Cost uses the stored token counts either way; latency may be understated.
- **matrix-fixed-haiku: 88 cached call(s)** (answers or judges; the results don't say which). Cost uses the stored token counts either way; latency may be understated.
- **matrix-fixed-llama: 4 cached call(s)** (answers or judges; the results don't say which). Cost uses the stored token counts either way; latency may be understated.
