#!/usr/bin/env bash
# bedrock-kb-emulator.sh
#
# Managed RAG on AWS — fully local, no AWS account.
# Spins up the official aws-emulator in Docker, creates a Bedrock Knowledge Base
# over your docs (S3 + S3 Vectors), ingests them, then scaffolds and runs a C#
# console app that calls RetrieveAndGenerate via AWSSDK.BedrockAgentRuntime.
#
# Usage:
#   ./bedrock-kb-emulator.sh [docs_dir] ["question"]
#   ./bedrock-kb-emulator.sh stop          # stop the emulator container
#
# Defaults: docs_dir=<repo>/data/corpus/docs, question="What is this documentation about?"
# Re-runnable: existing resources are reused.
#
# Notes:
#   - The emulator runs no LLM: the answer text is a fixed notice + the retrieved
#     passages quoted verbatim. Retrieval and citations are real (keyword-based).
#   - Everything is wiped when the container stops (emulator state is in-memory).

set -euo pipefail

# Repo root, worked out from this script's own location (docker/bedrock-kb/), so it runs from any folder.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# ---------- config (override with env vars) ----------
DOCS_DIR="${1:-${DOCS_DIR:-$REPO_ROOT/data/corpus/docs}}"
QUESTION="${2:-${QUESTION:-What is this documentation about?}}"
PORT="${PORT:-9297}"
CONTAINER="${CONTAINER:-aws-emulator}"
IMAGE="${IMAGE:-aws-emulator}"
BUILD_DIR="${BUILD_DIR:-$HOME/.aws-emulator-build}"
APP_DIR="${APP_DIR:-$HOME/ai-lab-kb-client}"
REGION="us-east-1"
ACCOUNT="000000000000"

DOCS_BUCKET="ai-lab-docs"
VECTOR_BUCKET="ai-lab-vectors"
INDEX_NAME="docs"
ROLE_NAME="BedrockKbRole"
KB_NAME="ai-lab-kb"
DS_NAME="docs-s3"
EMBED_MODEL_ARN="arn:aws:bedrock:${REGION}::foundation-model/amazon.titan-embed-text-v2:0"
GEN_MODEL_ARN="arn:aws:bedrock:${REGION}::foundation-model/amazon.nova-lite-v1:0"
EMU_DIR="$HOME/.aws/emulator/$PORT"
ENV_FILE="$HOME/.aws/emulator/ai-lab-kb.env"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
ok()   { printf '    \033[32m✓\033[0m %s\n' "$*"; }
die()  { printf '\n\033[1;31m✗ %s\033[0m\n' "$*" >&2; exit 1; }

# ---------- stop subcommand ----------
if [[ "${1:-}" == "stop" ]]; then
  docker stop "$CONTAINER" >/dev/null 2>&1 && echo "Emulator stopped." || echo "Emulator was not running."
  exit 0
fi

# ---------- 0. prerequisites ----------
log "Checking prerequisites"
command -v docker >/dev/null || die "Docker is required."
docker info >/dev/null 2>&1 || die "Docker is installed but not running — start Docker Desktop/OrbStack/Colima."
command -v aws >/dev/null    || die "AWS CLI v2 is required (brew install awscli)."
aws s3vectors help >/dev/null 2>&1 || die "Your AWS CLI has no 's3vectors' command — upgrade it (brew upgrade awscli)."
command -v curl >/dev/null   || die "curl is required."
[[ -d "$DOCS_DIR" ]] || die "Docs folder not found: $DOCS_DIR  (pass it as the first argument)"
[[ -n "$(find "$DOCS_DIR" -type f -not -name '.*' | head -1)" ]] || die "Docs folder is empty: $DOCS_DIR"
ok "docker, aws cli, docs at $DOCS_DIR"

# ---------- 1. build the emulator image ----------
log "Building emulator image (first run only)"
if docker image inspect "$IMAGE" >/dev/null 2>&1; then
  ok "image '$IMAGE' already exists"
