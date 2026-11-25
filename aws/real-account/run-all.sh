#!/usr/bin/env bash
# run-all.sh: weeks 7–8 replayed in order, 00 → 08 (99-teardown.sh is never run from here).
#   TARGET=emulator ./run-all.sh               rehearse on aws-emulator (start it with docker/bedrock-kb/bedrock-kb-emulator.sh)
#   TARGET=real ./run-all.sh                   the real account; pauses before each step
#   TARGET=real ./run-all.sh --from 04         resume
#   TARGET=real ./run-all.sh --only 07         one step
#   TARGET=real DRY_RUN=1 ./run-all.sh         print every command, run nothing (works before the account exists)
#   --yes                                      no pauses, and yes to the billed-step prompts
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[[ "${TARGET:-}" == "emulator" || "${TARGET:-}" == "real" ]] || { echo "Set TARGET=emulator or TARGET=real." >&2; exit 1; }

FROM="00"; ONLY=""; PASS=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --from) FROM="$2"; shift 2 ;;
    --only) ONLY="$2"; shift 2 ;;
    --yes)  PASS="--yes"; shift ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done

for script in "$DIR"/0[0-9]-*.sh; do
  step="$(basename "$script" | cut -c1-2)"
  if [[ -n "$ONLY" ]]; then [[ "$step" == "$ONLY" ]] || continue
  elif (( 10#$step < 10#$FROM )); then continue; fi
  printf '\n\033[1m━━━━━━━━ %s (%s) ━━━━━━━━\033[0m\n' "$(basename "$script")" "$TARGET"
  if [[ -z "$PASS" && "${DRY_RUN:-0}" != "1" && -z "$ONLY" ]]; then
    read -r -p "Run it? [Enter = yes, s = skip, q = quit] " answer
    case "$answer" in s|S) continue ;; q|Q) exit 0 ;; esac
  fi
  # ${PASS:+...}: bash 3.2 has no empty-array expansion under set -u
  "$script" ${PASS:+"$PASS"} || { echo "Stopped at $step. Fix it, then: TARGET=$TARGET $0 --from $step" >&2; exit 1; }
done
printf '\n\033[1;32mDone.\033[0m What stays manual is listed in %s/README.md.\n' "$DIR"
