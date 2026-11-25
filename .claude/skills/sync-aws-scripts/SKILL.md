---
name: sync-aws-scripts
description: Brings aws/real-account/ (the scripts that replay the emulator work on a real AWS account) up to date with new aws-emulator work. Use after any day that ran AWS CLI or Bedrock steps on aws-emulator, or when the user asks to update or create the real-account scripts.
---

# Sync the real-account scripts with the emulator work

`aws/real-account/` replays, in order, everything done on aws-emulator so that it can run on a real account. Each new emulator day must land there as a new or updated numbered script. Otherwise the replay silently stops at an old week.

## 1. Find what's new

- **Covered so far:** the step table in `aws/real-account/README.md`. Its last row is the newest covered day.
- **What was done since:**
  - `git log --oneline` since the last commit that touched `aws/real-account/`;
  - plans in `docs/plans/` dated after it;
  - `experiments.md`'s newest sections;
  - the runbooks: `docker/aws-setup/README.md`, `docker/aws-setup/real-account-gaps.md`, `docs/evals/README.md` "Run the eval suite on Bedrock", `docker/bedrock-kb/bedrock-kb-emulator.sh`.
- **List each new emulator step:** the command, what it creates, which identity runs it, whether it's billed on a real account, and what the emulator can't do (no model, no embeddings, no permission checks, one cut-off passage per document).
- **Nothing new:** if nothing is new, say so and stop.

## 2. Map each step onto a script

- **Same theme as an existing script:** extend that script. For example, a new IAM statement goes into `policy_render` + `simulator_cases` in `lib.sh` (and runs through 03 / 07), and a new eval goes into the matching eval script.
- **A new day or theme:** a new script with the next free number before `99`, named `NN-<topic>.sh`.
  - `run-all.sh` picks up `0[0-9]-*.sh`. A script numbered 10 or higher means widening that glob.
- **New resources:** add their deletion to `99-teardown.sh`.

## 3. Keep the rules every script follows

- **The first line** is `source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"`. Then call `use_identity admin` for setup and `use_identity dev` for runs, before any AWS call.
- **Targets:** both work. A step the emulator can't do goes through `emulator_limit "<why>"`; on a real account the same failure is a `die`.
- **Running commands:**
  - Anything with effects goes through `run`, and anything whose output is needed goes through `cap NAME`, so `DRY_RUN=1` prints it.
  - Never pipe `run` into something that hides its dry-run line.
- **Money:** a billed step calls `confirm_billed "<what>" "<expected cost>"` first, with the cost taken from the runbook.
- **State:** IDs and names go through `state_set` / `state_get` (`~/.aws/ai-lab/<target>.env`). Never an account ID, email or key in the repo.
- **Evals:** `AILAB_EVAL_JUDGE=none` always. Exact `--filter` values (`FullyQualifiedName~AiLab.Evals.<Class>.<Method>`): bare names match the KB tests too. Emulator eval executions get an `emulator-` prefix.
- **Header comment:** which week/day it replays and which runbook section. Steps that stay manual go through `manual "…"`.
- **bash 3.2:** no associative arrays, no `${x,,}`, no `mapfile`, no empty `"${arr[@]}"` under `set -u`.

## 4. Check

```bash
cd aws/real-account
bash -n *.sh && shellcheck -x -e SC1091 *.sh
TARGET=real DRY_RUN=1 ./run-all.sh --yes          # every command printed, placeholders like <ACCOUNT_ID> filled
TARGET=emulator ./run-all.sh --yes --only NN      # each new or changed script, live, emulator up (safety check SAFE)
```

- **Guard check:** `TARGET=real` in a terminal that sourced `~/.aws/emulator/ai-lab-kb.env` must print STOP.
- **Before a live emulator run that writes `eval-runs/` files with today's date:** back up any you'd overwrite, then restore them.
- **If an emulator run re-creates the KB:** say so first. Restarting the emulator wipes it.

## 5. Record

- **Folder README:** update the step table in `aws/real-account/README.md`, plus "What stays manual" and "On the emulator" with the date and results.
- **Runbooks:** when a runbook step is now scripted, point to the script from that runbook section.
- **Commit:** one commit per script group, on a branch (not `main`).
