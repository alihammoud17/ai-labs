#!/usr/bin/env bash
# 00-preflight.sh: tools, the target, and the account steps no script can do for you.
# Replays: docker/aws-setup/README.md "On the real account", steps 1, 2 and 8 (asked, not done).
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

log "Tools"
require_cmd aws jq curl dotnet docker
aws s3vectors help >/dev/null 2>&1 || die "Your AWS CLI has no 's3vectors' command: upgrade it (brew upgrade awscli)."
ok "aws $(aws --version 2>&1 | cut -d' ' -f1 | cut -d/ -f2), jq, curl, dotnet $(dotnet --version), docker"

if [[ "$TARGET" == "real" ]]; then
  manual "These are yours to do in the console, once. Nothing here checks them:"
  cat <<'EOF'
    1. Root user: MFA on, no access keys. An admin identity with MFA; stop using root.
    2. A payment method (Claude subscribes through AWS Marketplace).
    3. Two named profiles, short-lived credentials preferred (IAM Identity Center):
         ai-lab-admin  the admin identity  (setup: 01, 02, 03, 07, 99)   override with ADMIN_PROFILE=
         ai-lab-dev    the user 03 creates (runs:  04, 05, 06, 08)       override with DEV_PROFILE=
       ai-lab-dev's credentials can only exist after 03; until then only the admin profile is needed.
EOF
  if ! is_dry && [[ "$YES" != "1" ]]; then
    read -r -p "    Steps 1 and 2 done, and the admin profile works? [y/N] " answer
    [[ "$answer" == "y" || "$answer" == "Y" ]] || die "Do them first."
  fi
fi

log "Target: $TARGET"
use_identity admin
note "state file: $STATE_FILE (outside the repo)"
