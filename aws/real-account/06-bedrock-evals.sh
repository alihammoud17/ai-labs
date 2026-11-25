#!/usr/bin/env bash
# 06-bedrock-evals.sh: week 7 Thursday. Two Bedrock models answer /ask's 30 questions, 3 runs each, scored by our own
# gold check: no judge, no Anthropic call (AILAB_EVAL_JUDGE=none).
# Replays: docs/evals/README.md "Run the eval suite on Bedrock", steps 1–3. Steps 4–5 (hand pass, regenerate D2,
# the decision rule) stay manual: regenerating D2 rewrites a committed doc and drops its hand-written section.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

use_identity dev
cd "$REPO_ROOT"
require_postgres
require_ollama   # the eval host still embeds questions with nomic (corpus, heading chunks)

confirm_billed "2 models × 90 answers through /ask (87 reach the model; E5 is gated)" \
  "≈ \$0.07 (Nova Lite ≈ \$0.023, Haiku 5.5 ≈ \$0.045); above ≈ \$0.20 means something re-ran"

KEY="${TARGET}-$(date +%Y%m%d-%H%M)"   # a new cache key: fresh answers and fresh latency
# Exact filter: a bare "Ask_set_v1" also matches the KB test Ask_set_v1_kb, whose fixture throws without a KB ID
FILTER="FullyQualifiedName~AiLab.Evals.AskSetEvalTests.Ask_set_v1"

# The eval suite accepts exactly these two IDs (EvalSettings.BedrockModels), on both targets: on the emulator Haiku 5.5
# isn't in the catalog, so its rows fail with provider_unavailable, as in week 7
for model in "$NOVA_PROFILE" "us.anthropic.claude-haiku-5-5"; do
  case "$model" in
    "$NOVA_PROFILE") execution="bedrock-nova-lite" ;;
    *)               execution="bedrock-haiku-5-5" ;;
  esac
  # Separate names on the emulator, so its runs never land in the bedrock-* folders the real report reads
  is_emulator && execution="emulator-$execution"
  [[ ! -d "eval-runs/meai/results/$execution" ]] || is_dry \
    || die "eval-runs/meai/results/$execution already exists: move it aside first (the report reads every run in it)."
  log "$model → execution $execution"
  if ! run env AILAB_EVAL=1 AILAB_EVAL_ANSWERS=bedrock AILAB_EVAL_MODEL="$model" AILAB_EVAL_JUDGE=none \
       AILAB_EVAL_RUNS=3 AILAB_EVAL_CACHE_KEY="$KEY" AILAB_EVAL_EXECUTION="$execution" \
       dotnet test AiLab.Evals --filter "$FILTER"; then
    if is_emulator; then
      note "expected on the emulator: Nova Lite answers {} (502 reply_unreadable); Haiku 5.5 isn't in its catalog (provider_unavailable). Only E5 (gated) stores runs."
    else
      note "some rows failed: an HTTP error is a setup problem (nothing stored), and the report will say incomplete. Read the test output."
    fi
  fi
done

if is_emulator; then
  note "emulator executions stay under eval-runs/meai/results/emulator-bedrock-*: delete them when done"
else
  manual "Next, by hand (docs/evals/README.md steps 4–5):"
  cat <<'EOF'
    4. Hand pass on the 12 judge rows, run 1 → eval/results/<date>-bedrock-hand-scores.md
       (answers: dotnet aieval report --path eval-runs/meai --output eval-runs/meai/report.html)
    5. Regenerate D2, then copy the Artificial Analysis section back from git, then apply the rule:
       AILAB_EVAL_COST=matrix-heading-haiku,matrix-fixed-haiku,matrix-heading-llama,matrix-fixed-llama,bedrock-nova-lite,bedrock-haiku-5-5 \
       dotnet test AiLab.Evals --filter Write_cost_per_correct
EOF
fi
