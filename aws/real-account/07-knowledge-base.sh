#!/usr/bin/env bash
# 07-knowledge-base.sh: week 8 Monday (+ Tuesday's rebuild over all of data/corpus). A Bedrock Knowledge Base over the
# same 4 documents as the pgvector RAG: S3 bucket → S3 Vectors index (1024, cosine) → service role → KB (Titan v2) →
# S3 data source (default chunking) → ingestion. Then ai-lab-dev gets Retrieve / RetrieveAndGenerate on this KB only.
# Replays: docker/bedrock-kb/bedrock-kb-emulator.sh steps 4–10 and docker/aws-setup/real-account-gaps.md "Week 8"
# steps 2–3, with the service role narrowed (the emulator script grants s3vectors:* and bedrock:InvokeModel on *).
# Re-runnable: existing resources are reused; each run starts a new ingestion job.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

use_identity admin
kb_names
cd "$REPO_ROOT"
DOCS_DIR="$REPO_ROOT/data/corpus"
VECTOR_BUCKET_ARN="arn:aws:s3vectors:${REGION}:${ACCOUNT_ID}:bucket/${VECTOR_BUCKET}"
INDEX_ARN="${VECTOR_BUCKET_ARN}/index/${INDEX_NAME}"
ROLE_ARN="arn:aws:iam::${ACCOUNT_ID}:role/${ROLE_NAME}"

confirm_billed "a Knowledge Base: S3 + S3 Vectors storage (kept until 99-teardown.sh) and Titan tokens for the corpus (about 9k)" \
  "cents per month while it exists (check S3 Vectors pricing that day; never an OpenSearch Serverless store)"

log "Docs bucket s3://$DOCS_BUCKET"
if is_dry || ! aws s3api head-bucket --bucket "$DOCS_BUCKET" >/dev/null 2>&1; then
  run aws s3 mb "s3://$DOCS_BUCKET" --region "$REGION"
fi
# --delete: the bucket mirrors the folder, so no stale keys from an earlier run are indexed
run aws s3 sync "$DOCS_DIR" "s3://$DOCS_BUCKET/" --delete --exclude ".*" --exclude "*/.*" >/dev/null
keys="$(cap KEYS aws s3 ls "s3://$DOCS_BUCKET/" --recursive | awk '{print $4}' | sort | tr '\n' ' ')"
expected="README.md docs/architecture.md docs/contracts.md docs/decisions.md "
is_dry || [[ "$keys" == "$expected" ]] || die "Bucket keys are '$keys', expected '$expected' (the golden set's gold.file paths)."
ok "4 keys, the golden set's paths"

log "S3 Vectors: $VECTOR_BUCKET / $INDEX_NAME (1024, cosine)"
if is_dry || ! aws s3vectors get-vector-bucket --vector-bucket-name "$VECTOR_BUCKET" >/dev/null 2>&1; then
  run aws s3vectors create-vector-bucket --vector-bucket-name "$VECTOR_BUCKET"
fi
if is_dry || ! aws s3vectors get-index --vector-bucket-name "$VECTOR_BUCKET" --index-name "$INDEX_NAME" >/dev/null 2>&1; then
  run aws s3vectors create-index --vector-bucket-name "$VECTOR_BUCKET" --index-name "$INDEX_NAME" \
    --data-type float32 --dimension 1024 --distance-metric cosine
fi
ok "$INDEX_ARN"

log "Service role $ROLE_NAME (narrowed)"
trust="$(jq -nc --arg acct "$ACCOUNT_ID" --arg region "$REGION" '{
  Version: "2012-10-17",
  Statement: [{ Effect: "Allow", Principal: { Service: "bedrock.amazonaws.com" }, Action: "sts:AssumeRole",
    Condition: { StringEquals: { "aws:SourceAccount": $acct },
                 ArnLike: { "aws:SourceArn": "arn:aws:bedrock:\($region):\($acct):knowledge-base/*" } } }] }')"
access="$(jq -nc --arg bucket "$DOCS_BUCKET" --arg index "$INDEX_ARN" --arg titan "$TITAN_ARN" '{
  Version: "2012-10-17",
  Statement: [
    { Sid: "ReadDocs", Effect: "Allow", Action: ["s3:ListBucket"], Resource: ["arn:aws:s3:::\($bucket)"] },
    { Sid: "ReadDocObjects", Effect: "Allow", Action: ["s3:GetObject"], Resource: ["arn:aws:s3:::\($bucket)/*"] },
    { Sid: "UseVectorIndex", Effect: "Allow",
      Action: ["s3vectors:PutVectors", "s3vectors:GetVectors", "s3vectors:DeleteVectors", "s3vectors:QueryVectors", "s3vectors:GetIndex"],
      Resource: [$index] },
    { Sid: "EmbedWithTitan", Effect: "Allow", Action: ["bedrock:InvokeModel"], Resource: [$titan] } ] }')"
new_role=0
if is_dry || ! aws iam get-role --role-name "$ROLE_NAME" >/dev/null 2>&1; then
  run aws iam create-role --role-name "$ROLE_NAME" --assume-role-policy-document "$trust" --query Role.Arn --output text
  new_role=1
else
  run aws iam update-assume-role-policy --role-name "$ROLE_NAME" --policy-document "$trust"
fi
run aws iam put-role-policy --role-name "$ROLE_NAME" --policy-name kb-access --policy-document "$access"
if [[ "$new_role" == 1 ]] && ! is_emulator && ! is_dry; then
  note "waiting 15 s: a new IAM role takes a moment before Bedrock can assume it"; sleep 15
