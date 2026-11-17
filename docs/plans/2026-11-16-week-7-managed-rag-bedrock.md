# Managed RAG Day Plan: Bedrock Knowledge Bases on aws-emulator

- **Date:** Mon 16 Nov 2026 (worked on Tue 17 Nov)
- **Branch:** `stage-7-managed-rag` (from `main`)
- **Status:** in progress. Both roadmap deliverables work outside the repo; the repo work hasn't started.
- **Focus:** Managed RAG on AWS: a Bedrock Knowledge Base over the same corpus, queried from C#
- **Where:** ai-labs: the setup script in `docker/bedrock-kb/`, the client as a new presentation host `AiLab.Bedrock` in `AiLab.slnx`

Tutor mode: I type the commands and write the code; Claude explains each step, reviews it (build, tests) and ticks the boxes. Claude writes a step's code only when I say "hand it over". Exception: `~/bedrock-kb-emulator.sh` and the generated `~/ai-lab-kb-client` were handed over before this plan.

After each finished step, Claude adds a short **How we did it** note under it when I ask.

## Where things stand

| Block | Status | In the repo? |
|---|---|---|
| 1. Concepts | not done | no (notes) |
| 2. Build the Knowledge Base | **done** (2.3 reading left) | no, runs from `~/` |
| 3. Query from C# | **done** | `AiLab.Bedrock` (3.3–3.4 not committed yet) |
| 4. Adapter behind `IAskService` | not done (should-have) | — |
| 5. Stretch | not done (optional) | — |
| 6. Housekeeping + notes | 6.1 **done** (PR #13); 6.2 not done | yes |

**Minimum to finish today:** 3.2 → 3.3 → 3.4 → 6.1. Then 4 if there's time.

## Deliverables
- [x] **D1. Knowledge Base over the same docs:** `data/corpus/docs` ingested into a Bedrock Knowledge Base
- [x] **D2. Queried from C#:** `RetrieveAndGenerate` with citations, from the scratch client `~/ai-lab-kb-client` (moving it into ai-labs is Block 3.2)

## Resources
- AWS .NET blog, Integrating Amazon Bedrock in .NET (SDK packages section): https://aws.amazon.com/blogs/dotnet/getting-started-with-amazon-bedrock-in-net-applications/
- aws-emulator repo: https://github.com/aws/aws-emulator (known gaps: `issues/things-that-do-not-work/`)
- aws-emulator, Getting started with Docker: https://docs.aws.amazon.com/emulator/latest/userguide/getting-started-docker.html
- aws-emulator, Point your SDK at the emulator: https://docs.aws.amazon.com/emulator/latest/userguide/point-sdk-to-emulator.html

## Scope rules for today
- **No AWS account.** Everything runs against aws-emulator in Docker (Colima). Floci was ruled out: it has Bedrock Runtime only, no `bedrock-agent` / `bedrock-agent-runtime`.
- **The emulator runs no model.** Retrieval is keyword-based (term-frequency vectors), and each document is stored as **one passage** (no real chunking), cut off mid-text. `RetrieveAndGenerate` answers with a fixed notice plus the retrieved passages. Citations, `sessionId` and errors match the real API. Today checks the SDK calls and data shapes, **not answer quality**.
- Emulator state is in memory: stopping the container wipes the Knowledge Base. Re-run the script to rebuild it.
- `AiLab.Bedrock` has **no project references** today. Wiring it into the app is Block 4.

---

## Block 1: Concepts (15 min) — not done

- [x] **Knowledge Base** = data source (S3) + chunking + embedding model + vector store (S3 Vectors here).
  - **What to do:** write these four parts in your notes, each next to the ai-labs piece you built by hand: chunker → chunking config, `IEmbeddingGenerator` → embedding model ARN, pgvector → S3 Vectors, your ingestion code → the ingestion job.
  - **Why:** the whole day is "what does Bedrock do that I did myself in week 4". This map is what you explain in an interview.
- [x] **Two runtime calls:** `Retrieve` vs `RetrieveAndGenerate`.
  - **What to do:** one line each: `Retrieve` = chunks + scores, you write the prompt and call the model; `RetrieveAndGenerate` = retrieval + model answer + citations in one call.
  - **Why:** it decides who controls the prompt, the abstain logic and the model. It's also an AIF-C01 question.
- [x] **Two SDK packages:** `AWSSDK.BedrockAgent` vs `AWSSDK.BedrockAgentRuntime`.
  - **What to do:** note that the first builds things (KB, data source, ingestion; done today with the CLI) and the second queries them (your C# client).
  - **Why:** same split as build time vs query time in your own pipeline; picking the wrong package is a common first mistake.

---

## Block 2: Build the Knowledge Base → D1

### 2.1 Prerequisites
- [x] Colima running, AWS CLI v2 with `s3vectors`, .NET 10 (the script checked all three)

### 2.2 Run the setup script
- [x] `~/bedrock-kb-emulator.sh ~/claude-code-projects/ai-labs/data/corpus/docs "What architecture decisions were made?"`
- [x] Talking to the emulator (account `000000000000`), not real AWS
- [x] Ingestion `COMPLETE`: 3 scanned, 3 indexed, 0 failed
- [x] CLI `retrieve` hits all three files

> **Result (2026-11-17):** KB `LfHyKY3kWz`, data source `40rcvpKIgC`, ingestion job `JcJS76Ac1K`. Scores: architecture.md 0.042, decisions.md 0.039, contracts.md 0.023. Low scores because search matches words, not meaning; the ranking is still sensible.

### 2.3 Read what the script did — not done
- [x] Open `~/bedrock-kb-emulator.sh` and match steps 4–9 to the Block 1 map.
  - **What to do:** for each `aws` command, say in one line what it creates and which ai-labs piece it replaces.
  - **Why:** the script was handed over; reading it is how the build side becomes yours, and Block 3.2 copies it into the repo.
  - **Done (handed over):** the command-by-command map is in the appendix at the end of this plan (steps 0–11, with the ai-labs piece each one replaces). To check against your own reading.
- [x] `source ~/.aws/emulator/ai-lab-kb.env && aws bedrock-agent get-knowledge-base --knowledge-base-id $KB_ID`
  - **What to do:** find `embeddingModelArn` and `storageConfiguration` in the output.
  - **Why:** these two fields are the KB's "which model, which store" decision, the same choice you made with `Embedding:*` and pgvector.

---

## Block 3: Query from C# → D2 — done

> **Already done (scratch client):** `~/ai-lab-kb-client` ran `Retrieve` and `RetrieveAndGenerate` against the KB. The first run crashed in the handed-over certificate code (`CreateFromPemFile` expects a private key, `ca.pem` has none); fixed by loading only the certificate blocks. Then it returned session `ce3d7303-…`, the emulator notice + 3 passages, one citation per file.

### 3.1 Read the generated client — done
- [x] Read `~/ai-lab-kb-client/Program.cs` top to bottom.
  - **What to do:** find (1) `ServiceURL` + `AuthenticationRegion`, how the SDK points at the emulator; (2) `EmulatorHttpClientFactory`, which trusts only the emulator's `ca.pem`; (3) where `RetrievalResults`, `Output.Text`, `SessionId` and `Citations` are read.
  - **Why:** you retype this file in 3.2. On macOS, .NET checks certificates through the keychain and ignores `SSL_CERT_FILE`, so the factory is the part most likely to confuse you later.

### 3.2 Move it into the repo — done
- [x] Create the branch. You're on `milestone-1-readme` now:
  ```bash
  git switch main && git pull && git switch -c stage-7-managed-rag
  ```
  - **Why:** every stage gets its own branch and PR.

> **Result (2026-11-17):** the branch already existed, at `main` (`2423abf`). First commit `654bfa3`: this plan plus the convention change (day plans go straight into `docs/plans/`, `outputs/` removed). PR #12 (`milestone-1-readme`) is still open, so expect a small `README.md` conflict in "Project structure" during 6.1.
- [x] Copy the script and create the project, from the ai-labs root:
  ```bash
  mkdir -p docker/bedrock-kb && cp ~/bedrock-kb-emulator.sh docker/bedrock-kb/
  dotnet new console -n AiLab.Bedrock
  dotnet sln AiLab.slnx add AiLab.Bedrock/AiLab.Bedrock.csproj
  dotnet add AiLab.Bedrock package AWSSDK.BedrockAgentRuntime
  ```
  - **Why:** today's result has to live in the repo to count for the portfolio and to be reproducible from a clone.
- [x] Retype `Program.cs` into `AiLab.Bedrock` (don't copy-paste).
  - **Why:** tutor mode; typing the SDK calls is how you learn their shapes.
  - **Done (handed over):** I typed the setup and `EmulatorHttpClientFactory`; Claude added the `RetrieveAsync` and `RetrieveAndGenerateAsync` calls and fixed `ServiceURL` from `http://localhost` to `https://127.0.0.1` (the emulator serves HTTPS only, with a certificate for `127.0.0.1`).
- [x] Match the `.csproj` to the other projects: `net10.0`, `Nullable` and `ImplicitUsings` enabled.
  - **Why:** there's no `Directory.Build.props`, so each project sets these itself.
- [x] In the copied script, change the default docs folder to `data/corpus/docs` relative to the repo.
  - **Why:** the default `~/docs` only works on your Mac.
  - **Done (handed over):** `REPO_ROOT` is worked out from the script's own location (`${BASH_SOURCE[0]}/../..`), so it runs from any folder; the stop hint points at the repo copy; `chmod +x` so a clone can run it directly.
- [x] `dotnet build` and `dotnet test` on the whole solution.
  - **Why:** a new project must not break the existing build or tests.

> **Result (2026-11-17):** build 0 warnings, 0 errors. Tests: Api.Tests 175 passed / 2 skipped, Rag.Tests 83 passed, Evals 85 passed / 40 skipped (the opt-in evals and Postgres tests), 0 failed. `AWSSDK.BedrockAgentRuntime` 4.1.0.2. `dotnet run --project AiLab.Bedrock -- "What architecture decisions were made?"` (after `source ~/.aws/emulator/ai-lab-kb.env`): Retrieve architecture.md 0.042, decisions.md 0.039, contracts.md 0.023 (same as the CLI in 2.2); RetrieveAndGenerate session `444384f5-…`, the emulator notice, one citation per file. Still open: the script's step 11 scaffolds and overwrites `~/ai-lab-kb-client`, not `AiLab.Bedrock`.

### 3.3 Add a follow-up turn — done
- [x] Send a second `RetrieveAndGenerateRequest` with `SessionId = res.SessionId` and a question that refers back ("and how is it versioned?").
  - **Why:** the session ID is how Bedrock keeps a conversation going between calls. Your own `/ask` is single-turn, so this is new.
- [x] Check the second response returns the same `SessionId`.
  - **Why:** proves the session continued instead of a new one starting.
- [x] Try an invented session ID; expect a `ValidationException`.
  - **Why:** the service creates session IDs and the caller can't choose them. Knowing the exception type matters for Block 4's error mapping.
  - **Done (handed over):** see "How we did it: Block 3" at the end of this plan.

> **Result (2026-11-18):** turn 1 "What is the current internal billing contract version?" → session `e8acf548-…`; turn 2 "And how is it versioned?" with that ID → same `e8acf548-…`. Invented ID → `ValidationException` (code `ValidationException`, HTTP 400): "Session with Id … is not valid. Please check and try again." The follow-up's retrieval used only its own words (decisions.md rank 1): no model, so nothing rewrites "it".

### 3.4 Run the eval questions — done
- [x] Pick 3 questions from `docs/evals/ask-set-v1.md` that use words from the corpus.
  - **Why:** search here is keyword-only, so paraphrased questions will miss for reasons that have nothing to do with your code.
- [x] For each, write down the cited file and compare it with the gold source.
  - **Why:** this is the first real check of the managed path against your own eval data, and it feeds `experiments.md` section 20.
- [x] Expect misses on facts near the end of a document.
  - **Why:** the emulator keeps one cut-off passage per document. Write it down as an emulator limit, not a failure.
  - **Done (handed over):** see "How we did it: Block 3" at the end of this plan.

> **Result (2026-11-18):** A3 (contracts.md:45) **hit@1**, 0.397; N3 (decisions.md:21) **hit@2**, contracts first on shared words, 0.329 vs 0.316; N4 (contracts.md:132–136) **miss**, contracts last at 0.148, because its passage stops at line 63. Passages: architecture.md lines 1–24 of 90, contracts.md 1–63 of 152, decisions.md 1–35 of 62; only 5 of the 25 answerable rows have a gold line inside them. Recorded in `experiments.md` section 20.

---

## Block 4: Adapter behind `IAskService` (60–90 min, should-have) — not done

- [ ] Design first: write how `RetrieveAndGenerateResponse` maps to `AskResponse` (answer text, citation fields).
  - **Why:** the shapes differ (Bedrock cites spans of the answer, your `/ask` cites `[n]` chunks). Decide the mapping before you code.
- [ ] `AiLab.Infrastructure`: `BedrockKbAskService : IAskService`, taking `IAmazonBedrockAgentRuntime` from DI (`ServiceURL` from settings).
  - **Why:** Clean Architecture: the API keeps calling `IAskService` and only Infrastructure knows Bedrock exists.
- [ ] Keep the interface's error contract: `ValidationException` / `ResourceNotFoundException` → `InvalidRequestException` (400), anything else → `ModelCallException` (502).
  - **Why:** callers of `/ask` must see the same errors whichever backend runs.
- [ ] Pick the implementation by config, e.g. `Ask:Provider = Local | BedrockKb`.
  - **Why:** you can compare both paths on the same endpoint without code changes.
- [ ] Don't mix the scores: `SearchHit.Score` is a cosine **distance** (lower is closer), Bedrock's `score` is **relevance** in [0, 1] (higher is better).
  - **Why:** feeding one into the other's distance gate flips the abstain decision.
- [ ] Unit tests with a fake `IAmazonBedrockAgentRuntime` (mapping + error translation).
  - **Why:** same approach as `AiLab.Rag.Tests`: prove the code's behaviour without the real service.
- [ ] `POST /ask` with `BedrockKb` returns a cited answer; the `Local` path is unchanged.

---

## Block 5: Stretch (45 min, optional) — not done

- [ ] **Real answers:** `RetrieveAsync` → top 3 chunks → the same `AskPrompt` → Ollama `IChatClient`. Compare with local `/ask` on the 3 questions from 3.4.
  - **Why:** the emulator can't generate, so this is the only way to see a real answer built on managed retrieval.
- [ ] **Retrieval evals:** point the `AiLab.Evals` retrieval run at the Knowledge Base and note the keyword-only caveat in the results.
  - **Why:** puts a number (hit@5) on how much worse keyword-only retrieval is than your vector search.

---

## Block 6: Housekeeping + notes (45 min) — 6.1 done, 6.2 not done

### 6.1 Repo housekeeping
- [x] `experiments.md` section 20: managed vs custom RAG.
  - **What to do:** one table, ai-labs RAG vs Bedrock KB, with rows for chunking, embeddings, retrieval mode, abstain gate, citation check, cost per query on real AWS, and lock-in. Add the 3.4 results and one paragraph on the emulator's limits (keyword search, one passage per document, canned answer).
  - **Why:** every experiment gets recorded (CLAUDE.md convention). This section is the day's real output.
- [x] `CLAUDE.md`: a Week 7 section and the `AiLab.Bedrock` line (one more project).
  - **Why:** keeps the next Claude Code session aware of the new project.
- [x] `README.md`: `AiLab.Bedrock` under Project structure + a short "Running the Bedrock KB demo" note (the script command).
  - **Why:** someone reading the repo can run it.
- [x] Commit and open a PR (the plan is already in `docs/plans/`; the `outputs/` drop folder is gone). PR #13: https://github.com/alihammoud17/ai-labs/pull/13
  - **Why:** every stage ends with a PR.

> **Result (2026-11-18, handed over):** section 20 has the managed vs custom table (13 rows: chunking, embeddings, store, ingestion, retrieval mode, score, abstain gate, prompt, citation check, conversation, cost, lock-in, offline testing), the emulator's three limits, and the conclusion: a KB replaces the build-time pipeline, not the query-time rules; own RAG stays the default. Cost on real AWS is described but **not priced**: the pricing API needs AWS credentials and the AWS pages found give no per-query numbers. CLAUDE.md: `AiLab.Bedrock` line (twelve projects), current step, Week 7 section. README: contents entry, "Managed RAG on AWS (AiLab.Bedrock)" with the demo commands, project structure (`AiLab.Bedrock/`, `docker/bedrock-kb/`), Week 7 status line.

### 6.2 AIF-C01 tie-in (15 min, outside the repo)
- [ ] Add to the Domain 3 tab of the review sheet: Retrieve vs RetrieveAndGenerate; vector stores (OpenSearch Serverless, Aurora, S3 Vectors, Pinecone…); data sources (S3, web crawler, Confluence…); chunking options (fixed-size, hierarchical, semantic, none); grounding with citations and the session ID; a Knowledge Base vs fine-tuning (data that changes often, answers that need sources).
  - **Why:** the exam is on 4 Dec and today covered most of what it asks about Knowledge Bases.

---

## End of day
- [x] Minimum done: 3.2, 3.3, 3.4, 6.1
- [ ] `~/bedrock-kb-emulator.sh stop` (the KB is gone after this; the script rebuilds it)
- [ ] Ask Claude for today's recap PDF (for OneDrive)

> **Short on time?** Do 3.2 → 3.3 → 3.4 → 6.1. Drop Block 5 first, then Block 4.

---

## Appendix: what `bedrock-kb-emulator.sh` does, command by command (2.3)

Line numbers are from `docker/bedrock-kb/bedrock-kb-emulator.sh`. Steps 4–9 are the Knowledge Base itself; the steps around them prepare the emulator and test the result.

### Before the KB: the emulator (steps 0–3)

| Step | Command(s) | What it does | ai-labs equivalent |
|---|---|---|---|
| 0 (l. 56–64) | `docker info`, `aws s3vectors help`, `find "$DOCS_DIR"` | Checks Docker runs, the CLI is new enough to know S3 Vectors, and the docs folder isn't empty. | — |
| 1 (l. 67–89) | `docker build -t aws-emulator` | Builds an image from Amazon Linux plus the aws-emulator RPM (first run only). | `pgvector/pgvector:pg17` image in `docker-compose.yml` |
| 2 (l. 92–113) | `docker run … -v ~/.aws/emulator…`, `curl …/__admin/health` | Starts the container on `127.0.0.1:9297` and waits until it's healthy. The mounted folder is where the emulator writes `ca.pem`, credentials and config. | `docker compose up -d` |
| 3 (l. 116–130) | `export AWS_ENDPOINT_URL=…`, `AWS_CA_BUNDLE=…`, `aws sts get-caller-identity` | Points the CLI at the emulator, then **refuses to go on unless the account is `000000000000`**: the guard against touching real AWS. | the `ConnectionStrings:Vectors` user-secret (where the store lives) |

### The Knowledge Base (steps 4–9)

| Step | Command(s) | What it creates | What it replaces in ai-labs | Worth noticing |
|---|---|---|---|---|
| 4 (l. 133–136) | `aws s3 mb s3://ai-lab-docs`, `aws s3 sync "$DOCS_DIR" …` | An S3 bucket holding the raw `.md` files. This is the KB's **data source content**. | `data/corpus/` on disk, read by `/dev/ingest` (`Corpus:Path`) | `head-bucket` first, so a re-run reuses the bucket. `sync` uploads only changed files. |
| 5 (l. 139–145) | `aws s3vectors create-vector-bucket`, `aws s3vectors create-index --dimension 1024 --distance-metric cosine` | The **vector store**: a vector bucket and an index of 1024-number float32 vectors compared by cosine. | pgvector table `corpus` (`TextChunkRecord`: 768 dimensions, cosine distance, HNSW) | The dimension must match the embedding model (Titan v2 → 1024), the same rule as your `TextVectorStore` refusing vectors that aren't 768 long. Here a mismatch fails at ingestion. |
| 6 (l. 148–161) | `aws iam create-role BedrockKbRole` (trust: `bedrock.amazonaws.com`), `aws iam put-role-policy kb-access` | A **service role** that Bedrock assumes to read the docs bucket, write vectors and call the embedding model. | **Nothing.** Your app ran as itself with a connection string. In a managed service, the service acts for you, so it needs its own permissions. | `s3vectors:*` on `*` and `bedrock:InvokeModel` on `*` are fine on the emulator. On real AWS, scope them to the vector bucket ARN and the embedding model ARN (least privilege, an AIF-C01 topic). |
| 7 (l. 164–180) | `aws bedrock-agent create-knowledge-base --knowledge-base-configuration {type VECTOR, embeddingModelArn Titan v2} --storage-configuration {type S3_VECTORS, bucket, index}`, then `get-knowledge-base` until `ACTIVE` | The **KB**: it ties one embedding model to one vector store. It holds no data yet. | `Embedding:Provider` / `Embedding:Model` (nomic-embed-text) + `VectorStore:Provider` / `Collection` in `appsettings.json` | The KB stores the **embedding** model only. The **generation** model (`GEN_MODEL_ARN`, Nova Lite) isn't part of the KB: it's sent with each `RetrieveAndGenerate` call, as your `Chat:*` is separate from `Embedding:*`. Reused by name on a re-run (`list-knowledge-bases`). |
| 8 (l. 183–191) | `aws bedrock-agent create-data-source --data-source-configuration {type S3, bucketArn}` | The **data source**: tells the KB where to read documents. This is also where **chunking** is set (`--vector-ingestion-configuration`). | `Corpus:Path` + the chunker choice (`/dev/ingest?chunker=fixed\|heading`) | The script passes **no** chunking config, so real AWS would use its default fixed-size chunking. The emulator ignores chunking and keeps one cut-off passage per document. Your `HeadingChunker` has no direct Bedrock equivalent (closest: hierarchical or semantic chunking, or a Lambda for custom chunking). |
| 9 (l. 194–208) | `aws bedrock-agent start-ingestion-job`, then `get-ingestion-job` until `COMPLETE` / `FAILED` / `STOPPED` | An **ingestion job**: read S3 → chunk → embed → write vectors. Runs asynchronously; you poll its status and get statistics (scanned / indexed / failed). | `POST /dev/ingest` → `VectorSearchService.SyncAsync` (synchronous, returns per-file counts) | Real ingestion is incremental: unchanged documents are skipped, the same idea as your `ContentHash`. Each script run starts a new job; on unchanged docs it has nothing to do. Your run: 3 scanned, 3 indexed, 0 failed (`JcJS76Ac1K`). |

### After the KB: check and query (steps 10–11)

| Step | Command(s) | What it does | ai-labs equivalent |
|---|---|---|---|
| — (l. 211–226) | `cat > ~/.aws/emulator/ai-lab-kb.env` | Saves the step 3 variables plus `KB_ID`, `GEN_MODEL_ARN`, `EMULATOR_PORT`, so a new shell can `source` them. | `appsettings.json` + user-secrets |
| 10 (l. 229–232) | `aws bedrock-agent-runtime retrieve` | CLI smoke test: the **runtime** API, retrieval only, prints score + S3 URI per hit. | `GET /search` |
| 11 (l. 235–350) | `dotnet new console`, `dotnet add package AWSSDK.BedrockAgentRuntime`, writes `Program.cs`, `dotnet run` | Scaffolds and runs the C# client: `RetrieveAsync` (top 3) and `RetrieveAndGenerateAsync` (answer + citations + session). | `GET /search` and `POST /ask`; 3.2 moves this into `AiLab.Bedrock` |

**The pattern:** steps 4–9 use `aws s3`, `aws s3vectors`, `aws iam` and `aws bedrock-agent` (build time, the `AWSSDK.BedrockAgent` side). Steps 10–11 use `aws bedrock-agent-runtime` / `AWSSDK.BedrockAgentRuntime` (query time). Every create is preceded by a get or list by name, so the script can be re-run without duplicates.

---

## How we did it: Block 3 (handed over, 2026-11-17/18)

**3.1–3.2: the client in the repo.** You created `AiLab.Bedrock` (console, `net10.0`, `Nullable`, `ImplicitUsings`), added it to `AiLab.slnx` with `AWSSDK.BedrockAgentRuntime` 4.1.0.2, and typed the setup and `EmulatorHttpClientFactory`. Claude finished `Program.cs` with the two queries and fixed one bug: `ServiceURL` was `http://localhost`, but the emulator serves **HTTPS only**, with a certificate issued for `127.0.0.1`, so it's now `https://127.0.0.1:{port}`. The script got a repo-relative default (`REPO_ROOT` from `${BASH_SOURCE[0]}`, so it runs from any folder) and `chmod +x`. Build clean, all tests pass (commit `9124ee5`).

**3.3: the follow-up turn.** `RetrieveAndGenerateAsync` moved into a local function `AskAsync(text, sessionId)`, so the three calls differ only in their arguments:
1. `AskAsync(question)`: no `SessionId`, so Bedrock starts a session and returns its ID.
2. `AskAsync("And how is it versioned?", first.SessionId)`: the program prints whether the returned ID is the same.
3. `AskAsync(..., Guid.NewGuid().ToString())` inside `try/catch (AmazonServiceException)`: it prints the exception type, `ErrorCode`, HTTP status and message. `AmazonServiceException` is the SDK's base class for every error the service returns, so one catch shows whichever type comes back.

`PrintAnswer` also prints each citation's **answer span** (`GeneratedResponsePart.TextResponsePart.Span`) and, for each cited passage, its length and last 60 characters. The span shows how Bedrock cites (a range of the answer text, not a `[n]`), which Block 4's mapping needs. The tail shows where the stored passage was cut. A `--no-followup` flag skips steps 2–3 for plain eval runs.

**3.4: the eval questions.** To know which questions could even be answered, Claude first read each stored passage back (CLI `retrieve`, then matched its last words against the source file): architecture.md stops at line 24 of 90, contracts.md at 63 of 152, decisions.md at 35 of 62. Checking every row of `ask-set-v1.json` against those cut lines gave 5 of 25 answerable rows with a gold line inside. Then three questions were run with `--no-followup`: two whose gold line is inside a passage (A3, N3) and one past the cut (N4). Each gold fact was also searched for in the passage text, to tell "found and ranked well" from "never indexed".

**Problems we hit.**
- After the first `dotnet new`, no project existed on disk; it was there when you asked for the handover.
- The `http://localhost` URL, fixed as above.
- With only 3 documents and top 3, every query returns all three files, so "which file was cited" says nothing: only the ranking does.
- N4's miss can't be fixed from the client, because the fact was never indexed. That's the emulator's one-passage-per-document limit, recorded as such.

**Run it again:** `source ~/.aws/emulator/ai-lab-kb.env && dotnet run --project AiLab.Bedrock -- "your question"`. Add `--no-followup` for the retrieval part only.

