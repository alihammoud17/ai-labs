# AWS Setup Day Plan: Get AWS Ready Safely (rehearsed on aws-emulator)

- **Date:** Mon 16 Nov 2026 (done later, on the emulator)
- **Week theme:** Bedrock via MEAI
- **Branch:** `stage-7-aws-setup` (from `main`), not created yet. The plan is untracked on `main`: create the branch before the first commit.
- **Status:** not started. Reviewed 2026-11-19 against the repo, the emulator and the AWS docs; refinements R1–R12 applied.
- **Focus:** Get AWS ready safely: budget alarm, model access, least-privilege IAM for Bedrock invoke
- **Where:** ai-labs: the JSON files in a new folder `docker/aws-setup/`, so they can be reused on the real account

Tutor mode: I type the commands and write the files; Claude explains each step, reviews it and ticks the boxes. Claude writes a step's content only when I say "hand it over".

After each finished step, Claude adds a short **How we did it** note under it when I ask.

## Where things stand

| Block | Status |
|---|---|
| 1. Concepts | **done** (notes handed over, approved 2026-11-19) |
| 2. Budget alarm rehearsal (D1) | not done |
| 3. Find the model IDs (D2) | not done |
| 4. Least-privilege IAM policy (D3) | not done |
| 5. Housekeeping | not done |

**Order:** 1 → 2 → 3 → 4 → 5, the same order as on the real account (budget first). Block 2 is about 20 minutes.

## Deliverables (adapted: no AWS account yet)
- [x] **D1. Budget alarm, rehearsed:** a monthly cost budget with two email alerts, created on the emulator from JSON files that need only the account ID and an email on the real account
- [x] **D2. Model access, rehearsed:** the emulator model IDs and ARNs (Nova Lite through its profile, Haiku 4.5) and the real target (Haiku 5.5 through `us.anthropic.claude-haiku-5-5`) recorded; the Anthropic first-time-use form submitted on the emulator, with a before/after availability check
- [ ] **D3. Least-privilege IAM policy for Bedrock invoke:** written, checked offline, created, attached to a test user and proven with the IAM policy simulator (one case per call, results in a table)
- ~~AIF-C01 booking~~: skipped for now (needs only an AWS Builder ID, not an account)

## Resources
- AWS .NET blog, Integrating Amazon Bedrock in .NET: https://aws.amazon.com/blogs/dotnet/getting-started-with-amazon-bedrock-in-net-applications/
- Bedrock User Guide, Access Amazon Bedrock foundation models: https://docs.aws.amazon.com/bedrock/latest/userguide/model-access.html
- Bedrock User Guide, Prerequisites for inference profiles: https://docs.aws.amazon.com/bedrock/latest/userguide/inference-profiles-prereq.html
- Bedrock User Guide, Inference using Converse API: https://docs.aws.amazon.com/bedrock/latest/userguide/conversation-inference.html
- Bedrock model card, Claude Haiku 5.5: https://docs.aws.amazon.com/bedrock/latest/userguide/model-card-anthropic-claude-haiku-5-5.html
- AWS Budgets, email recipients and verification: https://docs.aws.amazon.com/cost-management/latest/userguide/budgets-email-recipients.html
- AWS Certified AI Practitioner (exam guide + prep): https://aws.amazon.com/certification/certified-ai-practitioner/

## Scope rules for today
- **No AWS account.** Everything runs on aws-emulator (`https://127.0.0.1:9297`, account `000000000000`). The files in `docker/aws-setup/` are reused on the real account with **only the account ID, the email and the Claude model / region list** changed (the emulator has no Haiku 5.5 and no Claude 4+ inference profiles).
- **Safety check, every terminal session:** `source ~/.aws/emulator/ai-lab-kb.env`, then `[ "$(aws sts get-caller-identity --query Account --output text)" = 000000000000 ] && echo SAFE || echo STOP`. Anything but `SAFE`: stop.
- **The emulator doesn't check permissions on calls.** Its own `user/emulator` has no policies and can call everything (checked 2026-11-19). So the IAM proof is the **policy simulator**, which gives real verdicts (exact allow, wildcards and explicit Deny checked 2026-11-19).
- **Budgets on the emulator never fire.** There is no real spending and no email. Today rehearses the setup, not the alert.
- **No personal data in git:** the budget email and the use-case form's details stay placeholders in the committed files.
- **Region:** `us-east-1` throughout (same as the KB lab).
- No .NET code today. Calling Bedrock from MEAI is later this week; today's README hands over the `Chat:Model` values and the credentials profile name.

### What the emulator showed in the review (2026-11-19)