fi
ok "$ROLE_ARN"

log "Knowledge Base $KB_NAME"
KB_ID="$(cap KB_ID aws bedrock-agent list-knowledge-bases \
  --query "knowledgeBaseSummaries[?name=='$KB_NAME'].knowledgeBaseId | [0]" --output text 2>/dev/null || true)"
if ! found "$KB_ID"; then
  KB_ID="$(cap KB_ID aws bedrock-agent create-knowledge-base --name "$KB_NAME" --role-arn "$ROLE_ARN" \
    --knowledge-base-configuration "{\"type\":\"VECTOR\",\"vectorKnowledgeBaseConfiguration\":{\"embeddingModelArn\":\"$TITAN_ARN\"}}" \
    --storage-configuration "{\"type\":\"S3_VECTORS\",\"s3VectorsConfiguration\":{\"vectorBucketArn\":\"$VECTOR_BUCKET_ARN\",\"indexName\":\"$INDEX_NAME\"}}" \
    --query knowledgeBase.knowledgeBaseId --output text)"
fi
if ! is_dry; then
  for _ in $(seq 1 60); do
    status="$(aws bedrock-agent get-knowledge-base --knowledge-base-id "$KB_ID" --query knowledgeBase.status --output text)"
    [[ "$status" == "ACTIVE" ]] && break
    [[ "$status" == "FAILED" ]] && die "Knowledge Base FAILED: aws bedrock-agent get-knowledge-base --knowledge-base-id $KB_ID"
    sleep 2
  done
  [[ "$status" == "ACTIVE" ]] || die "Knowledge Base still $status after 2 minutes."
fi
state_set KB_ID "$KB_ID"; state_set DOCS_BUCKET "$DOCS_BUCKET"; state_set VECTOR_BUCKET "$VECTOR_BUCKET"
ok "KB $KB_ID"

log "Data source $DS_NAME (default chunking: about 300 tokens)"
DS_ID="$(cap DS_ID aws bedrock-agent list-data-sources --knowledge-base-id "$KB_ID" \
  --query "dataSourceSummaries[?name=='$DS_NAME'].dataSourceId | [0]" --output text 2>/dev/null || true)"
if ! found "$DS_ID"; then
  DS_ID="$(cap DS_ID aws bedrock-agent create-data-source --knowledge-base-id "$KB_ID" --name "$DS_NAME" \
    --data-source-configuration "{\"type\":\"S3\",\"s3Configuration\":{\"bucketArn\":\"arn:aws:s3:::$DOCS_BUCKET\"}}" \
    --query dataSource.dataSourceId --output text)"
fi
state_set DS_ID "$DS_ID"
ok "data source $DS_ID"

log "Ingestion"
JOB_ID="$(cap JOB_ID aws bedrock-agent start-ingestion-job --knowledge-base-id "$KB_ID" --data-source-id "$DS_ID" \
  --query ingestionJob.ingestionJobId --output text)"
if ! is_dry; then
  for _ in $(seq 1 150); do
    job="$(aws bedrock-agent get-ingestion-job --knowledge-base-id "$KB_ID" --data-source-id "$DS_ID" \
      --ingestion-job-id "$JOB_ID" --query ingestionJob.status --output text)"
    [[ "$job" == "COMPLETE" || "$job" == "FAILED" || "$job" == "STOPPED" ]] && break
    sleep 2
  done
  stats="$(aws bedrock-agent get-ingestion-job --knowledge-base-id "$KB_ID" --data-source-id "$DS_ID" \
    --ingestion-job-id "$JOB_ID" --query 'ingestionJob.{status:status,stats:statistics,failures:failureReasons}' --output json)"
  echo "$stats" | jq -r '"    status \(.status), scanned \(.stats.numberOfDocumentsScanned), indexed \(.stats.numberOfNewDocumentsIndexed + .stats.numberOfModifiedDocumentsIndexed), failed \(.stats.numberOfDocumentsFailed)"'
  [[ "$job" == "COMPLETE" ]] || die "Ingestion ended $job."
  [[ "$(echo "$stats" | jq '.stats.numberOfDocumentsScanned')" == 4 && "$(echo "$stats" | jq '.stats.numberOfDocumentsFailed')" == 0 ]] \
    || die "Expected 4 scanned and 0 failed."
fi
ok "4 scanned, 0 failed (job $JOB_ID)"

log "ai-lab-dev may query this KB only"
policy="$(mktemp "${TMPDIR:-/tmp}/ailab-policy.XXXXXX")"
trap 'rm -f "$policy"' EXIT
POLICY_KB_ID="$KB_ID"
policy_render "$policy"
policy_apply "$policy"
simulator_cases | simulate_cases principal

log "CLI check: retrieve (scores are relevance: higher is better, never a distance)"
run aws bedrock-agent-runtime retrieve --knowledge-base-id "$KB_ID" \
  --retrieval-query '{"text":"What does the outbox pattern guarantee?"}' \
  --query 'retrievalResults[].{score:score,uri:location.s3Location.uri}' --output table

if is_emulator; then
  log "AiLab.Bedrock (emulator only: its ServiceURL is the emulator's)"
  run env KB_ID="$KB_ID" dotnet run --project AiLab.Bedrock -- --no-followup "What does the outbox pattern guarantee?" \
    || note "AiLab.Bedrock failed: see its output"
else
  note "AiLab.Bedrock is wired to the emulator only; 08 queries this KB through AiLab.Evals' adapter instead"
fi
