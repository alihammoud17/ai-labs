# AWS setup (rehearsed on aws-emulator)

Week 7 Monday's AWS setup, rehearsed on aws-emulator (`https://127.0.0.1:9297`, account `000000000000`). The files are reused on the real account; only the account ID, the email and the Claude model / region list change.

Plan: `docs/plans/2026-11-16-week-7-aws-setup-emulator.md`.

| File | What it is |
|---|---|
| `budget.json` | `ai-lab-monthly`: a 5 USD monthly cost budget |
| `budget-notifications.json` | Actual > 80% and forecasted > 100%, one email subscriber each (`BUDGET_EMAIL` placeholder, filled with `sed` into `$TMPDIR`, never in git) |
| `bedrock-invoke-policy.json` | `AiLabBedrockInvoke`, two statements. Chat: `bedrock:InvokeModel` + `InvokeModelWithResponseStream` on the Nova Lite profile, Nova Lite in its 3 regions and Haiku 4.5 (emulator). Embeddings (week 7 Wednesday): `bedrock:InvokeModel` only, on Titan Text Embeddings v2 in us-east-1 (embeddings don't stream). No wildcards. Attached to the user `ai-lab-dev` |
| `anthropic-use-case.json` | The Anthropic first-time-use (FTU) form, placeholders only. Sent with `fileb://` (the field is a blob) |

Always run the safety check first:

```bash
source ~/.aws/emulator/ai-lab-kb.env
[ "$(aws sts get-caller-identity --query Account --output text)" = 000000000000 ] && echo SAFE || echo STOP
```

## Model IDs

Checked 2026-11-19. Emulator columns from `list-foundation-models` / `get-inference-profile`; real columns from the AWS model cards (the emulator's catalog is old: no Haiku 5.5, no Claude 4+ inference profiles).

| Model | Emulator ID (what you call) | Real account ID / profile (what you call) | ARNs the IAM policy must allow | FTU form needed? |
|---|---|---|---|---|
| Nova Lite | `us.amazon.nova-lite-v1:0` (profile; model `amazon.nova-lite-v1:0`) | the same profile `us.amazon.nova-lite-v1:0` | Profile `arn:aws:bedrock:us-east-1:<account>:inference-profile/us.amazon.nova-lite-v1:0`, plus the model in each region it routes to. Emulator: `arn:aws:bedrock:{us-east-1,us-east-2,us-west-2}::foundation-model/amazon.nova-lite-v1:0`. Real: regions from `get-inference-profile` | No (Amazon model) |
| Claude Haiku 4.5 (emulator only) | `anthropic.claude-haiku-4-5-20251001-v1:0` (bare ID; the emulator has no profile for it: `ResourceNotFoundException`) | `us.anthropic.claude-haiku-4-5-20251001-v1:0` only (the bare ID isn't supported on demand) | Emulator: `arn:aws:bedrock:us-east-1::foundation-model/anthropic.claude-haiku-4-5-20251001-v1:0` | Yes (older than Opus 4.7). Rehearsed: the form is stored, but availability stays `NOT_AUTHORIZED` |
| Claude Haiku 5.5 (real target) | not in the emulator | `us.anthropic.claude-haiku-5-5` (model `anthropic.claude-haiku-5-5`; also `global.`) | Profile `arn:aws:bedrock:us-east-1:<account>:inference-profile/us.anthropic.claude-haiku-5-5`, plus `arn:aws:bedrock:<region>::foundation-model/anthropic.claude-haiku-5-5` in each region from `get-inference-profile` (`arn:aws:bedrock:::foundation-model/…` with `global.`) | No |

- **Profile + every region:** a call through a profile is checked against the profile ARN **and** the foundation-model ARN in the region that serves it. Allowing only one of them gives AccessDenied.
- **Take the regions from `get-inference-profile`** on the real account, not from the docs: AWS changes the region list over time.

## Least-privilege check (IAM policy simulator)

Run 2026-11-19 with `simulate-principal-policy` against the user `ai-lab-dev`, i.e. the policy **as attached**, read from IAM. The same 9 cases against the file alone (`simulate-custom-policy`) gave the same results.

```bash
# zsh: brace ${ACCOUNT_ID} before a ":" ($ACCOUNT_ID:u is zsh's uppercase modifier and eats ":u")
psim() { aws iam simulate-principal-policy --policy-source-arn "arn:aws:iam::${ACCOUNT_ID}:user/ai-lab-dev" \
  --action-names "$1" --resource-arns "${2:-*}" --query 'EvaluationResults[0].EvalDecision' --output text; }
P="arn:aws:bedrock:us-east-1:${ACCOUNT_ID}:inference-profile/us.amazon.nova-lite-v1:0"
```

| # | Action | Resource | Expected | Actual |
|---|---|---|---|---|
| 1 | `bedrock:InvokeModel` | Nova Lite profile (`$P`) | allowed | allowed |
| 2 | `bedrock:InvokeModel` | `arn:aws:bedrock:us-east-2::foundation-model/amazon.nova-lite-v1:0` | allowed | allowed |
| 3 | `bedrock:InvokeModel` | `arn:aws:bedrock:us-east-1::foundation-model/anthropic.claude-haiku-4-5-20251001-v1:0` | allowed | allowed |
| 4 | `bedrock:InvokeModelWithResponseStream` | Nova Lite profile (`$P`) | allowed | allowed |
| 5 | `bedrock:InvokeModel` | `arn:aws:bedrock:us-east-1::foundation-model/amazon.nova-pro-v1:0` | implicitDeny | implicitDeny |
| 6 | `bedrock:InvokeModel` | `arn:aws:bedrock:eu-west-1::foundation-model/amazon.nova-lite-v1:0` | implicitDeny | implicitDeny |
| 7 | `bedrock:CreateKnowledgeBase` | `*` | implicitDeny | implicitDeny |
| 8 | `iam:CreateUser` | `*` | implicitDeny | implicitDeny |
| 9 | `s3:ListAllMyBuckets` | `*` | implicitDeny | implicitDeny |
| 10 | `bedrock:InvokeModel` | `arn:aws:bedrock:us-east-1::foundation-model/amazon.titan-embed-text-v2:0` | allowed | allowed |
| 11 | `bedrock:InvokeModel` | `arn:aws:bedrock:us-east-1::foundation-model/amazon.titan-embed-text-v1` | implicitDeny | implicitDeny |
| 12 | `bedrock:InvokeModelWithResponseStream` | `arn:aws:bedrock:us-east-1::foundation-model/amazon.titan-embed-text-v2:0` | implicitDeny | implicitDeny |
| 13 | `bedrock:InvokeModel` | `arn:aws:bedrock:us-west-2::foundation-model/amazon.titan-embed-text-v2:0` | implicitDeny | implicitDeny |

Rows 10–13 (Titan, added 2026-11-21 for the RAG provider swap) were run with `simulate-custom-policy` against the file, since the emulator had been restarted and its IAM user was gone. Rows 1–9 were re-run the same way that day, with the same results. Row 12 is why Titan has its own statement: in the chat statement it would also get `InvokeModelWithResponseStream`.

The emulator doesn't check permissions on calls: `user/emulator` has no policies and can call everything. The simulator is the only real verdict here; on the real account the proof is one call as `ai-lab-dev` refused with AccessDenied.

To check a policy file before creating it, use `simulate-custom-policy` with `--policy-input-list "$(jq -Rs '[.]' bedrock-invoke-policy.json)"`: `file://` splits a list parameter into many items (`InvalidInput: Policy input list item 1 has invalid content`).

## Into the MEAI day

What the Bedrock + MEAI day starts from:

| Where | `Chat:Model` | Region | Credentials |
|---|---|---|---|
| aws-emulator | `us.amazon.nova-lite-v1:0` | `us-east-1` | `~/.aws/emulator/ai-lab-kb.env` (the emulator's `user/emulator`) |
| Real account | `us.anthropic.claude-haiku-5-5` | `us-east-1` | named profile `ai-lab-dev` (attached policy `AiLabBedrockInvoke`) |

- **Checked 2026-11-20:** the emulator's `converse` and `ConverseStream` accept the profile `us.amazon.nova-lite-v1:0` (and the bare `amazon.nova-lite-v1:0`). The emulator runs no real model. How the console runs on it: `experiments.md` section 22 and the README's "aws-emulator: start, check, stop".
- **Haiku 5.5 on Bedrock thinks by default** and its thinking blocks are bound to the AWS account that produced them: replay earlier turns unchanged (the same lesson as the week 5 `/ask` repair).
- **The emulator doesn't check permissions:** an AccessDenied can only show up on the real account.

## On the real account

What exists today and what's still missing to run week 7's code on a real account (the JSON files here are request bodies, not proof of anything created): [real-account-gaps.md](real-account-gaps.md). The steps below, the re-embed runbook and week 8's Knowledge Base are scripted in [`aws/real-account/`](../../aws/real-account/README.md) (`TARGET=real ./run-all.sh`).

In this order. The budget comes before anything that costs money.

1. **Root user:** MFA on, no access keys. Create an admin identity with MFA and stop using root (no policy can limit root).
2. **Payment method:** confirm one. Third-party models such as Claude subscribe through AWS Marketplace.
3. **Budget first:** the Block 2 files with the real account ID and email (`sed` into `$TMPDIR`, as on the emulator). Confirm the verification email, or no alert arrives.
4. **Regions:** `aws bedrock get-inference-profile` for `us.anthropic.claude-haiku-5-5` and `us.amazon.nova-lite-v1:0`; note the foundation-model ARNs (one per region).
5. **Policy:** swap the Haiku 4.5 entry for Haiku 5.5's profile ARN plus a foundation-model ARN in each region from step 4 (`arn:aws:bedrock:::foundation-model/…` if using `global.`), and use the real account ID in the profile ARNs. Then `create-policy`, `create-user ai-lab-dev`, `attach-user-policy` (4.3; brace `${ACCOUNT_ID}` in zsh).
6. **Simulator:** re-run the 13 cases above with `psim`; every row must match again.
7. **First Claude call as the admin** (it has the Marketplace permissions `ai-lab-dev` doesn't), then wait for `get-foundation-model-availability --model-id anthropic.claude-haiku-5-5` → `AVAILABLE`. The FTU form (`anthropic-use-case.json`) only for a Claude model older than Opus 4.7.
8. **Credentials for `ai-lab-dev`** in a named profile. Short-lived IAM Identity Center credentials beat a long-lived access key.
9. **Proof as `ai-lab-dev`:** one `aws bedrock-runtime converse` per allowed model, and one `invoke-model` on Titan v2 (the command from "Re-embed the corpus on Bedrock" step 2). Then one call to a model not in the policy (e.g. Nova Pro) → expect `AccessDeniedException`. The emulator could never show this.
10. **Next day:** check Billing for the spend (budget data refreshes up to 3 times a day).

## Re-embed the corpus on Bedrock

D2 of week 7 Wednesday (plan `docs/plans/2026-11-18-week-7-rag-provider-swap.md`). It's ready but not run: aws-emulator serves no embedding inference (HTTP 424 `ModelErrorException`, `experiments.md` section 23). It runs on the real account, after "On the real account" above, in about 10 minutes.

What it builds: `AiLab.Rag` with `ASPNETCORE_ENVIRONMENT=Bedrock` (`appsettings.Bedrock.json`), Titan Text Embeddings v2 at 1024 dimensions into **its own collection** `corpus_titan_v2_1024`. The Ollama `corpus` is never written, so switching back is just running without the profile.

1. **The safety check reversed:** you must be on the real account as `ai-lab-dev`, with no emulator variables:
   ```bash
   unset AWS_ENDPOINT_URL AWS_CA_BUNDLE AWS_SHARED_CREDENTIALS_FILE AWS_CONFIG_FILE SSL_CERT_FILE
   export AWS_PROFILE=ai-lab-dev AWS_REGION=us-east-1
   aws sts get-caller-identity --query '[Account, Arn]' --output text   # your 12-digit account, user/ai-lab-dev; NOT 000000000000
   ```
   The SDK copies `AWS_ENDPOINT_URL` into `ServiceURL` (section 22), so a terminal that once sourced `ai-lab-kb.env` would quietly reach the emulator. Use a fresh terminal.
2. **One embedding by hand** (also step 9's Titan proof):
   ```bash
   aws bedrock-runtime invoke-model --model-id amazon.titan-embed-text-v2:0 \
     --body '{"inputText":"hello","dimensions":1024}' --cli-binary-format raw-in-base64-out "$TMPDIR/emb.json"
   jq '{n: (.embedding | length), tokens: .inputTextTokenCount}' "$TMPDIR/emb.json"   # n: 1024
   ```
   AccessDenied means the policy's second statement is missing or not attached.
3. **Start the host** (Postgres up, `docker compose up -d`; the connection string comes from user-secrets in this environment too):
   ```bash
   dotnet run --project AiLab.Rag --launch-profile bedrock
   ```
   The startup line must read `Environment Bedrock: vector store postgres/corpus_titan_v2_1024, 1024 dims; embeddings bedrock/amazon.titan-embed-text-v2:0; chat bedrock/…`. For Claude instead of Nova Lite, add `Chat__Model=us.anthropic.claude-haiku-5-5` (after the Marketplace step).
4. **Ingest once:** `curl -s -X POST localhost:5074/dev/ingest | jq '.totals'`. Record per chunker: chunks, embedded, embedding tokens, elapsed. Expect 71 chunks (heading 50, fixed 21), so 71 `InvokeModel` calls, one at a time (the generator doesn't batch). A 502 `embedding_unavailable` names the keys to check; ingesting again carries on where it stopped (content hashes).
5. **Counts:** `corpus_titan_v2_1024` must have as many chunks per chunker as `corpus` (the `lines` seed only if you also ran `/dev/seed`):
   ```bash
   docker exec ai-labs-vectors-1 psql -U ailab -d ailab -c 'select "Chunker", count(*) from corpus_titan_v2_1024 group by 1 order by 1'
   ```
6. **Retrieval eval, nomic vs Titan:** `AiLab.Evals`' `RetrievalEvalTests` (no Anthropic key; it calls `GET /search` per mode) once against each setup. Its host (`EvalRagFactory`) forces `Development`, so `ASPNETCORE_ENVIRONMENT=Bedrock` doesn't reach it: select Titan with environment variables instead. Reports are named by date and mode, so move the first run's files before the second:
   ```bash
   AILAB_EVAL=1 dotnet test AiLab.Evals --filter "FullyQualifiedName~AiLab.Evals.RetrievalEvalTests"   # nomic, corpus
   mkdir -p eval-runs/retrieval/nomic && mv eval-runs/retrieval/$(date +%F)-*.md eval-runs/retrieval/nomic/
   AILAB_EVAL=1 Embedding__Provider=bedrock Embedding__Model=amazon.titan-embed-text-v2:0 Embedding__Dimensions=1024 \
     Embedding__UseTaskPrefixes=false VectorStore__Collection=corpus_titan_v2_1024 Bedrock__Region=us-east-1 \
     dotnet test AiLab.Evals --filter "FullyQualifiedName~AiLab.Evals.RetrievalEvalTests"  # Titan
   ```
   The Titan run's `vector` test fails on "Vector ranks differ from Tuesday's": that check is pinned to nomic's ranks, so it's expected; the report is written before it. Compare hit@1, hit@5 and MRR. Each Titan run embeds the 25 questions × 3 modes (75 small calls).
7. **Re-tune `Ask:MaxDistance`:** 0.50 was tuned on nomic's distances. From the `Ask gate … best distance` lines in the `rag-` log (or `/search`'s `distance`) for answerable vs off-topic questions, pick the new gate and put it in `appsettings.Bedrock.json`.
8. **Cost:** embedding tokens from step 4 × Titan Text Embeddings v2's on-demand price on the Bedrock pricing page (check it that day; embeddings bill input tokens only). Add the price to `ModelPrices` if the app should report it.
9. **Record** in `experiments.md` (a new section): steps 2, 4, 5, 6, 7, 8.

