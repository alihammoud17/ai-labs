#!/usr/bin/env bash
# 05-rag-titan.sh: week 7 Wednesday, D2. AiLab.Rag on Bedrock by configuration only: the corpus re-embedded with
# Titan Text Embeddings v2 into its own collection (corpus_titan_v2_1024; `corpus` is never written), then retrieval
# measured nomic vs Titan.
# Replays: docker/aws-setup/README.md "Re-embed the corpus on Bedrock", steps 2–6 (7–9 stay manual).
# The emulator serves no embedding inference (HTTP 424), so on TARGET=emulator this stops at step 2.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

use_identity dev
cd "$REPO_ROOT"
require_postgres
TITAN_COLLECTION="corpus_titan_v2_1024"

log "One embedding by hand"
emb="$(mktemp "${TMPDIR:-/tmp}/emb.XXXXXX")"
rag_pid=""
cleanup() { rm -f "$emb"; [[ -z "$rag_pid" ]] || kill "$rag_pid" 2>/dev/null || true; }
trap cleanup EXIT
run aws bedrock-runtime invoke-model --model-id "$TITAN_MODEL" \
  --body '{"inputText":"hello","dimensions":1024}' --cli-binary-format raw-in-base64-out "$emb" \
  || emulator_limit "Titan answered with an error (the emulator serves no embedding inference: HTTP 424). Nothing below can run here."
if ! is_dry; then
  n="$(jq '.embedding | length' "$emb")"
  [[ "$n" == "1024" ]] || die "Titan returned $n numbers, not 1024."
  ok "1024 numbers, $(jq .inputTextTokenCount "$emb") token(s)"
fi

log "Start AiLab.Rag (launch profile bedrock)"
if lsof -nP -iTCP:5074 -sTCP:LISTEN >/dev/null 2>&1; then
  die "Port 5074 is taken (your own Rag run?). Stop it first: this script only stops the process it starts."
fi
rag_log="${TMPDIR:-/tmp}/ailab-rag-titan.log"
if is_dry; then
  run dotnet run --project AiLab.Rag --launch-profile bedrock
else
  dotnet run --project AiLab.Rag --launch-profile bedrock > "$rag_log" 2>&1 &
  rag_pid=$!
  for _ in $(seq 1 60); do curl -sf localhost:5074/health >/dev/null 2>&1 && break; sleep 2; done
  curl -sf localhost:5074/health >/dev/null || die "Rag didn't start: see $rag_log"
  line="$(grep -m1 'Environment Bedrock' "$rag_log" || true)"
  echo "    $line"
  [[ "$line" == *"$TITAN_COLLECTION, 1024 dims"* && "$line" == *"embeddings bedrock/$TITAN_MODEL"* ]] \
    || die "The startup line doesn't show Titan into $TITAN_COLLECTION."
  ok "Rag on Titan (pid $rag_pid, log $rag_log)"
fi

confirm_billed "ingest the corpus with Titan: 71 chunks, one InvokeModel each (about 9k tokens)" "a fraction of a cent"
log "Ingest"
run curl -sf -X POST localhost:5074/dev/ingest -o "${TMPDIR:-/tmp}/ailab-ingest.json" \
  || die "Ingest failed (a 502 embedding_unavailable names the keys to check; ingesting again carries on)."
is_dry || jq '.totals' "${TMPDIR:-/tmp}/ailab-ingest.json"

log "Chunk counts: Titan vs nomic"
psql_in() { run docker compose exec -T vectors psql -U ailab -d ailab -At -c "$1"; }
psql_in "select '$TITAN_COLLECTION', \"Chunker\", count(*) from $TITAN_COLLECTION group by 2 order by 2"
psql_in "select 'corpus', \"Chunker\", count(*) from corpus where \"Chunker\" <> 'lines' group by 2 order by 2"
note "the two must match per chunker (heading 50, fixed 21)"

kill "$rag_pid" 2>/dev/null || true; rag_pid=""
ok "Rag stopped"

log "Retrieval: nomic, then Titan (no Anthropic key: GET /search per mode)"
require_ollama
today="$(date +%F)"
FILTER="FullyQualifiedName~AiLab.Evals.RetrievalEvalTests.Retrieval_hit_rate"
run env AILAB_EVAL=1 AILAB_EVAL_JUDGE=none AILAB_EVAL_ANSWERS=llama dotnet test AiLab.Evals --filter "$FILTER" || note "nomic run failed: read its output"
run mkdir -p eval-runs/retrieval/nomic
is_dry || mv eval-runs/retrieval/"$today"-{vector,keyword,hybrid}.md eval-runs/retrieval/nomic/ 2>/dev/null || true
run env AILAB_EVAL=1 AILAB_EVAL_JUDGE=none AILAB_EVAL_ANSWERS=llama \
  Embedding__Provider=bedrock Embedding__Model="$TITAN_MODEL" Embedding__Dimensions=1024 Embedding__UseTaskPrefixes=false \
  VectorStore__Collection="$TITAN_COLLECTION" Bedrock__Region="$REGION" \
  dotnet test AiLab.Evals --filter "$FILTER" \
  || note "expected: the Titan vector test fails on \"Vector ranks differ from Tuesday's\" (pinned to nomic); its report is written first"
ok "reports: eval-runs/retrieval/nomic/$today-*.md (nomic) and eval-runs/retrieval/$today-*.md (Titan)"

manual "Runbook steps 7–9: re-tune Ask:MaxDistance for Titan, price the embedding tokens, record a new experiments.md section."
