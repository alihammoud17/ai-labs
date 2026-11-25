# Weeks 7–8 on a real AWS account, as scripts

Everything AWS in weeks 7 and 8 ran by hand on aws-emulator, following the runbooks. These scripts replay that work in order, from an empty account to week 8 Tuesday's Knowledge Base evals. Each one runs against either target:

- `TARGET=emulator`: aws-emulator at `https://127.0.0.1:9297`, account `000000000000`. Use this to rehearse today. Start the emulator with `docker/bedrock-kb/bedrock-kb-emulator.sh`.
- `TARGET=real`: the real account, through two named profiles (`ai-lab-admin` and `ai-lab-dev`).

```bash
TARGET=real DRY_RUN=1 aws/real-account/run-all.sh      # print every command, run nothing (works before the account exists)
TARGET=real aws/real-account/run-all.sh                # 00 → 08, pausing before each step
TARGET=real aws/real-account/run-all.sh --from 05      # resume
TARGET=real aws/real-account/run-all.sh --only 07      # one step
TARGET=real aws/real-account/99-teardown.sh            # delete the KB resources (they bill while they exist)
```

`--yes` skips the pauses and answers yes to the billed-step prompts. The scripts are bash 3.2, which is macOS's own.

## The steps

| Script | Replays | Source runbook | Runs as | Billed (real account) |
|---|---|---|---|---|
| `00-preflight.sh` | tools, the target, and the account steps no script can do | `docker/aws-setup/README.md` "On the real account" 1, 2, 8 | admin | no |
| `01-budget.sh` | W7 Mon D1: `ai-lab-monthly`, 5 USD, alerts at 80% actual and 100% forecast | aws-setup README, Block 2 | admin | no |
| `02-model-access.sh` | W7 Mon D2: each inference profile's regions (saved for the policy), model availability, the first Claude call | aws-setup README "Model IDs", checklist 4, 7 | admin | no |
| `03-iam-invoke.sh` | W7 Mon D3 + Wed: `AiLabBedrockInvoke` rendered for the account, checked by the simulator as a file, then created, attached to `ai-lab-dev` and checked again | aws-setup README "Least-privilege check", checklist 5–6 | admin | no |
| `04-bedrock-chat.sh` | W7 Tue: `converse` per model as `ai-lab-dev`; Nova Pro must be refused | checklist 9 | ai-lab-dev | < $0.001 |
| `05-rag-titan.sh` | W7 Wed: one Titan embedding, `AiLab.Rag` on the `bedrock` profile, ingest into `corpus_titan_v2_1024`, chunk counts, retrieval nomic vs Titan | aws-setup README "Re-embed the corpus on Bedrock" 2–6 | ai-lab-dev | ~71 embedding calls |
| `06-bedrock-evals.sh` | W7 Thu: Nova Lite and Haiku 5.5 × 90 answers, gold check, no judge | `docs/evals/README.md` "Run the eval suite on Bedrock" 1–3 | ai-lab-dev | ≈ $0.07 |
| `07-knowledge-base.sh` | W8 Mon/Tue: S3 → S3 Vectors → narrowed service role → KB (Titan v2) → data source → ingestion; `ai-lab-dev` gets `Retrieve` / `RetrieveAndGenerate` on this KB only | `bedrock-kb-emulator.sh` steps 4–10, `real-account-gaps.md` "Week 8" 2–3 | admin | storage while it exists |
| `08-kb-evals.sh` | W8 Tue: KB retrieval, pgvector re-run the same day, KB answers × 90 | `real-account-gaps.md` "Week 8" 4–6 | ai-lab-dev | cents |
| `99-teardown.sh` | deletes 07's resources; with `--all`, also the policy, user and budget | — | admin | — |

## Safety rules (in `lib.sh`)

- **The target guard runs before any AWS call.**
  - `real`: refuses to start if the terminal has emulator variables (`AWS_ENDPOINT_URL`, `AWS_CA_BUNDLE`, `Bedrock__ServiceUrl`, …), because the SDK copies `AWS_ENDPOINT_URL` into `ServiceURL`. It also refuses account `000000000000`, and checks that the run steps really are `ai-lab-dev`.
  - `emulator`: the account must be `000000000000`.
- **Billed steps** print what they'll cost and wait for `y`. The emulator never asks.
- **State outside the repo:** `~/.aws/ai-lab/<target>.env` holds the account ID, the profile regions, the KB and data-source IDs and the bucket names. Your email (`BUDGET_EMAIL`) goes only to AWS.
- **No Anthropic calls:** every eval runs with `AILAB_EVAL_JUDGE=none`.
- **Re-runnable:** existing resources are reused, and IAM policy updates become a new default version.
- **Exact test filters:** a bare `Ask_set_v1` or `RetrievalEvalTests` also matches the week 8 KB tests, whose fixture throws without a KB ID.

## What stays manual

Done by you, and printed where it belongs:
- **Before 01:** root MFA and an admin identity, a payment method, and the two profiles (`ai-lab-dev`'s credentials can only exist after 03).
- **After 01:** confirm the budget email.
- **After 02:** the first Claude call as admin (Marketplace), then wait for `AVAILABLE`.
- **After 05:** re-tune `Ask:MaxDistance` for Titan, price the Titan tokens, and write an `experiments.md` section.
- **After 06:** the hand pass on the 12 judge rows. Then regenerate D2 and copy its Artificial Analysis section back.
- **After 08:**
  1. `real-account-gaps.md` step 7: Bedrock's refusal wording goes into `GoldCheck.AbstainPhrases`, then a re-score with `Write_goldcheck`.
  2. Step 8: the "KB on Bedrock" rows of `docs/evals/managed-vs-own-rag-2026-11-24.md`, then the rule.

## On the emulator (checked 2026-11-25)

`TARGET=emulator run-all.sh` ran end to end:
- **01–03:** the budget was created and read back. The simulator gave 15/15 expected verdicts on the file and as attached (week 7's 13 cases, with one row per Nova Lite region).
- **04:** both models answered with the emulator's notice. The AccessDenied check is skipped, because the emulator checks no permissions.
- **05:** stops at Titan's HTTP 424, an emulator limit.
- **06:** only E5's 3 gated runs are stored per model, because Nova Lite answers `{}` and Haiku 5.5 isn't in the emulator's catalog.
- **07:** reused the KB, 4 scanned and 0 failed; the simulator gave 18/18 with the KB statement.
- **08:** reproduced week 8 exactly (KB 7/24, answers 3/16, pgvector unchanged).

The emulator's eval executions are named `emulator-bedrock-*` so they never mix with the real `bedrock-*` folders. Delete them when done.

## Keeping it current

After any new day of emulator work, run `/sync-aws-scripts` in Claude Code (`.claude/skills/sync-aws-scripts/SKILL.md`). It turns that day's commands into a new or updated numbered script here.
