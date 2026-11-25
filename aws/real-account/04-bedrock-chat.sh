#!/usr/bin/env bash
# 04-bedrock-chat.sh: week 7 Tuesday. Bedrock answers as ai-lab-dev, through the same IDs the app uses, and refuses a
# model outside the policy.
# Replays: docker/aws-setup/README.md "On the real account" step 9; the console run of experiments.md section 22.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

use_identity dev
claude_ids
confirm_billed "one short converse per model (about 20 output tokens each)" "well under \$0.001"

converse() {  # converse MODEL_ID: a 3-word answer and its token counts
  run aws bedrock-runtime converse --model-id "$1" \
    --messages '[{"role":"user","content":[{"text":"Say hi in three words."}]}]' \
    --inference-config maxTokens=20 \
    --query '{text: output.message.content[0].text, input: usage.inputTokens, output: usage.outputTokens, stop: stopReason}' \
    --output json
}

log "Nova Lite ($NOVA_PROFILE)"
converse "$NOVA_PROFILE" || emulator_limit "Nova Lite converse failed."
ok "answered"

log "Claude ($CLAUDE_CALL_ID)"
if converse "$CLAUDE_CALL_ID"; then
  ok "answered"
elif is_emulator; then
  note "the emulator's Claude answer failed: not a finding about Bedrock"
else
  die "Claude refused: model access not AVAILABLE yet (02), or the policy lacks a region (03)."
fi

log "A model outside the policy must be refused"
if is_emulator; then
  note "skipped: the emulator checks no permissions on calls (user/emulator can call everything)"
elif is_dry; then
  converse "amazon.nova-pro-v1:0"
else
  if err="$(converse "amazon.nova-pro-v1:0" 2>&1)"; then
    die "Nova Pro answered as $DEV_USER: the policy is wider than it should be."
  fi
  [[ "$err" == *AccessDenied* ]] || die "Nova Pro failed, but not with AccessDenied: $err"
  ok "AccessDeniedException, as expected"
fi

log "The console, by hand (streams through ConverseStream)"
if is_emulator; then
  echo "    Chat__Provider=bedrock Chat__Model=$NOVA_PROFILE Bedrock__ServiceUrl=$Bedrock__ServiceUrl \\"
  echo "    Bedrock__EmulatorCaPath=$Bedrock__EmulatorCaPath dotnet run --project AiLab.Console"
else
  echo "    AWS_PROFILE=$DEV_PROFILE Chat__Provider=bedrock Chat__Model=$CLAUDE_CALL_ID dotnet run --project AiLab.Console"
fi
note "/analyze-ticket still fails on Bedrock (response format + tools): a known gap, not a setup problem"
