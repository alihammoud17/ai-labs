#!/usr/bin/env bash
# 08-kb-evals.sh: week 8 Tuesday. The golden set against the Knowledge Base with the same scoring as the pgvector RAG:
# retrieval (top 10, PassageLocator → RetrievalMetrics), pgvector re-run the same day, then answers (RetrieveAndGenerate,
# gold check, 3 runs). The runner labels its reports itself from the endpoint: kb-emulator or kb-bedrock.
# Replays: docker/aws-setup/real-account-gaps.md "Week 8" steps 4–6. Step 7 (refusal phrases) and 8 (the rule) stay manual.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

use_identity dev
cd "$REPO_ROOT"
KB_ID="$(state_get KB_ID)"
[[ -n "$KB_ID" ]] || is_dry || die "No KB_ID in $STATE_FILE: run 07 first."
[[ -n "$KB_ID" ]] || KB_ID="<KB_ID>"
MODEL_ARN="${KB_MODEL_ARN:-$NOVA_US_EAST_1_ARN}"   # the model RetrieveAndGenerate answers with (allowed by 03's policy)
if is_emulator; then PLACE="kb-emulator"; LABEL="KB on aws-emulator (measured, stand-in)"
else PLACE="kb-bedrock"; LABEL="KB on Bedrock (measured)"; fi
today="$(date +%F)"
EVAL_ENV=(AILAB_EVAL=1 AILAB_EVAL_JUDGE=none AILAB_EVAL_ANSWERS=llama KnowledgeBase__Id="$KB_ID")

log "Retrieval on the KB ($PLACE)"
run env "${EVAL_ENV[@]}" dotnet test AiLab.Evals --filter "FullyQualifiedName~KnowledgeBaseRetrievalEvalTests" \
  || die "The KB retrieval run failed: an AWS error writes no report (it never reads as 0 hits). Read the test output."
report="eval-runs/retrieval/$today-$PLACE.md"
if ! is_dry; then
  head -1 "$report" | grep -qF "$LABEL" || die "$report isn't labelled '$LABEL': check the endpoint variables."
  grep -A4 '^## Overall' "$report" | tail -1
  sed -n '/^## Not located/,$p' "$report" | head -8
fi
note "read Not located before any number: an unlocated passage counts as a miss"

log "pgvector, the same day (Postgres + Ollama)"
require_postgres
require_ollama
run env AILAB_EVAL=1 AILAB_EVAL_JUDGE=none AILAB_EVAL_ANSWERS=llama \
  dotnet test AiLab.Evals --filter "FullyQualifiedName~AiLab.Evals.RetrievalEvalTests.Retrieval_hit_rate" \
  || die "The pgvector retrieval run failed."
ok "eval-runs/retrieval/$today-{vector,keyword,hybrid}.md"

confirm_billed "RetrieveAndGenerate × 90 (30 rows × 3 runs) with $MODEL_ARN, plus Titan for each query" \
  "cents (Nova Lite, small prompts); the service reports no tokens, so read the bill"
log "Answers on the KB (execution $PLACE)"
[[ ! -d "eval-runs/meai/results/$PLACE" ]] || is_dry \
  || die "eval-runs/meai/results/$PLACE already exists: move it aside first (the reports read every run in it)."
run env "${EVAL_ENV[@]}" AILAB_EVAL_RUNS=3 KnowledgeBase__ModelArn="$MODEL_ARN" \
  dotnet test AiLab.Evals --filter "FullyQualifiedName~KnowledgeBaseAskEvalTests" \
  || die "The KB answer run failed. Read the test output."
ok "eval-runs/meai/$PLACE-summary.md (cost shows ? by design: no usage from the service)"

if is_emulator; then
  note "expected on the emulator: retrieval 7/24 (one cut-off passage per file), answers 3/16, abstains 0/4: a retrieval proxy"
else
  manual "Before reading abstains: real-account-gaps.md step 7 (teach GoldCheck.AbstainPhrases Bedrock's refusal wording,"
  echo "    then re-score with AILAB_EVAL_GOLDCHECK=$PLACE dotnet test AiLab.Evals --filter Write_goldcheck: no new call)."
  echo "    Then step 8: fill the 'KB on Bedrock' rows of docs/evals/managed-vs-own-rag-2026-11-24.md and apply the rule."
fi