| Check | Result |
|---|---|
| Models | Nova Lite / Micro / Pro; Claude Haiku 4.5, Sonnet and Opus up to 5.5. **No Haiku 5.5.** |
| Haiku 4.5 `inferenceTypesSupported` | `ON_DEMAND`, which is **wrong vs real AWS** (profile only there) |
| Inference profiles | 15, an old static list: Nova (us-east-1, us-east-2, us-west-2) and Claude 3.x. **None for Claude 4+.** |
| Use-case form | `get` → `ResourceNotFoundException` (not submitted). `put` not tried (it saves state and has no delete). |
| Model availability | Haiku 4.5 `NOT_AVAILABLE` / `NOT_AUTHORIZED`, Nova Lite `AVAILABLE` |
| `converse` | Canned "No language model runs in this emulator…" text plus usage; Haiku 4.5 answers **without the form** (not enforced) |
| Simulator | Real verdicts. With several `--resource-arns`, the top-level `EvalDecision` is `implicitDeny` if any one is denied: read `ResourceSpecificResults` or use one resource per call |
| Budgets | `describe-budgets` works (empty); `create-budget` not tried. Cost Explorer `NotImplemented` |

---

## Block 1: Concepts (15 min) — done

- [x] **Model access changed in Oct 2025.**
  - **What to do:** note that serverless foundation models are enabled by default. The one-time Anthropic "first-time use" (FTU) form is needed only for Anthropic models **older than Claude Opus 4.7** (e.g. Haiku 4.5, Sonnet 4.6, Claude 3.x), once per account or organization, and not on the `bedrock-mantle` endpoint. **Haiku 5.5 doesn't need it.**
  - **Also note:** the first call to a third-party model (any Claude) starts an AWS Marketplace subscription. The identity making that call needs `aws-marketplace:Subscribe`, `aws-marketplace:Unsubscribe` and `aws-marketplace:ViewSubscriptions`, and the account needs a valid payment method. Access can sit in `PENDING` for a while (calls return AccessDenied); `get-foundation-model-availability` shows the state.
  - **Why:** the roadmap item "enable model access" is mostly gone. The real trap is a least-privilege user making the first Claude call and failing.
  - **Done (handed over):** see Block 1 notes, section 1, at the end of this plan: default access, the FTU form only before Opus 4.7, the Marketplace subscription and its prerequisites, `PENDING` and the availability check.
- [x] **Which IAM action each call needs.**
  - **What to do:** write the table in your notes: `Converse` / `InvokeModel` → `bedrock:InvokeModel`; `ConverseStream` / `InvokeModelWithResponseStream` → `bedrock:InvokeModelWithResponseStream`.
  - **Why:** there is no `bedrock:Converse` permission to grant. The MEAI Bedrock client calls Converse, and ConverseStream when streaming (the console streams), so your app needs both actions. (Checked: `AWSSDK.Extensions.Bedrock.MEAI` 4.0.101.8 references `ConverseAsync` and `ConverseStreamAsync`.)
  - **Done (handed over):** see Block 1 notes, section 2: the four calls and their two actions, the MEAI package check, and why streaming is tested from C#.
- [x] **Model ARN shapes.**
  - **What to do:** note the three shapes: foundation model `arn:aws:bedrock:<region>::foundation-model/<model-id>` (no account ID); inference profile `arn:aws:bedrock:<region>:<account>:inference-profile/<profile-id>`; and for a **global** profile, the foundation model with no region, `arn:aws:bedrock:::foundation-model/<model-id>`.
  - **Why:** newer Claude models are called through a cross-region inference profile (`us.anthropic…`). The policy must allow the profile **and** the foundation model in every region the profile routes to, or the call fails with AccessDenied. Reading a profile (`bedrock:GetInferenceProfile`) is **not** needed to invoke through it.
  - **Done (handed over):** see Block 1 notes, section 3: four ARN shapes, the every-region rule, profile-only Claude models, optional hardening.
- [x] **Budgets vs billing alarms.**
  - **What to do:** note that an AWS Budget watches cost (actual or forecasted) against a limit and emails at thresholds. Budget data is updated up to 3 times a day (8–12 h apart), so alerts lag. New email recipients must confirm a verification email or they get nothing.
  - **Why:** the budget is the first thing to create on the real account, before any model call.
  - **Done (handed over):** see Block 1 notes, section 4: alert types and limits, refresh lag, email verification, daily budgets without forecasts, the CloudWatch billing alarm (from memory), what the emulator can show.

---

## Block 2: Budget alarm rehearsal (20 min) → D1 — not done

### 2.1 Start the emulator and check where you are
- [x] `docker ps --format '{{.Names}} {{.Status}}'`: if `aws-emulator` isn't listed, run `docker/bedrock-kb/bedrock-kb-emulator.sh` (it starts the container with the right flags, waits for health, refuses any account but `000000000000`, and rebuilds the KB, which does no harm).
  - **Why:** reuses Tuesday's script instead of a hand-typed `docker run` without `--telemetry off`, `--memory-swap` or the health wait.
- [x] Run the safety check from the scope rules, then `ACCOUNT_ID=000000000000`.
  - **Why:** a hard stop, not a check by eye. `ACCOUNT_ID` keeps the account out of the commands, so they run unchanged on the real account.
