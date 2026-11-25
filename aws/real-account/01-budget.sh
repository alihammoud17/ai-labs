#!/usr/bin/env bash
# 01-budget.sh: week 7 Monday, D1. The 5 USD monthly budget with its two alerts, then read back.
# Replays: docker/aws-setup/README.md Block 2 (budget.json, budget-notifications.json; the email filled into $TMPDIR).
# Needs BUDGET_EMAIL on the real account (never written to the repo or the state file).
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

use_identity admin
BUDGET_NAME="ai-lab-monthly"

if is_emulator; then
  EMAIL="${BUDGET_EMAIL:-budget@example.invalid}"   # the emulator sends no email
else
  EMAIL="${BUDGET_EMAIL:-}"
  [[ -n "$EMAIL" ]] || is_dry || die "Set BUDGET_EMAIL=<your address> (it goes to AWS only, never into git)."
  [[ -n "$EMAIL" ]] || EMAIL="<BUDGET_EMAIL>"
fi

log "Budget $BUDGET_NAME"
existing="$(cap BUDGET aws budgets describe-budget --account-id "$ACCOUNT_ID" --budget-name "$BUDGET_NAME" \
  --query Budget.BudgetName --output text 2>/dev/null || true)"
if found "$existing"; then
  ok "already exists"
else
  notifications="$(mktemp "${TMPDIR:-/tmp}/budget-notifications.XXXXXX")"
  trap 'rm -f "$notifications"' EXIT
  sed "s/BUDGET_EMAIL/$EMAIL/" "$AWS_SETUP_DIR/budget-notifications.json" > "$notifications"
  run aws budgets create-budget --account-id "$ACCOUNT_ID" \
    --budget "file://$AWS_SETUP_DIR/budget.json" \
    --notifications-with-subscribers "file://$notifications"
  ok "created"
fi

log "Read back"
run aws budgets describe-budget --account-id "$ACCOUNT_ID" --budget-name "$BUDGET_NAME" \
  --query 'Budget.{name:BudgetName,limit:BudgetLimit,unit:TimeUnit,type:BudgetType}' --output table
run aws budgets describe-notifications-for-budget --account-id "$ACCOUNT_ID" --budget-name "$BUDGET_NAME" \
  --query 'Notifications[].[NotificationType,ComparisonOperator,Threshold]' --output table

if is_emulator; then
  note "stored, never evaluated: the emulator counts no spend and sends no email"
else
  manual "Confirm the subscription email AWS sends, or no alert ever arrives. Budget data refreshes up to 3 times a day."
fi
