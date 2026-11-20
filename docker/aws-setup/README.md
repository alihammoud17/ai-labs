# AWS setup (rehearsed on aws-emulator)

Week 7 Monday's AWS setup, rehearsed on aws-emulator (`https://127.0.0.1:9297`, account `000000000000`). The files are reused on the real account; only the account ID, the email and the Claude model / region list change.

Plan: `docs/plans/2026-11-16-week-7-aws-setup-emulator.md`.

| File | What it is |
|---|---|
| `budget.json` | `ai-lab-monthly`: a 5 USD monthly cost budget |
| `budget-notifications.json` | Actual > 80% and forecasted > 100%, one email subscriber each (`BUDGET_EMAIL` placeholder, filled with `sed` into `$TMPDIR`, never in git) |
| `bedrock-invoke-policy.json` | `AiLabBedrockInvoke`: `bedrock:InvokeModel` + `InvokeModelWithResponseStream` on the Nova Lite profile, Nova Lite in its 3 regions and Haiku 4.5 (emulator). No wildcards. Attached to the user `ai-lab-dev` |
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

In this order. The budget comes before anything that costs money.

1. **Root user:** MFA on, no access keys. Create an admin identity with MFA and stop using root (no policy can limit root).
2. **Payment method:** confirm one. Third-party models such as Claude subscribe through AWS Marketplace.
3. **Budget first:** the Block 2 files with the real account ID and email (`sed` into `$TMPDIR`, as on the emulator). Confirm the verification email, or no alert arrives.
4. **Regions:** `aws bedrock get-inference-profile` for `us.anthropic.claude-haiku-5-5` and `us.amazon.nova-lite-v1:0`; note the foundation-model ARNs (one per region).
5. **Policy:** swap the Haiku 4.5 entry for Haiku 5.5's profile ARN plus a foundation-model ARN in each region from step 4 (`arn:aws:bedrock:::foundation-model/…` if using `global.`), and use the real account ID in the profile ARNs. Then `create-policy`, `create-user ai-lab-dev`, `attach-user-policy` (4.3; brace `${ACCOUNT_ID}` in zsh).
6. **Simulator:** re-run the 9 cases above with `psim`; every row must match again.
7. **First Claude call as the admin** (it has the Marketplace permissions `ai-lab-dev` doesn't), then wait for `get-foundation-model-availability --model-id anthropic.claude-haiku-5-5` → `AVAILABLE`. The FTU form (`anthropic-use-case.json`) only for a Claude model older than Opus 4.7.
8. **Credentials for `ai-lab-dev`** in a named profile. Short-lived IAM Identity Center credentials beat a long-lived access key.
9. **Proof as `ai-lab-dev`:** one `aws bedrock-runtime converse` per allowed model, then one to a model not in the policy (e.g. Nova Pro) → expect `AccessDeniedException`. The emulator could never show this.
10. **Next day:** check Billing for the spend (budget data refreshes up to 3 times a day).

