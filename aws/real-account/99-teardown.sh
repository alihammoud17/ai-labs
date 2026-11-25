#!/usr/bin/env bash
# 99-teardown.sh: delete what 07 built (S3 Vectors and S3 storage bill while they exist), and with --all also the
# week 7 setup (the KB statement, policy, user ai-lab-dev and the budget). Budget last: it guards everything else.
#   TARGET=real ./99-teardown.sh          KB, data source, vector index + bucket, docs bucket, service role
#   TARGET=real ./99-teardown.sh --all    ...plus policy, user and budget
# On the real account it asks you to type "delete" first (--yes doesn't skip that).
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ALL=0
for a in "$@"; do [[ "$a" == "--all" ]] && ALL=1; done
use_identity admin
kb_names
found "$(state_get DOCS_BUCKET)" && DOCS_BUCKET="$(state_get DOCS_BUCKET)"
found "$(state_get VECTOR_BUCKET)" && VECTOR_BUCKET="$(state_get VECTOR_BUCKET)"

if ! is_emulator && ! is_dry; then
  printf '\n    Deletes on account %s: KB %s, s3://%s, vector bucket %s, role %s%s.\n' "$ACCOUNT_ID" \
    "$(state_get KB_ID)" "$DOCS_BUCKET" "$VECTOR_BUCKET" "$ROLE_NAME" "$([[ $ALL == 1 ]] && echo ", policy $POLICY_NAME, user $DEV_USER, budget ai-lab-monthly")"
  read -r -p "    Type delete to go on: " answer
  [[ "$answer" == "delete" ]] || die "Nothing deleted."
fi

log "Knowledge Base"
KB_ID="$(state_get KB_ID)"
is_dry && ! found "$KB_ID" && KB_ID="<KB_ID>"
if found "$KB_ID" || is_dry; then
  DS_ID="$(state_get DS_ID)"
  is_dry && ! found "$DS_ID" && DS_ID="<DS_ID>"
  { found "$DS_ID" || is_dry; } && run aws bedrock-agent delete-data-source --knowledge-base-id "$KB_ID" --data-source-id "$DS_ID" >/dev/null || true
  run aws bedrock-agent delete-knowledge-base --knowledge-base-id "$KB_ID" >/dev/null || true
  if ! is_dry; then
    for _ in $(seq 1 60); do
      aws bedrock-agent get-knowledge-base --knowledge-base-id "$KB_ID" >/dev/null 2>&1 || break
      sleep 2
    done
  fi
  state_unset KB_ID; state_unset DS_ID
  ok "KB $KB_ID deleted"
else
  note "no KB_ID in $STATE_FILE"
fi

log "Vector store and docs bucket"
run aws s3vectors delete-index --vector-bucket-name "$VECTOR_BUCKET" --index-name "$INDEX_NAME" 2>/dev/null || true
run aws s3vectors delete-vector-bucket --vector-bucket-name "$VECTOR_BUCKET" 2>/dev/null || true
run aws s3 rb "s3://$DOCS_BUCKET" --force >/dev/null 2>&1 || true
state_unset DOCS_BUCKET; state_unset VECTOR_BUCKET
ok "$VECTOR_BUCKET, s3://$DOCS_BUCKET"

log "Service role $ROLE_NAME"
run aws iam delete-role-policy --role-name "$ROLE_NAME" --policy-name kb-access 2>/dev/null || true
run aws iam delete-role --role-name "$ROLE_NAME" 2>/dev/null || true
ok "deleted"

if [[ "$ALL" == 1 ]]; then
  arn="arn:aws:iam::${ACCOUNT_ID}:policy/${POLICY_NAME}"
  log "Policy, user, budget (--all)"
  run aws iam detach-user-policy --user-name "$DEV_USER" --policy-arn "$arn" 2>/dev/null || true
  if ! is_dry; then
    # shellcheck disable=SC2016 # backticks are JMESPath literals
    for v in $(aws iam list-policy-versions --policy-arn "$arn" --query 'Versions[?IsDefaultVersion==`false`].VersionId' --output text 2>/dev/null || true); do
      aws iam delete-policy-version --policy-arn "$arn" --version-id "$v"
    done
    for k in $(aws iam list-access-keys --user-name "$DEV_USER" --query 'AccessKeyMetadata[].AccessKeyId' --output text 2>/dev/null || true); do
      aws iam delete-access-key --user-name "$DEV_USER" --access-key-id "$k"
    done
  fi
  run aws iam delete-policy --policy-arn "$arn" 2>/dev/null || true
  run aws iam delete-user --user-name "$DEV_USER" 2>/dev/null || true
  run aws budgets delete-budget --account-id "$ACCOUNT_ID" --budget-name ai-lab-monthly 2>/dev/null || true
  ok "policy, user and budget deleted"
else
  # The policy still names the deleted KB: harmless (no such resource), and 07 replaces the statement next time
  note "kept: $POLICY_NAME, $DEV_USER, the budget (--all deletes them)"
fi
