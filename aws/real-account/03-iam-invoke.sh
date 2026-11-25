#!/usr/bin/env bash
# 03-iam-invoke.sh: week 7 Monday D3 (+ Wednesday's Titan statement, + week 8's KB statement once 07 ran).
# The least-privilege policy AiLabBedrockInvoke, checked as a file, then created and attached to the user ai-lab-dev,
# then checked again as attached. Any simulator decision that differs from the expected one stops the script.
# Replays: docker/aws-setup/README.md "Least-privilege check" (the 13 cases) and "On the real account" steps 5–6.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

use_identity admin
[[ -n "$(state_get NOVA_MODEL_ARNS)" ]] || is_dry || die "Run 02 first: the policy needs each profile's regions."

policy="$(mktemp "${TMPDIR:-/tmp}/ailab-policy.XXXXXX")"
trap 'rm -f "$policy"' EXIT

log "Render $POLICY_NAME"
policy_render "$policy"
jq -r '.Statement[] | "    \(.Sid): \(.Action | join(", "))\n      " + (.Resource | join("\n      "))' "$policy"

log "Simulator on the file (before anything exists)"
simulator_cases | simulate_cases custom "$policy"

log "Create or update"
policy_apply "$policy"
if is_dry || ! aws iam get-user --user-name "$DEV_USER" >/dev/null 2>&1; then
  run aws iam create-user --user-name "$DEV_USER" --query User.Arn --output text
fi
run aws iam attach-user-policy --user-name "$DEV_USER" --policy-arn "arn:aws:iam::${ACCOUNT_ID}:policy/${POLICY_NAME}"
run aws iam list-attached-user-policies --user-name "$DEV_USER" --query 'AttachedPolicies[].PolicyName' --output text
ok "user $DEV_USER has $POLICY_NAME"

log "Simulator as attached"
simulator_cases | simulate_cases principal

if is_emulator; then
  note "the emulator checks no permissions on calls: these verdicts are the only proof here (04 proves it on a real account)"
else
  manual "Credentials for $DEV_USER in the profile $DEV_PROFILE (IAM Identity Center beats a long-lived access key)."
  echo "    Check: AWS_PROFILE=$DEV_PROFILE aws sts get-caller-identity --query Arn --output text   # .../$DEV_USER"
fi
