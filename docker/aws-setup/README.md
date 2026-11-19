# AWS setup (rehearsed on aws-emulator)

Week 7 Monday's AWS setup, rehearsed on aws-emulator (`https://127.0.0.1:9297`, account `000000000000`). The files are reused on the real account; only the account ID, the email and the Claude model / region list change.

Plan: `docs/plans/2026-11-16-week-7-aws-setup-emulator.md`.

| File | What it is |
|---|---|
| `budget.json` | `ai-lab-monthly`: a 5 USD monthly cost budget |
| `budget-notifications.json` | Actual > 80% and forecasted > 100%, one email subscriber each (`BUDGET_EMAIL` placeholder, filled with `sed` into `$TMPDIR`, never in git) |
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

### `Chat:Model` for the MEAI day

| Where | `Chat:Model` | Note |
|---|---|---|
| aws-emulator | `us.amazon.nova-lite-v1:0` | The emulator runs no real model. Not checked yet: whether its `converse` accepts the profile ID (fallback: `amazon.nova-lite-v1:0`) |
| Real account | `us.anthropic.claude-haiku-5-5` | Region `us-east-1`; credentials from the `ai-lab-dev` profile (Block 4) |
