#!/usr/bin/env bash
# 02-model-access.sh: week 7 Monday, D2. Which regions each inference profile routes to (the policy needs one
# foundation-model ARN per region), and whether Claude is usable yet.
# Replays: docker/aws-setup/README.md "Model IDs", and "On the real account" steps 4 and 7.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

use_identity admin
claude_ids

profile_arns() {  # the foundation-model ARNs behind a profile, space-separated
  cap ARNS aws bedrock get-inference-profile --inference-profile-identifier "$1" \
    --query 'models[].modelArn' --output text | tr '\t' ' '
}

log "Nova Lite: $NOVA_PROFILE"
nova="$(profile_arns "$NOVA_PROFILE")"
found "$nova" || is_dry || die "No inference profile $NOVA_PROFILE in $REGION."
state_set NOVA_MODEL_ARNS "$nova"
for arn in $nova; do ok "$arn"; done

log "Claude: $CLAUDE_CALL_ID"
if is_emulator; then
  # No Haiku 5.5 and no Claude 4+ profile in the emulator's catalog: rehearse with Haiku 4.5's bare model ARN
  claude="arn:aws:bedrock:${REGION}::foundation-model/${CLAUDE_CALL_ID}"
  note "emulator: no Claude profile; using the bare model $CLAUDE_CALL_ID"
else
  claude="$(profile_arns "$CLAUDE_PROFILE")"
  found "$claude" || is_dry || die "No inference profile $CLAUDE_PROFILE in $REGION."
fi
state_set CLAUDE_MODEL_ARNS "$claude"
for arn in $claude; do ok "$arn"; done

log "Availability"
for model in "$NOVA_MODEL" "${CLAUDE_CALL_ID#us.}"; do
  status="$(cap STATUS aws bedrock get-foundation-model-availability --model-id "$model" \
    --query '[agreementAvailability.status, authorizationStatus, entitlementAvailability]' --output text 2>/dev/null || echo unknown)"
  printf '    %-45s %s\n' "$model" "$status"
done

if ! is_emulator; then
  manual "Claude: the first call must come from the admin (it has the Marketplace permissions ai-lab-dev lacks):"
  cat <<EOF
      AWS_PROFILE=$ADMIN_PROFILE aws bedrock-runtime converse --model-id $CLAUDE_CALL_ID \\
        --messages '[{"role":"user","content":[{"text":"Say hi in three words."}]}]' --inference-config maxTokens=20
    Then wait until this says AVAILABLE before 04 (it can take a few minutes):
      aws bedrock get-foundation-model-availability --model-id anthropic.claude-haiku-5-5
    No first-use form: Haiku 5.5 doesn't need one (anthropic-use-case.json is for Claude models older than Opus 4.7).
EOF
fi
