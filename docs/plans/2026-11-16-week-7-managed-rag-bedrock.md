# Managed RAG Day Plan: Bedrock Knowledge Bases on aws-emulator

- **Date:** Mon 16 Nov 2026 (worked on Tue 17 Nov)
- **Branch:** `stage-7-managed-rag` (from `main`), not created yet
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
| 3. Query from C# | first run **done** in the scratch client; 3.1–3.4 left | not yet |
| 4. Adapter behind `IAskService` | not done (should-have) | — |
| 5. Stretch | not done (optional) | — |
| 6. Housekeeping + notes | not done | — |

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

- [ ] **Knowledge Base** = data source (S3) + chunking + embedding model + vector store (S3 Vectors here).
  - **What to do:** write these four parts in your notes, each next to the ai-labs piece you built by hand: chunker → chunking config, `IEmbeddingGenerator` → embedding model ARN, pgvector → S3 Vectors, your ingestion code → the ingestion job.
  - **Why:** the whole day is "what does Bedrock do that I did myself in week 4". This map is what you explain in an interview.
- [ ] **Two runtime calls:** `Retrieve` vs `RetrieveAndGenerate`.
  - **What to do:** one line each: `Retrieve` = chunks + scores, you write the prompt and call the model; `RetrieveAndGenerate` = retrieval + model answer + citations in one call.
  - **Why:** it decides who controls the prompt, the abstain logic and the model. It's also an AIF-C01 question.