- [x] `git switch -c stage-7-aws-setup` and `mkdir -p docker/aws-setup`.

### 2.2 Write the two files
- [x] `docker/aws-setup/budget.json`: `BudgetName` `ai-lab-monthly`, `BudgetType` `COST`, `TimeUnit` `MONTHLY`, `BudgetLimit` with `Amount` `"5"` (a string) and `Unit` `USD`.
- [x] `docker/aws-setup/budget-notifications.json`: a JSON array of two items, each a `Notification` plus `Subscribers`. **Actual > 80%** and **forecasted > 100%** (`ComparisonOperator` `GREATER_THAN`, `ThresholdType` `PERCENTAGE`). One `EMAIL` subscriber each, with `Address` the placeholder `BUDGET_EMAIL`.
  - **Why:** actual catches spend that already happened; forecasted warns before the month ends over the limit. The placeholder keeps your email out of git.
  - **Done (handed over):** item 1 was missing `NotificationType` (required by the API, `[required]` in `aws budgets create-budget help`), so it is now `ACTUAL`; its `Address` was `user@example.com`, now the `BUDGET_EMAIL` placeholder so 2.3's `sed` finds it. Item 2 added: `FORECASTED`, `GREATER_THAN` 100 `PERCENTAGE`, same subscriber. Valid JSON (`python3 -m json.tool`).

### 2.3 Create and check it
- [x] Fill in the email outside the repo: `sed "s/BUDGET_EMAIL/<your email>/" docker/aws-setup/budget-notifications.json > "$TMPDIR/budget-notifications.json"`. `git diff` must stay empty.
- [x] Run:
```bash
  aws budgets create-budget --account-id "$ACCOUNT_ID" \
    --budget file://docker/aws-setup/budget.json \
    --notifications-with-subscribers file://"$TMPDIR/budget-notifications.json"
  aws budgets describe-budgets --account-id "$ACCOUNT_ID"
  aws budgets describe-notifications-for-budget --account-id "$ACCOUNT_ID" --budget-name ai-lab-monthly
```
  - **Look for:** 5 USD, MONTHLY, COST, and two notifications.
  - **Why:** proves the files were understood, not only accepted. On the real account the same files and command take a minute.
- [x] Run `create-budget` again and note what the emulator does (real AWS: `DuplicateRecordException`).
  - **Why:** tells you whether a re-run is safe.

**Done when:** `describe-budgets` shows `ai-lab-monthly` at 5 USD and `describe-notifications-for-budget` lists ACTUAL 80% and FORECASTED 100%.

---

## Block 3: Find the model IDs (30 min) → D2 — done

