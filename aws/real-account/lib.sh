# shellcheck shell=bash disable=SC2034  # SC2034: the variables are read by the scripts that source this file
# lib.sh: shared by every script in aws/real-account/. Sourced, never run.
#
# One rule above all: a script must know which account it talks to before its first AWS call.
#   TARGET=emulator  aws-emulator at https://127.0.0.1:9297, account 000000000000 (sources ~/.aws/emulator/ai-lab-kb.env)
#   TARGET=real      a real account through named profiles; refuses to start in a terminal that carries emulator variables
# DRY_RUN=1 prints every external command instead of running it (works before the account exists).
# --yes (or YES=1) answers the billed-step prompts.
#
# macOS ships bash 3.2: no associative arrays, no ${x,,}, no mapfile, no empty "${arr[@]}" under set -u.

set -euo pipefail

LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$LIB_DIR/../.." && pwd)"
AWS_SETUP_DIR="$REPO_ROOT/docker/aws-setup"

REGION="us-east-1"
EMULATOR_ACCOUNT="000000000000"
EMULATOR_PORT="${EMULATOR_PORT:-9297}"
EMULATOR_ENV_FILE="$HOME/.aws/emulator/ai-lab-kb.env"
ADMIN_PROFILE="${ADMIN_PROFILE:-ai-lab-admin}"
DEV_PROFILE="${DEV_PROFILE:-ai-lab-dev}"
DEV_USER="ai-lab-dev"
POLICY_NAME="AiLabBedrockInvoke"
DRY_RUN="${DRY_RUN:-0}"
YES="${YES:-0}"
for _arg in "$@"; do [[ "$_arg" == "--yes" ]] && YES=1; done

# Models (week 7). Chat: Nova Lite through its US profile on both targets. Claude: Haiku 5.5 on the real account;
# the emulator's catalog has no Haiku 5.5 and no Claude 4+ profile, so it rehearses with Haiku 4.5 by its bare ID.
NOVA_PROFILE="us.amazon.nova-lite-v1:0"
NOVA_MODEL="amazon.nova-lite-v1:0"
TITAN_MODEL="amazon.titan-embed-text-v2:0"
TITAN_ARN="arn:aws:bedrock:${REGION}::foundation-model/${TITAN_MODEL}"
NOVA_US_EAST_1_ARN="arn:aws:bedrock:${REGION}::foundation-model/${NOVA_MODEL}"

# ---------- output ----------
log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
ok()   { printf '    \033[32m✓\033[0m %s\n' "$*"; }
note() { printf '    \033[33m•\033[0m %s\n' "$*"; }
die()  { printf '\n\033[1;31m✗ %s\033[0m\n' "$*" >&2; exit 1; }
manual() { printf '\n\033[1;35m[manual]\033[0m %s\n' "$*"; }

is_emulator() { [[ "$TARGET" == "emulator" ]]; }
is_dry()      { [[ "$DRY_RUN" == "1" ]]; }

# ---------- running commands ----------
# run CMD...: an external command with effects (or slow). Printed instead of run in a dry run.
run() {
  if is_dry; then printf '    $'; printf ' %q' "$@"; printf '\n'; return 0; fi
  "$@"
}

# cap NAME CMD...: an external command whose stdout we need. A dry run prints it and returns the placeholder <NAME>.
cap() {
  local name="$1"; shift
  if is_dry; then printf '    $' >&2; printf ' %q' "$@" >&2; printf '\n' >&2; printf '<%s>' "$name"; return 0; fi
  "$@"
}

# found VALUE: a lookup that returned something real ("None" and "" are the CLI's "nothing"; a dry run finds nothing)
found() { [[ -n "$1" && "$1" != "None" && "$1" != "null" && "$1" != \<*\> ]]; }

require_cmd() { for c in "$@"; do command -v "$c" >/dev/null || die "$c is required."; done; }

# ---------- the target guard ----------
case "${TARGET:-}" in
  emulator|real) ;;
  *) die "Set TARGET=emulator or TARGET=real (nothing runs without it)." ;;
esac

STATE_DIR="$HOME/.aws/ai-lab"
STATE_FILE="$STATE_DIR/$TARGET.env"

