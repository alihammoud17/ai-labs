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

## Week 8: score the golden set on a real Knowledge Base

Added 2026-11-25 (week 8 Tuesday, plan `docs/plans/2026-11-24-week-8-managed-vs-own-rag.md`, Task 7.1). The code to score a Knowledge Base with the same golden set and the same scoring as the pgvector RAG exists and ran on aws-emulator; the real-account run fills the "KB on Bedrock (pending)" column of [managed-vs-own-rag-2026-11-24.md](../../docs/evals/managed-vs-own-rag-2026-11-24.md). Each step says whether it **ran on the emulator** or is **real account only**.

1. **Check the cost first. Real account only.** Three meters, none of them priced in this repo yet: the vector store (S3 Vectors storage and queries), Titan Text Embeddings v2 tokens (the whole corpus once at ingestion, about 9k tokens by week 4's count, plus every query), and the answer model's tokens for each `RetrieveAndGenerate` call (30 rows × 3 runs; the service reports no usage back, so read it from the bill or CloudWatch). Look them up on [Amazon Bedrock pricing](https://aws.amazon.com/bedrock/pricing/) and [Amazon S3 pricing](https://aws.amazon.com/s3/pricing/) and write the expected total down before step 2. Avoid an OpenSearch Serverless store: it has an always-on minimum charge.
2. **Build the Knowledge Base. Ran on the emulator** (`docker/bedrock-kb/bedrock-kb-emulator.sh`, steps 4–9), **real account only** for the real one. Don't run the script itself on the real account: it hard-codes account `000000000000` and stops unless it's talking to the emulator. Run its commands by hand with the real account ID:
   - an S3 bucket with the whole corpus: `aws s3 sync data/corpus s3://<bucket>/ --delete --exclude ".*" --exclude "*/.*"`; `aws s3 ls s3://<bucket> --recursive` must list exactly `README.md`, `docs/architecture.md`, `docs/contracts.md`, `docs/decisions.md` (the golden set's `gold.file` paths: the adapter strips `s3://<bucket>/`);
   - an S3 Vectors bucket and index: dimension 1024, cosine;
   - an IAM service role trusted by `bedrock.amazonaws.com`, allowed `s3:GetObject` / `s3:ListBucket` on that bucket, `s3vectors:*` on that index only, and `bedrock:InvokeModel` on Titan v2 only (the script's role uses `*` for the last two: narrow them);
   - the KB (`VECTOR`, `embeddingModelArn` Titan Text Embeddings v2, storage `S3_VECTORS`), then the S3 data source with default chunking (about 300 tokens; hierarchical isn't recommended with S3 Vectors);
   - an ingestion job: 4 scanned, 4 indexed, 0 failed. Note the KB ID.
3. **IAM for `ai-lab-dev`. Real account only.** Add a statement to the real policy: `bedrock:Retrieve` and `bedrock:RetrieveAndGenerate` on `arn:aws:bedrock:us-east-1:${ACCOUNT_ID}:knowledge-base/<KB_ID>` only (write `${ACCOUNT_ID}` before the `:` in zsh). `RetrieveAndGenerate` also invokes the answer model, which `AllowBedrockModelInvocation` must allow. Check both with the policy simulator before the first call, as in step 6 of the checklist.
4. **Safety check, reversed. Real account only.** In a fresh terminal, the block at the end of this page must print `REAL`. Leave `Bedrock__ServiceUrl` and `Bedrock__EmulatorCaPath` unset: with any endpoint variable set, the runner labels the run "KB on aws-emulator" (`KnowledgeBaseTarget`), which is the check that it really left the emulator.
5. **Retrieval. Ran on the emulator** (2026-11-25, with the emulator variables: `eval-runs/retrieval/2026-11-25-kb-emulator.md`, 7/24 = its ceiling), **real account only** for real numbers:
   ```bash
   AILAB_EVAL=1 AILAB_EVAL_JUDGE=none AILAB_EVAL_ANSWERS=llama KnowledgeBase__Id=<KB_ID> \
   dotnet test AiLab.Evals --filter KnowledgeBaseRetrievalEvalTests
   ```
   Open `eval-runs/retrieval/<date>-kb-bedrock.md`: the header must say **KB on Bedrock (measured)**, and read its **Not located** section before any number. Bedrock's chunks may not be the file's text as written (parsing), and an unlocated passage counts as a miss. The same day, re-run pgvector (`AILAB_EVAL=1 AILAB_EVAL_JUDGE=none AILAB_EVAL_ANSWERS=llama dotnet test AiLab.Evals --filter "FullyQualifiedName~Retrieval_hit_rate&(DisplayName~vector|DisplayName~hybrid)"`, with Postgres and Ollama up).
6. **Answers. Ran on the emulator** (2026-11-25, 1 run: `eval-runs/meai/kb-emulator-summary.md`, 3/16, a retrieval proxy), **real account only** for real answers:
   ```bash
   AILAB_EVAL=1 AILAB_EVAL_JUDGE=none AILAB_EVAL_ANSWERS=llama AILAB_EVAL_RUNS=3 \
   KnowledgeBase__Id=<KB_ID> KnowledgeBase__ModelArn=<answer model ARN> \
   dotnet test AiLab.Evals --filter KnowledgeBaseAskEvalTests
   ```
   Results in `eval-runs/meai/results/kb-bedrock/`, summary `eval-runs/meai/kb-bedrock-summary.md`. Its cost shows `?` by design (no usage from the service): the step 1 meters, read from the bill, give the real figure. Then the hand pass on the 12 judge rows, as in week 5.
7. **Apply the rule. Real account only.** Fill the "KB on Bedrock" rows of the D2 doc from steps 5–6, then the plan's Task 1.4 bars: retrieval hit@5 ≥ 23/24 and MRR within 0.05 of that day's vector run; answers gold check ≥ 14/16 on run 1, abstains ≥ 11/12 plus E5, hand pass ≥ 10/12. E5 can't pass without a gate (the one-call KB has none).

## Before the first real call, every time

In a fresh terminal (a terminal that once sourced `ai-lab-kb.env` carries `AWS_ENDPOINT_URL`, which the SDK copies into `ServiceURL`):

```bash
unset AWS_ENDPOINT_URL AWS_CA_BUNDLE AWS_SHARED_CREDENTIALS_FILE AWS_CONFIG_FILE SSL_CERT_FILE
export AWS_PROFILE=ai-lab-dev AWS_REGION=us-east-1
[ "$(aws sts get-caller-identity --query Account --output text)" != 000000000000 ] && echo REAL || echo STOP
```

Also leave `Bedrock__ServiceUrl` and `Bedrock__EmulatorCaPath` unset: they are emulator only.