else
  case "$(uname -m)" in
    arm64|aarch64) RPM_ARCH="aarch64" ;;
    x86_64|amd64)  RPM_ARCH="x86_64" ;;
    *) die "Unsupported CPU architecture: $(uname -m)" ;;
  esac
  mkdir -p "$BUILD_DIR"
  cat > "$BUILD_DIR/Dockerfile" <<EOF
FROM public.ecr.aws/amazonlinux/amazonlinux:2023-minimal
ADD https://downloads.aws.com/aws-emulator/packages/aws-emulator/LTS/latest/aws-emulator-${RPM_ARCH}.rpm /tmp/aws-emulator.rpm
RUN rpm -i /tmp/aws-emulator.rpm && rm /tmp/aws-emulator.rpm \\
    && mkdir -p /home/emulator && chown 1000 /home/emulator
USER 1000
ENV HOME=/home/emulator
EXPOSE 9297
ENTRYPOINT ["aws-emulator", "-b", "0.0.0.0"]
EOF
  docker build -t "$IMAGE" "$BUILD_DIR"
  ok "image built ($RPM_ARCH)"
fi

# ---------- 2. start the emulator ----------
log "Starting emulator on https://127.0.0.1:$PORT"
mkdir -p "$HOME/.aws/emulator"
if docker ps --format '{{.Names}}' | grep -qx "$CONTAINER"; then
  ok "already running"
else
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  docker run -d --rm --name "$CONTAINER" --memory=2g --memory-swap=2g \
    -p "127.0.0.1:$PORT:$PORT" \
    -e HOME="$HOME" -v "$HOME/.aws/emulator:$HOME/.aws/emulator" \
    "$IMAGE" -p "$PORT" --telemetry off >/dev/null
  ok "container started"
fi

printf '    waiting for health check'
for _ in $(seq 1 60); do
  if [[ -f "$EMU_DIR/ca.pem" ]] && curl -fs --cacert "$EMU_DIR/ca.pem" "https://127.0.0.1:$PORT/__admin/health" >/dev/null 2>&1; then
    printf '\n'; ok "emulator healthy"; break
  fi
  printf '.'; sleep 2
done
curl -fs --cacert "$EMU_DIR/ca.pem" "https://127.0.0.1:$PORT/__admin/health" >/dev/null 2>&1 \
  || { echo; docker logs --tail 30 "$CONTAINER" 2>&1 || true; die "Emulator did not become healthy."; }

# ---------- 3. point the AWS CLI / SDK at it ----------
log "Configuring environment"
unset AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_SESSION_TOKEN AWS_PROFILE
export AWS_ENDPOINT_URL="https://127.0.0.1:$PORT"
export AWS_SHARED_CREDENTIALS_FILE="$EMU_DIR/emulator_credentials"
export AWS_CONFIG_FILE="$EMU_DIR/config"
export AWS_CA_BUNDLE="$EMU_DIR/ca.pem"
export SSL_CERT_FILE="$EMU_DIR/ca.pem"
export AWS_EC2_METADATA_DISABLED=true
export AWS_DISABLE_HOST_PREFIX_INJECTION=true
export AWS_REGION="$REGION" AWS_DEFAULT_REGION="$REGION"
export AWS_PAGER=""

ACCT_SEEN="$(aws sts get-caller-identity --query Account --output text)"
[[ "$ACCT_SEEN" == "$ACCOUNT" ]] || die "Not talking to the emulator (account=$ACCT_SEEN). Aborting to protect real AWS."
ok "talking to emulator (account $ACCOUNT)"

# ---------- 4. S3 bucket with the docs ----------
log "Uploading docs to s3://$DOCS_BUCKET"
aws s3api head-bucket --bucket "$DOCS_BUCKET" >/dev/null 2>&1 || aws s3 mb "s3://$DOCS_BUCKET" >/dev/null
aws s3 sync "$DOCS_DIR" "s3://$DOCS_BUCKET/" --exclude ".*" --exclude "*/.*" >/dev/null
ok "$(aws s3 ls "s3://$DOCS_BUCKET/" --recursive | wc -l | tr -d ' ') object(s) in bucket"