# use_identity admin|dev: point the CLI and the SDK at the right account and identity, then prove it.
use_identity() {
  local who="$1"
  if is_emulator; then
    [[ -f "$EMULATOR_ENV_FILE" ]] || die "No $EMULATOR_ENV_FILE: start the emulator with docker/bedrock-kb/bedrock-kb-emulator.sh first."
    # shellcheck source=/dev/null
    source "$EMULATOR_ENV_FILE"
    # Both identities are the emulator's own user: it checks no permissions on calls (the simulator is the only verdict).
    export Bedrock__ServiceUrl="https://127.0.0.1:${EMULATOR_PORT}"
    export Bedrock__EmulatorCaPath="$HOME/.aws/emulator/${EMULATOR_PORT}/ca.pem"
    if is_dry; then ACCOUNT_ID="$EMULATOR_ACCOUNT"; ok "dry run: emulator (account $ACCOUNT_ID, $who)"; return 0; fi
    ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text 2>/dev/null || true)"
    [[ "$ACCOUNT_ID" == "$EMULATOR_ACCOUNT" ]] || die "STOP: TARGET=emulator but the account is '${ACCOUNT_ID:-unreachable}'. Is the emulator running?"
    ok "SAFE: emulator, account $ACCOUNT_ID ($who)"
  else
    # A terminal that once sourced ai-lab-kb.env carries these, and the SDK copies AWS_ENDPOINT_URL into ServiceURL.
    # Refuse rather than unset: mixing the two in one terminal is how a run lands on the wrong account.
    local v
    for v in AWS_ENDPOINT_URL AWS_ENDPOINT_URL_BEDROCK_AGENT_RUNTIME AWS_CA_BUNDLE AWS_SHARED_CREDENTIALS_FILE \
             AWS_CONFIG_FILE SSL_CERT_FILE Bedrock__ServiceUrl Bedrock__EmulatorCaPath; do
      [[ -z "${!v:-}" ]] || die "STOP: TARGET=real but $v is set (an emulator terminal). Open a fresh terminal."
    done
    unset AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_SESSION_TOKEN
    if [[ "$who" == "admin" ]]; then export AWS_PROFILE="$ADMIN_PROFILE"; else export AWS_PROFILE="$DEV_PROFILE"; fi
    export AWS_REGION="$REGION" AWS_DEFAULT_REGION="$REGION" AWS_PAGER=""
    if is_dry; then
      ACCOUNT_ID="$(state_get ACCOUNT_ID)"; [[ -n "$ACCOUNT_ID" ]] || ACCOUNT_ID="<ACCOUNT_ID>"
      ok "dry run: real account $ACCOUNT_ID, profile $AWS_PROFILE (not checked)"; return 0
    fi
    local arn
    arn="$(aws sts get-caller-identity --query Arn --output text 2>/dev/null || true)"
    ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text 2>/dev/null || true)"
    [[ -n "$ACCOUNT_ID" && "$ACCOUNT_ID" != "None" ]] || die "STOP: profile $AWS_PROFILE has no working credentials (aws sso login --profile $AWS_PROFILE?)."
    [[ "$ACCOUNT_ID" != "$EMULATOR_ACCOUNT" ]] || die "STOP: TARGET=real but the account is the emulator's."
    if [[ "$who" == "dev" && "$arn" != *"$DEV_USER"* ]]; then
      die "STOP: the runs go as $DEV_USER, but profile $AWS_PROFILE is $arn."
    fi
    ok "REAL: account $ACCOUNT_ID as $arn"
    state_set ACCOUNT_ID "$ACCOUNT_ID"
  fi
}

# ---------- state (outside the repo: IDs and the account number never go into git) ----------
state_get() {
  [[ -f "$STATE_FILE" ]] || { printf ''; return 0; }
  # Lines are KEY="value"
  sed -n "s/^$1=\"\(.*\)\"$/\1/p" "$STATE_FILE" | tail -1
}
state_set() {
  is_dry && return 0
  mkdir -p "$STATE_DIR"; touch "$STATE_FILE"; chmod 600 "$STATE_FILE"
  local tmp; tmp="$(mktemp)"
  grep -v "^$1=" "$STATE_FILE" > "$tmp" || true
  printf '%s="%s"\n' "$1" "$2" >> "$tmp"
  mv "$tmp" "$STATE_FILE"; chmod 600 "$STATE_FILE"
}
state_unset() {
  is_dry && return 0
  [[ -f "$STATE_FILE" ]] || return 0
  local tmp; tmp="$(mktemp)"; grep -v "^$1=" "$STATE_FILE" > "$tmp" || true; mv "$tmp" "$STATE_FILE"; chmod 600 "$STATE_FILE"
}

# ---------- money ----------
# confirm_billed WHAT EXPECTED: real money needs a yes. The emulator is free.
confirm_billed() {
  is_emulator && return 0
  printf '\n\033[1;33m$ Billed:\033[0m %s. Expected: %s.\n' "$1" "$2"
  is_dry && return 0
  [[ "$YES" == "1" ]] && { note "--yes given"; return 0; }
  local answer; read -r -p "    Go on? [y/N] " answer
  [[ "$answer" == "y" || "$answer" == "Y" ]] || die "Stopped before the billed step."
}

# emulator_limit MESSAGE: a step the emulator can't do. On the emulator, say so and end this script cleanly;
# on a real account the same failure is a real failure.
emulator_limit() {
  if is_emulator; then printf '\n\033[1;33m[emulator limit]\033[0m %s\n' "$*"; exit 0; fi
  die "$*"
}

