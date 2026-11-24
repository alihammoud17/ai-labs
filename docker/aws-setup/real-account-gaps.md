# Real account: what exists and what's still missing

Written 2026-11-24, at the end of week 7. It assumes a real AWS account now exists. Nothing from week 7 has run on it yet; every run so far was on aws-emulator. The checklist itself is in [README.md](README.md#on-the-real-account), "On the real account". This page says what that checklist starts from, and what else stands between week 7's code and a real Bedrock run.

## The JSON files are inputs, not proof

The files in this folder are **request bodies** for the AWS CLI. They prove the commands are written, not that anything exists.

| File | What it's for | Where it was created |
|---|---|---|
| `budget.json`, `budget-notifications.json` | `aws budgets create-budget` (5 USD monthly, alerts at 80% actual and 100% forecast) | Emulator only (week 7 Monday) |
| `bedrock-invoke-policy.json` | `aws iam create-policy` (`AiLabBedrockInvoke`) | Emulator only. **Must be edited first**, see below |
| `anthropic-use-case.json` | the Anthropic first-use form | Emulator only. Not needed for Haiku 5.5 |

The emulator rehearsal doesn't carry over to the real account:
- **Its resources are gone or going.** The emulator keeps everything in memory, so the budget, policy and user disappear when the container stops. By Wednesday the user `ai-lab-dev` was already gone after a restart.
- **It never had real consequences.** It sent no budget email, counted no spend, and checked no permissions on actual calls.

On the real account, a resource exists only once it has been **read back there**: `aws budgets describe-budgets`, `aws iam get-policy`, `aws iam list-attached-user-policies --user-name ai-lab-dev`, `aws bedrock get-foundation-model-availability`.

## Missing on the account (none of it exists yet)

In the checklist's order:

1. **Root locked down** and an admin identity with MFA (checklist step 1).
2. **A payment method,** needed for Marketplace models such as Claude (step 2).
3. **The budget,** created from the two budget files with the real account ID and email, and the verification email confirmed (step 3).
4. **The inference profiles checked** for `us.anthropic.claude-haiku-5-5` and `us.amazon.nova-lite-v1:0`, with each profile's foundation-model ARNs noted, one per region (step 4).
5. **The policy, edited, then created.** `bedrock-invoke-policy.json` is the emulator version:
   - It allows Haiku **4.5** (`anthropic.claude-haiku-4-5-20251001-v1:0`), not Haiku 5.5. Swap in Haiku 5.5's inference-profile ARN, plus a foundation-model ARN for each region the profile routes to.
   - Its inference-profile ARN carries the emulator's account ID, `000000000000`. Use the real one, and write `${ACCOUNT_ID}` in zsh.
   - Then `create-policy`, `create-user ai-lab-dev`, `attach-user-policy` (step 5).
6. **The simulator re-run** on the real policy, all 13 cases (step 6).
7. **Model access:** the first Claude call as the admin, then wait for `get-foundation-model-availability --model-id anthropic.claude-haiku-5-5` to return `AVAILABLE` (step 7).
8. **Credentials for `ai-lab-dev`** in a named profile, preferably short-lived IAM Identity Center credentials (step 8). The code holds no credentials: the AWS credential chain finds `AWS_PROFILE=ai-lab-dev`.
9. **Proof as `ai-lab-dev`:** one `converse` call per model, one Titan `invoke-model`, and one model outside the policy returning `AccessDeniedException` (step 9).

## Missing per day of week 7

Each day's code is configuration-ready. What's left is listed day by day.

### Monday: AWS setup
Everything in the account list above. The code needs nothing.

### Tuesday: Bedrock as an `IChatClient` (console, API)
- **Console chat:** ready. Run it with `Chat__Provider=bedrock Chat__Model=us.amazon.nova-lite-v1:0` (or `us.anthropic.claude-haiku-5-5` after step 7), in a fresh terminal with no emulator variables. On the real account it was never run: no streamed reply, real tokens or real cost seen yet.
- **`POST /analyze-ticket` on Bedrock: doesn't work yet.** It sends a response format together with tools. The AWS package's default structured output (a forced synthetic tool) can't share the request with them, and it throws `ArgumentException` before any call. Missing: `BedrockStructuredOutputMode.Native`, or no response format on Bedrock (Tuesday's follow-up).

### Wednesday: `AiLab.Rag` on Bedrock (chat + Titan embeddings)
- **The Titan collection is empty.** `corpus_titan_v2_1024` has never been filled: the emulator answers every embedding call with HTTP 424. Missing: run [README.md](README.md#re-embed-the-corpus-on-bedrock), "Re-embed the corpus on Bedrock", which is one ingest of about 71 `InvokeModel` calls.
- **Titan has no price.** It isn't in `ModelPrices` (`AiLab.Application/Services/ModelPrices.cs`), so embedding cost can't be shown yet.
- **The distance gate is tuned for nomic.** `Ask:MaxDistance` 0.50 was set on nomic's distances; Titan's will differ. It needs re-tuning on the retrieval comparison (nomic vs Titan: hit@1, hit@5, MRR) before trusting abstains.
- **`/ask` needs a chat model that fills the forced tool.** `AskService` asks for a typed `AskAnswer`, and the package does that with a forced tool. Nova Lite answered `{}` on the emulator; on real Bedrock this is unknown. Haiku 5.5 is the expected fit, but it's untested.

### Thursday: the eval suite on Bedrock
- **Ready:** `AILAB_EVAL_ANSWERS=bedrock`, `AILAB_EVAL_MODEL`, `AILAB_EVAL_JUDGE=none`, the Bedrock client from `Bedrock:*`, the gold check, and the cost-per-correct report. Prices for both model IDs are in `ModelPrices`.
- **Missing:** the run itself. [docs/evals/README.md](../../docs/evals/README.md#run-the-eval-suite-on-bedrock), "Run the eval suite on Bedrock": 2 models × 90 answers, about $0.07. Then the hand pass on the 12 judge rows into `eval/results/<date>-bedrock-hand-scores.md`, the regenerated report, and the decision rule (plan `docs/plans/2026-11-19-week-7-bedrock-model-evals.md`, Task 1.4).
- **Unknown until that run:**
  - **The forced tool:** whether Nova Lite fills it, the same issue as Wednesday. If it doesn't, the report shows the model as **incomplete** and the rule fails it. The fix is code (Bedrock native structured output), not a re-run.
  - **The model ID:** whether Bedrock reports it the way `ModelPrices` keys it. If not, cost shows `?`: a safe failure, but one to fix.
  - **Tokens and latency:** Nova Lite's tokenizer differs, so its projected $0.000258 per answer is only an estimate.

## Not part of week 7

The managed knowledge base (`AiLab.Bedrock`, `docker/bedrock-kb/`, week 8 Monday, done early) was built only by the emulator script. A real knowledge base needs its own setup (S3, S3 Vectors, an IAM role, ingestion) and costs money beyond model calls. It isn't covered here.

## Before the first real call, every time

In a fresh terminal (a terminal that once sourced `ai-lab-kb.env` carries `AWS_ENDPOINT_URL`, which the SDK copies into `ServiceURL`):

```bash
unset AWS_ENDPOINT_URL AWS_CA_BUNDLE AWS_SHARED_CREDENTIALS_FILE AWS_CONFIG_FILE SSL_CERT_FILE
export AWS_PROFILE=ai-lab-dev AWS_REGION=us-east-1
[ "$(aws sts get-caller-identity --query Account --output text)" != 000000000000 ] && echo REAL || echo STOP
```

Also leave `Bedrock__ServiceUrl` and `Bedrock__EmulatorCaPath` unset: they are emulator only.