Two tracks: the **emulator set** (what exists there today) and the **real target** (from the AWS docs, since the emulator's catalog is old).

| Track | Nova | Claude |
|---|---|---|
| Emulator | `amazon.nova-lite-v1:0` through profile `us.amazon.nova-lite-v1:0` (3 regions) | `anthropic.claude-haiku-4-5-20251001-v1:0` by its bare model ID (no profile exists there) |
| Real account | the same Nova profile | `anthropic.claude-haiku-5-5` through `us.anthropic.claude-haiku-5-5` (regions read on the real account), no FTU form |

### 3.1 List the models
- [x] `aws bedrock list-foundation-models --by-provider amazon --query "modelSummaries[].[modelId,join(',',inferenceTypesSupported)]" --output text`, and the same with `--by-provider anthropic`.
- [x] `aws bedrock get-foundation-model --model-identifier <id>` for Nova Lite and Haiku 4.5.
  - **What to do:** look at `inferenceTypesSupported` and `responseStreamingSupported`. Note that the emulator says Haiku 4.5 is `ON_DEMAND`, while real AWS needs a profile for it.
  - **Why:** the IAM policy names exact model IDs; a typo here is the most common cause of AccessDenied.

### 3.2 Inference profiles
- [x] `aws bedrock get-inference-profile --inference-profile-identifier us.amazon.nova-lite-v1:0 --query "{arn:inferenceProfileArn,models:models[].modelArn}"`
  - **What to do:** note the profile ARN (with the account) and the three foundation-model ARNs (us-east-1, us-east-2, us-west-2).
  - **Why:** these are the ARNs Block 4 must allow; the "profile + every region" rule can be rehearsed fully with Nova.
- [x] Try `get-inference-profile` for `us.anthropic.claude-haiku-4-5-20251001-v1:0`; expect `ResourceNotFoundException` and note it as an emulator gap.
- [x] Write the real target from the Haiku 5.5 model card: model ID `anthropic.claude-haiku-5-5`, profile `us.anthropic.claude-haiku-5-5`, regions "read with `get-inference-profile` on the real account".
  - **Done (handed over):** re-ran the Haiku 4.5 profile check (`ResourceNotFoundException: Inference profile not found`; the emulator's only Anthropic profiles are Claude 3 / 3.5) and wrote the real target from the AWS model card into "Block 3 notes" §2–3 below: model ID `anthropic.claude-haiku-5-5`, geo profile `us.anthropic.claude-haiku-5-5` (also `global.`), regions left for `get-inference-profile` on the real account.

### 3.3 Anthropic first-time-use form (rehearsal; on the real account only for a Claude model older than Opus 4.7)
- [x] Before: `aws bedrock get-use-case-for-model-access` (expect `ResourceNotFoundException`) and `aws bedrock get-foundation-model-availability --model-id anthropic.claude-haiku-4-5-20251001-v1:0` (expect `NOT_AVAILABLE` / `NOT_AUTHORIZED`).
- [x] Write `docker/aws-setup/anthropic-use-case.json` with the six fields from the docs: `companyName`, `companyWebsite` (a GitHub profile is accepted), `intendedUsers` (`"0"` internal, `"1"` external, `"2"` both), `industryOption`, `otherIndustryOption`, `useCases` (max 8192 characters; the others max 128). Placeholders in the committed file.
- [x] Submit: `aws bedrock put-use-case-for-model-access --form-data fileb://docker/aws-setup/anthropic-use-case.json` (`fileb://`, not `file://`: the field is a blob). Note whether the emulator checks the fields (untested).
- [x] After: both checks again. Note whether availability changed.
  - **Why:** on the real account, `PENDING` → `AVAILABLE` from `get-foundation-model-availability` is how you know access went through.

> Result (2026-11-19):
> - **Before:** `get-use-case-for-model-access` → `ResourceNotFoundException`. Availability for Haiku 4.5: agreement `NOT_AVAILABLE`, `NOT_AUTHORIZED`, entitlement `NOT_AVAILABLE`, region `AVAILABLE`.
> - **Submit:** no output, which means success (an empty response, like `create-budget`). The file has placeholders only (Acme Corp, example.com, `intendedUsers` `"0"`).
> - **After:** `get-use-case-for-model-access` returns `formData` as base64. Decoded, it's byte-identical to the file (`base64 -d | cmp`), so `fileb://` sent the raw bytes. Availability **unchanged**: the emulator stores the form but doesn't link it to access (real AWS: `PENDING` → `AVAILABLE`). Field checks not tested (no bad-value run).

### 3.4 Record
- [x] `docker/aws-setup/README.md` → **Model IDs**: model, emulator ID, real ID / profile, ARNs, "FTU form needed?". Plus the `Chat:Model` value for the MEAI day: Nova Lite on the emulator, `us.anthropic.claude-haiku-5-5` on the real account.
  - **Done (handed over):** `docker/aws-setup/README.md` created: what each file is, the safety check, the **Model IDs** table (Nova Lite, Haiku 4.5 emulator only, Haiku 5.5 real target: emulator ID, real ID / profile, IAM ARNs, FTU form) from the Block 3 notes, the "profile + every region" rule, and `Chat:Model` (emulator `us.amazon.nova-lite-v1:0`, real `us.anthropic.claude-haiku-5-5`). Not checked: whether the emulator's `converse` accepts the profile ID. Block 5 adds the simulator table and the real-account checklist.

**Done when:** the README table is filled in and `get-use-case-for-model-access` returns your form.

---

## Block 4: Least-privilege IAM policy (60 min) → D3 — not done

### 4.1 Write the policy
- [x] Create `docker/aws-setup/bedrock-invoke-policy.json` with one statement (with a `Sid`):
  - Effect `Allow`; Actions exactly `bedrock:InvokeModel` and `bedrock:InvokeModelWithResponseStream`
  - Resources: the Nova Lite **profile** ARN, the three Nova Lite **foundation-model** ARNs from 3.2, and the Haiku 4.5 foundation-model ARN in us-east-1 (emulator only; note the real Haiku 5.5 entries to swap in)
  - No `bedrock:GetInferenceProfile` (not needed to invoke)
  - **Why least privilege:** the user your app runs as can call exactly the chosen models and nothing else: no Knowledge Base admin, no model customization, no other models with other prices.
- [x] No `*` in Actions, no `foundation-model/*` in Resources.
  - **Why:** a wildcard here is how a leaked key ends up running an expensive model.
  - **Done (handed over):** fixed three things in my draft: the foundation-model ARN used the profile ID (`foundation-model/us.amazon.nova-lite-v1:0`, which matches nothing; the model ID is `amazon.nova-lite-v1:0`); its region was `*` (every region) instead of the profile's three; the profile ARN was still a placeholder, and Haiku 4.5 was missing. Now: one statement, the 2 actions, 5 ARNs (Nova Lite profile in us-east-1 with account `000000000000`, Nova Lite in us-east-1 / us-east-2 / us-west-2, Haiku 4.5 in us-east-1). Valid JSON, no `*` anywhere; `accessanalyzer validate-policy` on the emulator gave no findings. Real account: swap the account ID, and swap Haiku 4.5 for the Haiku 5.5 profile ARN plus its foundation-model ARNs (README table). Stretch skipped.
- [ ] Optional stretch: a second statement that allows the foundation models only through the profile, with the `bedrock:InferenceProfileArn` condition key (see the inference-profile prerequisites page).

### 4.2 Check the file before creating anything
- [x] `aws iam simulate-custom-policy --policy-input-list file://docker/aws-setup/bedrock-invoke-policy.json --action-names <one action> --resource-arns <one ARN> --query 'EvaluationResults[0].EvalDecision'` for the cases in 4.4.
  - **Why:** checks the file on its own, costs nothing to repeat. One resource per call: with several, the top-level decision hides which one was denied.
  - **Found by me first:** I ran the command as written above, before handing 4.2 over, and got `aws: [ERROR]: An error occurred (InvalidInput) when calling the SimulateCustomPolicy operation: Policy input list item 1 has invalid content`. The error was in the plan's command, not in my policy file.
  - **Working command** (use this one; the `file://` form above never works for this parameter):
    ```bash
    sim() { aws iam simulate-custom-policy --policy-input-list "$(jq -Rs '[.]' docker/aws-setup/bedrock-invoke-policy.json)" \
      --action-names "$1" --resource-arns "${2:-*}" --query 'EvaluationResults[0].EvalDecision' --output text; }
    sim bedrock:InvokeModel "arn:aws:bedrock:us-east-1:000000000000:inference-profile/us.amazon.nova-lite-v1:0"
    ```
    `jq -R` reads the file as raw text, `-s` as one string, `[.]` wraps it in a list: one item, the whole policy. `${2:-*}` = the resource, or `*` when none is given.
  - **Rule:** `file://` reads a file as *one value*. That's right for single-string parameters (`create-policy --policy-document`, 4.3), but a list parameter like `--policy-input-list` gets split into many items. 4.4's `simulate-principal-policy` reads the attached policy, so it doesn't have this problem.
  - **Done (handed over):** the command as written fails: `--policy-input-list file://…` gives `InvalidInput: Policy input list item 1 has invalid content`. The parameter is a *list* of strings, and the CLI split the file into many items (`--debug`: `PolicyInputList.member.1` = `{`). Fix: send the file as a one-item JSON list, `--policy-input-list "$(jq -Rs '[.]' docker/aws-setup/bedrock-invoke-policy.json)"`. Run through the `sim` helper (action, resource, default `*`).

> Result (2026-11-19): 9 / 9 as expected. `allowed`: `InvokeModel` on the Nova Lite profile, on Nova Lite in us-east-2, on Haiku 4.5; `InvokeModelWithResponseStream` on the profile. `implicitDeny`: `InvokeModel` on Nova Pro, on Nova Lite in eu-west-1; `bedrock:CreateKnowledgeBase`, `iam:CreateUser`, `s3:ListAllMyBuckets` (resource `*`).

### 4.3 Create it and attach it
- [ ] Run:
```bash
  aws iam create-policy --policy-name AiLabBedrockInvoke --policy-document file://docker/aws-setup/bedrock-invoke-policy.json
  aws iam create-user --user-name ai-lab-dev
  aws iam attach-user-policy --user-name ai-lab-dev --policy-arn "arn:aws:iam::$ACCOUNT_ID:policy/AiLabBedrockInvoke"
  aws iam list-attached-user-policies --user-name ai-lab-dev
```
  - **Why:** on the real account the app runs as this user (or a role), never as root or an admin.

### 4.4 Prove it with the simulator
- [ ] `aws iam simulate-principal-policy --policy-source-arn "arn:aws:iam::$ACCOUNT_ID:user/ai-lab-dev" --action-names <one action> --resource-arns <one ARN> --query 'EvaluationResults[0].EvalDecision'`, one case per call:
  - **Expect `allowed`:** `InvokeModel` on the Nova profile; on the Nova Lite foundation model in us-east-2; on Haiku 4.5. `InvokeModelWithResponseStream` on the Nova profile.
  - **Expect `implicitDeny`:** `InvokeModel` on Nova Pro; on Nova Lite in eu-west-1; `bedrock:CreateKnowledgeBase`; `iam:CreateUser`; `s3:ListAllMyBuckets`.
- [ ] Save the results in `docker/aws-setup/README.md` as a table: action, resource, expected, actual. Add one line: "The emulator doesn't check permissions on calls: `user/emulator` has no policies and can call everything."
  - **Why:** the table is the evidence the policy is least-privilege. Re-run the same commands on the real account; the real proof there is one call refused with AccessDenied (checklist step 9).

**Done when:** every row has expected = actual, and `list-attached-user-policies` shows `AiLabBedrockInvoke`.

> Dropped from the first draft: a real call as the restricted user on the emulator. The review already answered it (no permission checks on calls), and it would need a long-lived access key for nothing.

---

## Block 5: Housekeeping (30 min) — not done

- [ ] `docker/aws-setup/README.md`: the Model IDs table, the simulator table, an **"Into the MEAI day"** note (`Chat:Model` values from 3.4, credentials profile name `ai-lab-dev`), and an **"On the real account"** checklist, in order:
  1. Root user: MFA on, no access keys. Create an admin identity with MFA and stop using root.
  2. Confirm a payment method (third-party models subscribe through AWS Marketplace).
  3. Budget first: Block 2 files with the real account ID and email. Confirm the verification email.
  4. `get-inference-profile` for `us.anthropic.claude-haiku-5-5` and `us.amazon.nova-lite-v1:0`; note their regions.
  5. Swap the policy's Claude entries for Haiku 5.5's profile ARN plus a foundation-model ARN in each region (`arn:aws:bedrock:::…` if using `global.`). Create the policy and `ai-lab-dev`, and attach it.
  6. Re-run the 4.4 simulator table.
  7. First Claude call as the admin (it has the Marketplace permissions); wait for `get-foundation-model-availability` → `AVAILABLE`. Submit the Block 3.3 form only for a Claude model older than Opus 4.7.
  8. Credentials for `ai-lab-dev` in a named profile (short-lived Identity Center credentials beat an access key).
  9. As `ai-lab-dev`: one `converse` call per allowed model, then one call to a model not in the policy → expect AccessDenied.
  10. Next day: check Billing for the spend.
  - **Why:** the day you create the account becomes a short checklist instead of a research day.
- [ ] Relabel check (R12, applied 2026-11-19 by Claude): Tuesday's managed RAG work is now Week 8 Monday, done early: `docs/plans/2026-11-23-week-8-managed-rag-bedrock.md` (moved with `git mv`), `experiments.md` heading and its table-of-contents link, README and CLAUDE.md wording. Review the diff and commit it with this branch.
- [ ] `CLAUDE.md`: fill in the Week 7 Monday entry (AWS setup rehearsed on the emulator, `docker/aws-setup/`) with results.
- [ ] `README.md`: one line under Project structure for `docker/aws-setup/`.
- [ ] `experiments.md` section 21: the emulator findings and the simulator table (expected vs actual).
- [ ] `git status`: no file holds your email or personal form details. Commit, open a PR.

---

## End of day
- [ ] D1–D3 checked
- [ ] `docker/bedrock-kb/bedrock-kb-emulator.sh stop` (wipes the emulator state, including the KB, the budget and the IAM user)
- [ ] Ask Claude for today's recap PDF (for OneDrive)

> **Short on time?** Keep the order 2 → 3 → 4 → 5. Drop the 4.1 stretch first, then the re-run check in 2.3.

---

## Block 1 notes (handed over 2026-11-19, approved)

Sources: Bedrock User Guide (model access, inference-profile prerequisites, Converse API, Claude Haiku 4.5 and 5.5 model cards), AWS Budgets docs (budget considerations, email recipients, API reference), the `AWSSDK.Extensions.Bedrock.MEAI` 4.0.101.8 package, and the emulator itself. Anything not checked is marked.

### 1. Model access (changed in Oct 2025)

- **Default on.** Serverless foundation models can be called right away in every commercial region where they exist. The console's **Model access** page now only matters for GovCloud.
- **The Anthropic first-time-use (FTU) form** is needed only for Claude models **older than Claude Opus 4.7** (Haiku 4.5, Sonnet 4.6, Opus 4.6, Claude 3.x). It's filled once per account, or once at the organization's management account, and again in opt-in regions. It isn't needed on the `bedrock-mantle` endpoint. **Haiku 5.5, our real target, doesn't need it.**
- **Marketplace subscription.** The first call to a third-party model (any Claude) starts an AWS Marketplace subscription in the background. It needs:
  - `aws-marketplace:Subscribe`, `aws-marketplace:Unsubscribe` and `aws-marketplace:ViewSubscriptions` on the identity making that call;
  - a valid payment method on the account.

  If either is missing, the subscription fails and later calls return `AccessDeniedException`. Amazon models (Nova) have no subscription step.
- **Calls can lie for a while.** Setup can take up to 15 minutes, and calls may succeed during it, then fail if a prerequisite is missing. After an FTU submission the agreement can sit in `PENDING`, and calls are refused until it's `AVAILABLE`.
- **The check:** `aws bedrock get-foundation-model-availability --model-id <id>` returns `agreementAvailability.status`: `AVAILABLE` / `PENDING` / `NOT_AVAILABLE` / `ERROR`.
- **Form fields** (base64 JSON; the CLI does the encoding with `fileb://`): `companyName`, `companyWebsite` (a GitHub profile is accepted), `intendedUsers` (`0` internal, `1` external, `2` both), `industryOption`, `otherIndustryOption`, `useCases` (max 8192 characters; the others max 128).
- **For today:** the first real Claude call is made by the admin, not by `ai-lab-dev`, so the subscription can start. The FTU form is a rehearsal for older models only.
- **Outdated source:** the AWS .NET blog in Resources still shows the old "Manage model access" page, Claude 3 and `AmazonBedrockFullAccess`. Use it only for the package list.

### 2. Which IAM action each call needs

| API call | IAM action |
|---|---|
| `Converse` | `bedrock:InvokeModel` |
| `InvokeModel` | `bedrock:InvokeModel` |
| `ConverseStream` | `bedrock:InvokeModelWithResponseStream` |
| `InvokeModelWithResponseStream` | `bedrock:InvokeModelWithResponseStream` |

- **There is no `bedrock:Converse` action.** Denying both actions above also blocks Converse and ConverseStream.
- **MEAI:** `AWSSDK.Extensions.Bedrock.MEAI` 4.0.101.8 references `ConverseAsync` and `ConverseStreamAsync` (the chat client) and `InvokeModelAsync`, probably the embedding generator; not checked in its source. The console streams, so the app needs **both** actions.
- **The CLI can't stream** (`InvokeModelWithResponseStream` isn't supported there), so on the real account the streaming permission is tested from C#, not the CLI.

### 3. Model ARN shapes

| What | ARN |
|---|---|
| Foundation model (no account) | `arn:aws:bedrock:<region>::foundation-model/<model-id>` |
| System inference profile (`us.…`, `global.…`) | `arn:aws:bedrock:<region>:<account>:inference-profile/<profile-id>` |
| Foundation model behind a **global** profile (no region, no account) | `arn:aws:bedrock:::foundation-model/<model-id>` |
| Application inference profile (one you create; not used today) | `arn:aws:bedrock:<region>:<account>:application-inference-profile/<id>` |

- **The rule:** calling through a profile needs the **profile ARN and the foundation-model ARN in every region the profile routes to**. Missing one region gives an AccessDenied that comes and goes, depending on where a request lands.
- **Profile only.** Haiku 4.5 has no bare model ID for on-demand calls on `bedrock-runtime`, only `us.` / `global.…-20251001-v1:0`. Haiku 5.5 (`anthropic.claude-haiku-5-5`) is also used through `us.` / `global.anthropic.claude-haiku-5-5`. The emulator wrongly lists Haiku 4.5 as `ON_DEMAND` and has no Claude 4+ profiles.
- **Reading a profile** (`bedrock:GetInferenceProfile`) isn't needed to invoke through it.
- **Optional hardening:** a condition on the foundation-model statement with `bedrock:InferenceProfileArn` lets the models be called only through that profile. An explicit Deny on `aws:RequestedRegion = unspecified` blocks global routing.
- **Regions:** the emulator's `us.amazon.nova-lite-v1:0` routes to us-east-1, us-east-2 and us-west-2. The real `us.anthropic.claude-haiku-5-5` region list wasn't found in the docs: read it with `get-inference-profile` on the real account.

### 4. Budgets vs billing alarms

- **An AWS Budget** watches cost (or usage) for a period against a limit and notifies at thresholds.
  - **Alert types:** `ACTUAL` (already spent) or `FORECASTED` (on track to exceed).
  - **Limits:** up to 10 notifications per budget, each with up to 10 email subscribers plus 1 SNS topic.
  - **Thresholds:** a percentage of the limit, or an absolute amount.
- **Not real time.** Budget data refreshes up to 3 times a day, 8–12 h apart, and billing lags usage. You can be over the threshold before the email arrives. A budget warns; it doesn't stop spending.
- **Email verification.** New email recipients get a verification email (AWS User Notifications), and only verified addresses receive alerts. On the real account, confirm it the same day.
- **Daily budgets** have no forecasted alerts. Use `MONTHLY` for the forecast alert.
- **Not checked:** I believe forecasts need some spending history before `FORECASTED` alerts work, so expect the forecast alert to stay quiet in a new account's first weeks.
- **Billing alarm (from memory, not checked today):** a CloudWatch alarm on the `EstimatedCharges` metric, which exists only in us-east-1 once "Receive billing alerts" is turned on in the billing preferences. It's coarser and older; the budget is the main guard.
- **For today:** the emulator stores the budget but never evaluates it (no spending, no email), and Cost Explorer is `NotImplemented`. D1 proves the file shapes and the command, nothing more.


## Block 3 notes

### 1. Listing the models

- **Amazon** provider: 
  | Model ID | Inferences Types Supported |
  |---|---|
  | amazon.nova-2-5-sonic | ON_DEMAND  |
  | amazon.nova-2-lite-v1:0 |ON_DEMAND |
  | amazon.nova-2-sonic-v1:0     |   ON_DEMAND |
  | amazon.nova-lite-v1:0  | ON_DEMAND,PROVISIONED |
  | amazon.nova-micro-v1:0 | ON_DEMAND,PROVISIONED |
  | amazon.nova-pro-v1:0  |  ON_DEMAND,PROVISIONED |
  | amazon.rerank-v1:0   |   ON_DEMAND |
  | amazon.titan-embed-g1-text-02  | ON_DEMAND,PROVISIONED |
  | amazon.titan-embed-image-v1   |  ON_DEMAND,PROVISIONED |
  | amazon.titan-embed-image-v1:0  | ON_DEMAND,PROVISIONED |
  | amazon.titan-embed-text-v1  |    ON_DEMAND,PROVISIONED |
  | amazon.titan-embed-text-v1:2:8k |ON_DEMAND,PROVISIONED |
  | amazon.titan-embed-text-v2:0   | ON_DEMAND,PROVISIONED |

- **Anthropic** models:
  | Model ID | Inferences Types Supported |
  |---|---|
  | anthropic.claude-fable-5     |   ON_DEMAND |
  | anthropic.claude-fable-5-1    |  ON_DEMAND|
  | anthropic.claude-haiku-4-5-20251001-v1:0     |   ON_DEMAND|
  | anthropic.claude-opus-4-1-20250805-v1:0 |ON_DEMAND|
  | anthropic.claude-opus-4-5-20251101-v1:0 |ON_DEMAND|
  | anthropic.claude-opus-4-6-v1   | ON_DEMAND|
  | anthropic.claude-opus-4-7    |   ON_DEMAND|
  | anthropic.claude-opus-4-8     |  ON_DEMAND|
  | anthropic.claude-opus-5 | ON_DEMAND|
  | anthropic.claude-opus-5-5    |   ON_DEMAND|
  | anthropic.claude-sonnet-4-20250514-v1:0 | ON_DEMAND|
  | anthropic.claude-sonnet-4-5-20250929-v1:0   |    ON_DEMAND|
  | anthropic.claude-sonnet-4-6   |  ON_DEMAND|
  | anthropic.claude-sonnet-5     |  ON_DEMAND|
  | anthropic.claude-sonnet-5-5   |  ON_DEMAND|

### 2. Inference Profiles 
- **amazon.nova-lite-v1:0**
  ```json
  {
    "arn": "arn:aws:bedrock:us-east-1:000000000000:inference-profile/us.amazon.nova-lite-v1:0",
    "models": [
      "arn:aws:bedrock:us-east-1::foundation-model/amazon.nova-lite-v1:0",
      "arn:aws:bedrock:us-east-2::foundation-model/amazon.nova-lite-v1:0",
      "arn:aws:bedrock:us-west-2::foundation-model/amazon.nova-lite-v1:0"
    ]
  }
  ```
  These are what **Block 4** must allow.
- **us.anthropic.claude-haiku-4-5-20251001-v1:0** (2026-11-19): not found, an emulator gap.
  ```
  aws: [ERROR]: An error occurred (ResourceNotFoundException) when calling the GetInferenceProfile operation: Inference profile not found.
  ```
  - `list-inference-profiles` on the emulator has only Claude 3 / 3.5 Anthropic profiles (`us.anthropic.claude-3-haiku-20240307-v1:0` and three Sonnets). Real AWS is the opposite: Haiku 4.5's model card gives no bare model ID for `bedrock-runtime`, only the `us.` / `eu.` / `au.` / `jp.` / `in.` / `global.` profiles. So the emulator accepts Haiku 4.5 by its bare ID, but the same call would fail on the real account.

### 3. Real target: Claude Haiku 5.5 (from the AWS model card, not the emulator)

Source: [Claude Haiku 5.5 model card](https://docs.aws.amazon.com/bedrock/latest/userguide/model-card-anthropic-claude-haiku-5-5.html), read 2026-11-19.

| Field | Value |
|---|---|
| Model ID | `anthropic.claude-haiku-5-5` (no date or `-v1:0` suffix) |
| Geo profile (what we call) | `us.anthropic.claude-haiku-5-5` (also `ca.`, `eu.`, `au.`, `jp.`) |
| Global profile | `global.anthropic.claude-haiku-5-5` (routes anywhere, so no data residency) |
| In-Region on `bedrock-runtime` | N/A: on-demand calls go through a profile |
| Profile ARN (Block 4) | `arn:aws:bedrock:us-east-1:<account>:inference-profile/us.anthropic.claude-haiku-5-5` |
| Foundation-model ARNs (Block 4) | `arn:aws:bedrock:<region>::foundation-model/anthropic.claude-haiku-5-5`, one per region the profile routes to. **Get the regions from `get-inference-profile` on the real account**, not from the docs (AWS re:Post gives the same advice: build IAM from the command's output, not a hard-coded list) |
| FTU form | Not needed (only for Claude models older than Opus 4.7) |
| Launched / EOL no sooner than | 2026-10-07 / 2027-10-07 |

- **Same pattern as Nova Lite in §2:** the IAM policy must allow the profile ARN **and** the foundation-model ARN in every region the profile routes to. Block 4 rehearses this with Nova because the emulator has a real 3-region Nova profile and no Haiku one.
- **Watch:** the model card calls these "inference profiles with limited model selection": AWS may not add new models to them. Today's IDs don't change; it matters when Haiku 5.5 is retired.
- **The bare ID still matters:** IAM uses it in the foundation-model ARNs, and `get-foundation-model-availability` takes it (3.3).