# ---------- names ----------
# The emulator keeps the names the week 8 script used (so its KB is reused); real S3 bucket names are global, so they
# carry the account ID.
kb_names() {
  KB_NAME="ai-lab-kb"; DS_NAME="docs-s3"; INDEX_NAME="docs"; ROLE_NAME="BedrockKbRole"
  if is_emulator; then DOCS_BUCKET="ai-lab-docs"; VECTOR_BUCKET="ai-lab-vectors"
  else DOCS_BUCKET="ai-lab-docs-${ACCOUNT_ID}"; VECTOR_BUCKET="ai-lab-vectors-${ACCOUNT_ID}"; fi
}

# Claude for this target: the model the policy allows and the ID the console / evals call
claude_ids() {
  if is_emulator; then
    CLAUDE_CALL_ID="anthropic.claude-haiku-4-5-20251001-v1:0"   # bare ID: the emulator has no profile for it
    CLAUDE_PROFILE=""
  else
    CLAUDE_CALL_ID="us.anthropic.claude-haiku-5-5"
    CLAUDE_PROFILE="us.anthropic.claude-haiku-5-5"
  fi
}

# ---------- the invoke policy (week 7 Monday D3, Wednesday's Titan statement, week 8's KB statement) ----------
# Built from the state 02 saved (each profile's foundation-model ARNs, one per region) and, once 07 ran, the KB ID.
# Same statements as docker/aws-setup/bedrock-invoke-policy.json; only the account, Claude and the regions change.
policy_render() {
  local out="$1" nova_arns claude_arns kb_id
  nova_arns="$(state_get NOVA_MODEL_ARNS)"; claude_arns="$(state_get CLAUDE_MODEL_ARNS)"; kb_id="$(state_get KB_ID)"
  claude_ids
  [[ -n "$nova_arns" ]] || nova_arns="<NOVA_MODEL_ARNS>"
  [[ -n "$claude_arns" ]] || claude_arns="<CLAUDE_MODEL_ARNS>"
  local profiles="arn:aws:bedrock:${REGION}:${ACCOUNT_ID}:inference-profile/${NOVA_PROFILE}"
  [[ -z "$CLAUDE_PROFILE" ]] || profiles="$profiles arn:aws:bedrock:${REGION}:${ACCOUNT_ID}:inference-profile/${CLAUDE_PROFILE}"
  # shellcheck disable=SC2086 # word splitting is the point: space-separated ARN lists
  jq -n '$ARGS.positional' --args $profiles $nova_arns $claude_arns > "$out.chat"
  jq -n --slurpfile chat "$out.chat" --arg titan "$TITAN_ARN" --arg kb "$kb_id" \
     --arg kbarn "arn:aws:bedrock:${REGION}:${ACCOUNT_ID}:knowledge-base/${kb_id}" '
    { Version: "2012-10-17",
      Statement: ([
        { Sid: "AllowBedrockModelInvocation", Effect: "Allow",
          Action: ["bedrock:InvokeModel", "bedrock:InvokeModelWithResponseStream"], Resource: $chat[0] },
        { Sid: "AllowBedrockEmbeddings", Effect: "Allow", Action: ["bedrock:InvokeModel"], Resource: [$titan] }
      ] + (if $kb == "" then [] else
        [{ Sid: "AllowKnowledgeBaseQueries", Effect: "Allow",
           Action: ["bedrock:Retrieve", "bedrock:RetrieveAndGenerate"], Resource: [$kbarn] }] end)) }' > "$out"
  rm -f "$out.chat"
}

# policy_apply FILE: create the policy, or add a default version (IAM keeps 5: the oldest non-default goes first)
policy_apply() {
  local file="$1" arn="arn:aws:iam::${ACCOUNT_ID}:policy/${POLICY_NAME}"
  if is_dry || ! aws iam get-policy --policy-arn "$arn" >/dev/null 2>&1; then
    run aws iam create-policy --policy-name "$POLICY_NAME" --policy-document "file://$file" --query Policy.Arn --output text
  else
    local count oldest
    count="$(aws iam list-policy-versions --policy-arn "$arn" --query 'length(Versions)' --output text)"
    if [[ "$count" -ge 5 ]]; then
      # shellcheck disable=SC2016 # backticks are JMESPath literals, not shell
      oldest="$(aws iam list-policy-versions --policy-arn "$arn" --query 'Versions[?IsDefaultVersion==`false`] | [-1].VersionId' --output text)"
      run aws iam delete-policy-version --policy-arn "$arn" --version-id "$oldest"
    fi
    run aws iam create-policy-version --policy-arn "$arn" --policy-document "file://$file" --set-as-default \
      --query PolicyVersion.VersionId --output text
  fi
  ok "policy $POLICY_NAME"
}