# ---------- 5. S3 Vectors bucket + index ----------
log "Creating vector store (S3 Vectors, dim 1024, cosine)"
aws s3vectors get-vector-bucket --vector-bucket-name "$VECTOR_BUCKET" >/dev/null 2>&1 \
  || aws s3vectors create-vector-bucket --vector-bucket-name "$VECTOR_BUCKET" >/dev/null
aws s3vectors get-index --vector-bucket-name "$VECTOR_BUCKET" --index-name "$INDEX_NAME" >/dev/null 2>&1 \
  || aws s3vectors create-index --vector-bucket-name "$VECTOR_BUCKET" --index-name "$INDEX_NAME" \
       --data-type float32 --dimension 1024 --distance-metric cosine >/dev/null
ok "vector bucket '$VECTOR_BUCKET', index '$INDEX_NAME'"

# ---------- 6. IAM role for Bedrock ----------
log "Creating IAM role $ROLE_NAME"
if ! aws iam get-role --role-name "$ROLE_NAME" >/dev/null 2>&1; then
  aws iam create-role --role-name "$ROLE_NAME" --assume-role-policy-document \
    '{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"Service":"bedrock.amazonaws.com"},"Action":"sts:AssumeRole"}]}' >/dev/null
fi
aws iam put-role-policy --role-name "$ROLE_NAME" --policy-name kb-access --policy-document "{
  \"Version\":\"2012-10-17\",
  \"Statement\":[
    {\"Effect\":\"Allow\",\"Action\":[\"s3:GetObject\",\"s3:ListBucket\"],\"Resource\":[\"arn:aws:s3:::$DOCS_BUCKET\",\"arn:aws:s3:::$DOCS_BUCKET/*\"]},
    {\"Effect\":\"Allow\",\"Action\":[\"s3vectors:*\"],\"Resource\":\"*\"},
    {\"Effect\":\"Allow\",\"Action\":[\"bedrock:InvokeModel\"],\"Resource\":\"*\"}
  ]}" >/dev/null
ROLE_ARN="arn:aws:iam::$ACCOUNT:role/$ROLE_NAME"
ok "$ROLE_ARN"

# ---------- 7. Knowledge Base ----------
log "Creating Knowledge Base $KB_NAME"
KB_ID="$(aws bedrock-agent list-knowledge-bases \
  --query "knowledgeBaseSummaries[?name=='$KB_NAME'].knowledgeBaseId | [0]" --output text 2>/dev/null || true)"
if [[ -z "$KB_ID" || "$KB_ID" == "None" ]]; then
  KB_ID="$(aws bedrock-agent create-knowledge-base --name "$KB_NAME" --role-arn "$ROLE_ARN" \
    --knowledge-base-configuration "{\"type\":\"VECTOR\",\"vectorKnowledgeBaseConfiguration\":{\"embeddingModelArn\":\"$EMBED_MODEL_ARN\"}}" \
    --storage-configuration "{\"type\":\"S3_VECTORS\",\"s3VectorsConfiguration\":{\"vectorBucketArn\":\"arn:aws:s3vectors:$REGION:$ACCOUNT:bucket/$VECTOR_BUCKET\",\"indexName\":\"$INDEX_NAME\"}}" \
    --query knowledgeBase.knowledgeBaseId --output text)"
fi
printf '    waiting for ACTIVE'
for _ in $(seq 1 30); do
  KB_STATUS="$(aws bedrock-agent get-knowledge-base --knowledge-base-id "$KB_ID" --query knowledgeBase.status --output text)"
  [[ "$KB_STATUS" == "ACTIVE" ]] && break
  [[ "$KB_STATUS" == "FAILED" ]] && { echo; die "Knowledge Base FAILED."; }
  printf '.'; sleep 2
done
printf '\n'; ok "KB $KB_ID ($KB_STATUS)"

# ---------- 8. Data source ----------
log "Connecting S3 data source"
DS_ID="$(aws bedrock-agent list-data-sources --knowledge-base-id "$KB_ID" \
  --query "dataSourceSummaries[?name=='$DS_NAME'].dataSourceId | [0]" --output text 2>/dev/null || true)"
if [[ -z "$DS_ID" || "$DS_ID" == "None" ]]; then
  DS_ID="$(aws bedrock-agent create-data-source --knowledge-base-id "$KB_ID" --name "$DS_NAME" \
    --data-source-configuration "{\"type\":\"S3\",\"s3Configuration\":{\"bucketArn\":\"arn:aws:s3:::$DOCS_BUCKET\"}}" \
    --query dataSource.dataSourceId --output text)"
fi
ok "data source $DS_ID"

# ---------- 9. Ingestion ----------
log "Ingesting documents"
JOB_ID="$(aws bedrock-agent start-ingestion-job --knowledge-base-id "$KB_ID" --data-source-id "$DS_ID" \
  --query ingestionJob.ingestionJobId --output text)"
printf '    job %s' "$JOB_ID"
for _ in $(seq 1 90); do
  JOB_STATUS="$(aws bedrock-agent get-ingestion-job --knowledge-base-id "$KB_ID" --data-source-id "$DS_ID" \
    --ingestion-job-id "$JOB_ID" --query ingestionJob.status --output text)"
  [[ "$JOB_STATUS" == "COMPLETE" || "$JOB_STATUS" == "FAILED" || "$JOB_STATUS" == "STOPPED" ]] && break
  printf '.'; sleep 2
done
printf '\n'
aws bedrock-agent get-ingestion-job --knowledge-base-id "$KB_ID" --data-source-id "$DS_ID" \
  --ingestion-job-id "$JOB_ID" --query 'ingestionJob.{status:status,stats:statistics,failures:failureReasons}' --output json
[[ "$JOB_STATUS" == "COMPLETE" ]] || die "Ingestion ended with status $JOB_STATUS."
ok "ingestion complete"

# Save env for later sessions:  source ~/.aws/emulator/ai-lab-kb.env
cat > "$ENV_FILE" <<EOF
unset AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_SESSION_TOKEN AWS_PROFILE
export AWS_ENDPOINT_URL="$AWS_ENDPOINT_URL"
export AWS_SHARED_CREDENTIALS_FILE="$AWS_SHARED_CREDENTIALS_FILE"
export AWS_CONFIG_FILE="$AWS_CONFIG_FILE"
export AWS_CA_BUNDLE="$AWS_CA_BUNDLE"
export SSL_CERT_FILE="$SSL_CERT_FILE"
export AWS_EC2_METADATA_DISABLED=true
export AWS_DISABLE_HOST_PREFIX_INJECTION=true
export AWS_REGION="$REGION" AWS_DEFAULT_REGION="$REGION"
export AWS_PAGER=""
export KB_ID="$KB_ID"
export GEN_MODEL_ARN="$GEN_MODEL_ARN"
export EMULATOR_PORT="$PORT"
EOF
ok "env saved to $ENV_FILE"

# ---------- 10. Quick CLI sanity check ----------
log "CLI check: retrieve"
aws bedrock-agent-runtime retrieve --knowledge-base-id "$KB_ID" \
  --retrieval-query "{\"text\":\"$QUESTION\"}" \
  --query 'retrievalResults[].{score:score,uri:location.s3Location.uri}' --output table || true

# ---------- 11. C# client ----------
if ! command -v dotnet >/dev/null; then
  log "dotnet not found — skipping the C# client. KB_ID=$KB_ID"
  exit 0
fi

log "Scaffolding C# client in $APP_DIR"
if [[ ! -f "$APP_DIR/KbClient.csproj" ]]; then
  mkdir -p "$APP_DIR"
  (cd "$APP_DIR" && dotnet new console -n KbClient -o . --force >/dev/null \
     && dotnet add package AWSSDK.BedrockAgentRuntime >/dev/null)
  ok "project created"
else
  ok "project exists"
fi

cat > "$APP_DIR/Program.cs" <<'CSHARP'
using System.Security.Cryptography.X509Certificates;
using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;

var kbId     = Environment.GetEnvironmentVariable("KB_ID") ?? throw new("KB_ID not set");
var modelArn = Environment.GetEnvironmentVariable("GEN_MODEL_ARN") ?? throw new("GEN_MODEL_ARN not set");
var port     = Environment.GetEnvironmentVariable("EMULATOR_PORT") ?? "9297";
var question = args.Length > 0 ? string.Join(' ', args) : "What is this documentation about?";

var config = new AmazonBedrockAgentRuntimeConfig
{
    ServiceURL = $"https://127.0.0.1:{port}",
    AuthenticationRegion = "us-east-1",
    HttpClientFactory = new EmulatorHttpClientFactory(port)
};
using var client = new AmazonBedrockAgentRuntimeClient(config);

Console.WriteLine($"Q: {question}\n");

// 1) Retrieve only — what the KB found
var retrieved = await client.RetrieveAsync(new RetrieveRequest
{
    KnowledgeBaseId = kbId,
    RetrievalQuery = new KnowledgeBaseQuery { Text = question },
    RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
    {
        VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration { NumberOfResults = 3 }
    }
});
Console.WriteLine("── Retrieve ──");
foreach (var r in retrieved.RetrievalResults ?? [])
    Console.WriteLine($"  [{r.Score:F3}] {r.Location?.S3Location?.Uri}");

// 2) RetrieveAndGenerate — managed RAG in one call
var res = await client.RetrieveAndGenerateAsync(new RetrieveAndGenerateRequest
{
    Input = new RetrieveAndGenerateInput { Text = question },
    RetrieveAndGenerateConfiguration = new RetrieveAndGenerateConfiguration
    {
        Type = RetrieveAndGenerateType.KNOWLEDGE_BASE,
        KnowledgeBaseConfiguration = new KnowledgeBaseRetrieveAndGenerateConfiguration
        {
            KnowledgeBaseId = kbId,
            ModelArn = modelArn
        }
    }
});

Console.WriteLine($"\n── RetrieveAndGenerate (session {res.SessionId}) ──\n{res.Output.Text}\n");
Console.WriteLine("── Citations ──");
foreach (var c in res.Citations ?? [])
    foreach (var r in c.RetrievedReferences ?? [])
    {
        var text = r.Content?.Text ?? "";
        Console.WriteLine($"  ↳ {r.Location?.S3Location?.Uri}: {text[..Math.Min(100, text.Length)].ReplaceLineEndings(" ")}…");
    }

// Trusts ONLY the emulator's self-signed CA (dev use). macOS .NET ignores SSL_CERT_FILE.
sealed class EmulatorHttpClientFactory(string port) : Amazon.Runtime.HttpClientFactory
{
    public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig)
    {
        var caPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                  ".aws", "emulator", port, "ca.pem");
        var trusted = LoadPemCertificates(caPath);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
            {
                if (cert is null || chain is null) return false;
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(trusted);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(cert);
            }
        };
        return new HttpClient(handler);
    }

    // Certificate blocks only: CreateFromPemFile would also demand a private key, which a CA bundle doesn't have.
    static X509Certificate2Collection LoadPemCertificates(string path)
    {
        var certs = new X509Certificate2Collection();
        var rest = File.ReadAllText(path);
        while (System.Security.Cryptography.PemEncoding.TryFind(rest, out var pem))
        {
            if (rest[pem.Label] == "CERTIFICATE")
                certs.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(rest[pem.Base64Data])));
            rest = rest[pem.Location.End..];
        }
        if (certs.Count == 0) throw new InvalidOperationException($"No certificate found in {path}");
        return certs;
    }
}
CSHARP
ok "Program.cs written"

export KB_ID GEN_MODEL_ARN EMULATOR_PORT="$PORT"
log "Running C# client"
(cd "$APP_DIR" && dotnet run -- "$QUESTION")

log "Done"
cat <<EOF
    KB_ID          $KB_ID
    C# project     $APP_DIR
    Ask again      source $ENV_FILE && (cd $APP_DIR && dotnet run -- "your question")
    Stop emulator  $REPO_ROOT/docker/bedrock-kb/bedrock-kb-emulator.sh stop   (all state is lost on stop)
EOF