- [ ] **Two SDK packages:** `AWSSDK.BedrockAgent` vs `AWSSDK.BedrockAgentRuntime`.
  - **What to do:** note that the first builds things (KB, data source, ingestion; done today with the CLI) and the second queries them (your C# client).
  - **Why:** same split as build time vs query time in your own pipeline; picking the wrong package is a common first mistake.

---

## Block 2: Build the Knowledge Base → D1 — done (except 2.3)

### 2.1 Prerequisites
- [x] Colima running, AWS CLI v2 with `s3vectors`, .NET 10 (the script checked all three)

### 2.2 Run the setup script
- [x] `~/bedrock-kb-emulator.sh ~/claude-code-projects/ai-labs/data/corpus/docs "What architecture decisions were made?"`
- [x] Talking to the emulator (account `000000000000`), not real AWS
- [x] Ingestion `COMPLETE`: 3 scanned, 3 indexed, 0 failed
- [x] CLI `retrieve` hits all three files

> **Result (2026-11-17):** KB `LfHyKY3kWz`, data source `40rcvpKIgC`, ingestion job `JcJS76Ac1K`. Scores: architecture.md 0.042, decisions.md 0.039, contracts.md 0.023. Low scores because search matches words, not meaning; the ranking is still sensible.

### 2.3 Read what the script did — not done
- [ ] Open `~/bedrock-kb-emulator.sh` and match steps 4–9 to the Block 1 map.
  - **What to do:** for each `aws` command, say in one line what it creates and which ai-labs piece it replaces.
  - **Why:** the script was handed over; reading it is how the build side becomes yours, and Block 3.2 copies it into the repo.
- [ ] `source ~/.aws/emulator/ai-lab-kb.env && aws bedrock-agent get-knowledge-base --knowledge-base-id $KB_ID`
  - **What to do:** find `embeddingModelArn` and `storageConfiguration` in the output.
  - **Why:** these two fields are the KB's "which model, which store" decision, the same choice you made with `Embedding:*` and pgvector.

---

## Block 3: Query from C# → D2 — first run done, steps 3.1–3.4 left

> **Already done (scratch client):** `~/ai-lab-kb-client` ran `Retrieve` and `RetrieveAndGenerate` against the KB. The first run crashed in the handed-over certificate code (`CreateFromPemFile` expects a private key, `ca.pem` has none); fixed by loading only the certificate blocks. Then it returned session `ce3d7303-…`, the emulator notice + 3 passages, one citation per file.

### 3.1 Read the generated client — not done
- [ ] Read `~/ai-lab-kb-client/Program.cs` top to bottom.
  - **What to do:** find (1) `ServiceURL` + `AuthenticationRegion`, how the SDK points at the emulator; (2) `EmulatorHttpClientFactory`, which trusts only the emulator's `ca.pem`; (3) where `RetrievalResults`, `Output.Text`, `SessionId` and `Citations` are read.
  - **Why:** you retype this file in 3.2. On macOS, .NET checks certificates through the keychain and ignores `SSL_CERT_FILE`, so the factory is the part most likely to confuse you later.

### 3.2 Move it into the repo — not done
- [ ] Create the branch. You're on `milestone-1-readme` now:
  ```bash
  git switch main && git pull && git switch -c stage-7-managed-rag
  ```
  - **Why:** every stage gets its own branch and PR.
- [ ] Copy the script and create the project, from the ai-labs root:
  ```bash
  mkdir -p docker/bedrock-kb && cp ~/bedrock-kb-emulator.sh docker/bedrock-kb/
  dotnet new console -n AiLab.Bedrock
  dotnet sln AiLab.slnx add AiLab.Bedrock/AiLab.Bedrock.csproj
  dotnet add AiLab.Bedrock package AWSSDK.BedrockAgentRuntime
  ```
  - **Why:** today's result has to live in the repo to count for the portfolio and to be reproducible from a clone.
- [ ] Retype `Program.cs` into `AiLab.Bedrock` (don't copy-paste).
  - **Why:** tutor mode; typing the SDK calls is how you learn their shapes.
- [ ] Match the `.csproj` to the other projects: `net10.0`, `Nullable` and `ImplicitUsings` enabled.
  - **Why:** there's no `Directory.Build.props`, so each project sets these itself.
- [ ] In the copied script, change the default docs folder to `data/corpus/docs` relative to the repo.
  - **Why:** the default `~/docs` only works on your Mac.
- [ ] `dotnet build` and `dotnet test` on the whole solution.
  - **Why:** a new project must not break the existing build or tests.

### 3.3 Add a follow-up turn — not done
- [ ] Send a second `RetrieveAndGenerateRequest` with `SessionId = res.SessionId` and a question that refers back ("and how is it versioned?").
  - **Why:** the session ID is how Bedrock keeps a conversation going between calls. Your own `/ask` is single-turn, so this is new.
- [ ] Check the second response returns the same `SessionId`.
  - **Why:** proves the session continued instead of a new one starting.
- [ ] Try an invented session ID; expect a `ValidationException`.
  - **Why:** the service creates session IDs and the caller can't choose them. Knowing the exception type matters for Block 4's error mapping.

### 3.4 Run the eval questions — not done
- [ ] Pick 3 questions from `docs/evals/ask-set-v1.md` that use words from the corpus.
  - **Why:** search here is keyword-only, so paraphrased questions will miss for reasons that have nothing to do with your code.
- [ ] For each, write down the cited file and compare it with the gold source.
  - **Why:** this is the first real check of the managed path against your own eval data, and it feeds `experiments.md` section 20.
- [ ] Expect misses on facts near the end of a document.
  - **Why:** the emulator keeps one cut-off passage per document. Write it down as an emulator limit, not a failure.

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

## Block 6: Housekeeping + notes (45 min) — not done

### 6.1 Repo housekeeping
- [ ] `experiments.md` section 20: managed vs custom RAG.
  - **What to do:** one table, ai-labs RAG vs Bedrock KB, with rows for chunking, embeddings, retrieval mode, abstain gate, citation check, cost per query on real AWS, and lock-in. Add the 3.4 results and one paragraph on the emulator's limits (keyword search, one passage per document, canned answer).
  - **Why:** every experiment gets recorded (CLAUDE.md convention). This section is the day's real output.
- [ ] `CLAUDE.md`: a Week 7 section and the `AiLab.Bedrock` line (one more project).
  - **Why:** keeps the next Claude Code session aware of the new project.
- [ ] `README.md`: `AiLab.Bedrock` under Project structure + a short "Running the Bedrock KB demo" note (the script command).
  - **Why:** someone reading the repo can run it.
- [ ] Move this plan from `outputs/` to `docs/plans/`, commit, open a PR.
  - **Why:** reviewed drafts move under `docs/` (CLAUDE.md convention).

### 6.2 AIF-C01 tie-in (15 min, outside the repo)
- [ ] Add to the Domain 3 tab of the review sheet: Retrieve vs RetrieveAndGenerate; vector stores (OpenSearch Serverless, Aurora, S3 Vectors, Pinecone…); data sources (S3, web crawler, Confluence…); chunking options (fixed-size, hierarchical, semantic, none); grounding with citations and the session ID; a Knowledge Base vs fine-tuning (data that changes often, answers that need sources).
  - **Why:** the exam is on 4 Dec and today covered most of what it asks about Knowledge Bases.

---

## End of day
- [ ] Minimum done: 3.2, 3.3, 3.4, 6.1
- [ ] `~/bedrock-kb-emulator.sh stop` (the KB is gone after this; the script rebuilds it)
- [ ] Ask Claude for today's recap PDF (for OneDrive)

> **Short on time?** Do 3.2 → 3.3 → 3.4 → 6.1. Drop Block 5 first, then Block 4.