# simulate_cases custom FILE | principal: run "action|resource|expected" lines from stdin, print a table, count mismatches.
# zsh users: the scripts are bash, so ${ACCOUNT_ID}: needs no special care here.
simulate_cases() {
  local mode="$1" file="${2:-}" fails=0 n=0 action resource expected actual
  printf '    %-3s %-42s %-12s %-12s %s\n' "#" "Action" "Expected" "Actual" "Resource"
  while IFS='|' read -r action resource expected; do
    [[ -z "$action" ]] && continue
    n=$((n + 1))
    if [[ "$mode" == "custom" ]]; then
      actual="$(cap DECISION aws iam simulate-custom-policy --policy-input-list "$(jq -Rs '[.]' < "$file")" \
        --action-names "$action" --resource-arns "$resource" --query 'EvaluationResults[0].EvalDecision' --output text < /dev/null)"
    else
      actual="$(cap DECISION aws iam simulate-principal-policy \
        --policy-source-arn "arn:aws:iam::${ACCOUNT_ID}:user/${DEV_USER}" \
        --action-names "$action" --resource-arns "$resource" --query 'EvaluationResults[0].EvalDecision' --output text < /dev/null)"
    fi
    is_dry || [[ "$actual" == "$expected" ]] || fails=$((fails + 1))
    printf '    %-3s %-42s %-12s %-12s %s\n' "$n" "$action" "$expected" "$actual" "$resource"
  done
  is_dry && return 0
  [[ "$fails" -eq 0 ]] || die "$fails of $n simulator case(s) differ from the expected decision."
  ok "simulator: $n/$n as expected ($mode)"
}

# The week 7 cases (13) plus week 8's KB cases once a KB exists. Expected decisions only: no AWS call here.
simulator_cases() {
  claude_ids
  local p="arn:aws:bedrock:${REGION}:${ACCOUNT_ID}:inference-profile/${NOVA_PROFILE}" arn kb_id
  echo "bedrock:InvokeModel|$p|allowed"
  for arn in $(state_get NOVA_MODEL_ARNS); do echo "bedrock:InvokeModel|$arn|allowed"; done
  for arn in $(state_get CLAUDE_MODEL_ARNS); do echo "bedrock:InvokeModel|$arn|allowed"; done
  [[ -z "$CLAUDE_PROFILE" ]] || echo "bedrock:InvokeModel|arn:aws:bedrock:${REGION}:${ACCOUNT_ID}:inference-profile/${CLAUDE_PROFILE}|allowed"
  echo "bedrock:InvokeModelWithResponseStream|$p|allowed"
  echo "bedrock:InvokeModel|arn:aws:bedrock:${REGION}::foundation-model/amazon.nova-pro-v1:0|implicitDeny"
  echo "bedrock:InvokeModel|arn:aws:bedrock:eu-west-1::foundation-model/${NOVA_MODEL}|implicitDeny"
  echo "bedrock:CreateKnowledgeBase|*|implicitDeny"
  echo "iam:CreateUser|*|implicitDeny"
  echo "s3:ListAllMyBuckets|*|implicitDeny"
  echo "bedrock:InvokeModel|$TITAN_ARN|allowed"
  echo "bedrock:InvokeModel|arn:aws:bedrock:${REGION}::foundation-model/amazon.titan-embed-text-v1|implicitDeny"
  echo "bedrock:InvokeModelWithResponseStream|$TITAN_ARN|implicitDeny"
  echo "bedrock:InvokeModel|arn:aws:bedrock:us-west-2::foundation-model/${TITAN_MODEL}|implicitDeny"
  kb_id="$(state_get KB_ID)"
  if [[ -n "$kb_id" ]]; then
    echo "bedrock:Retrieve|arn:aws:bedrock:${REGION}:${ACCOUNT_ID}:knowledge-base/${kb_id}|allowed"
    echo "bedrock:RetrieveAndGenerate|arn:aws:bedrock:${REGION}:${ACCOUNT_ID}:knowledge-base/${kb_id}|allowed"
    echo "bedrock:Retrieve|arn:aws:bedrock:${REGION}:${ACCOUNT_ID}:knowledge-base/OTHERKB000|implicitDeny"
  fi
}

# ---------- local services the eval steps need ----------
require_postgres() {
  is_dry && return 0
  (cd "$REPO_ROOT" && docker compose ps --status running --services 2>/dev/null | grep -qx vectors) \
    || die "Postgres isn't running: docker compose up -d (colima start first)."
}
require_ollama() {
  is_dry && return 0
  curl -sf http://localhost:11434/api/tags >/dev/null || die "Ollama isn't running (the eval host embeds questions with nomic-embed-text)."
}
