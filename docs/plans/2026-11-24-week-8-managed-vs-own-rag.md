# Managed vs Own RAG Day Plan: Run the Golden Set Against Both

- **Date:** Tue 24 Nov 2026 (Week 8, theme managed RAG on AWS), on aws-emulator
- **Branch:** `stage-8-managed-vs-own-rag` (from `main`), not created yet
- **Status:** in progress (Block 0 done)
- **Focus:** Managed vs your RAG, measured: the same 30-question golden set (`ask-set-v1`) against the Bedrock Knowledge Base and against the pgvector RAG, scored by the same code
- **Where:** `AiLab.Domain` (one interface, one output record), `AiLab.Infrastructure` (the Agent Runtime client factory, the Knowledge Base adapter, `AddKnowledgeBase`), `AiLab.Evals` (passage locator, a separate `kb` retrieval runner, report), `docker/bedrock-kb/` (default corpus), `docs/evals/`, `experiments.md`. `AiLab.Rag` doesn't change.

**Tutor mode, strict.** I type every command and every line of code. Claude Code starts each task with a **push**, waits while I work, reviews, and ticks the box only after the check passes. It writes code only when I say "hand it over".

After each finished task, Claude adds a short **How we did it** note under it when I ask.

## How each task runs

1. **Push** (Claude Code, before I start): goal and "Done when" · the files to open · 1–3 things to read first (a doc section and similar ai-labs code) · the first step as a hint, in words · the likely trap.
2. **I do it**, following the task's numbered steps, and ask when stuck.
3. **Review** (Claude Code): runs the task's **Check**, compares with **Done when**, says what's wrong and where. It doesn't fix it.
4. **Tick**, with a one-line `> Result (date):`. Then the next push.

Every task has the same parts: **You'll build** · **Open** · **Steps** · **Check** · **Done when** · **Why**.

## Where things stand

| Block | Tasks | Status |
|---|---|---|
| 0. Setup: the Knowledge Base on the same corpus | 0.1–0.2 | done |
| 1. Understand both pipelines, write the rule | 1.1–1.4 | done |
| 2. The Knowledge Base behind an interface | 2.1–2.4 | done |
| 3. Passages → line ranges | 3.1–3.2 | not done |
| 4. Retrieval, scored on the KB → D1 | 4.1–4.3 | not done |
| 5. Answers through the same suite (should-have) | 5.1–5.4 | not done |
| 6. Side by side → D2 | 6.1–6.2 | not done |
| 7. Real-account runbook + housekeeping | 7.1–7.3 | not done |

**Minimum:** 0 → 1 → 2 → 3 → 4 → 6 → 7. Block 5 adds the answer layer; on the emulator its numbers are a proxy (see scope rules), so it comes after the retrieval comparison.

## Deliverables (adapted: no AWS account, the emulator's KB is a stand-in)
- [ ] **D1. Golden set scored on the Knowledge Base:** every answerable row of `ask-set-v1` sent to the KB's `Retrieve`, its passages mapped to file and lines, and scored with the same `RetrievalMetrics` (first hit rank, hit@1/3/5, MRR) as `/search`. If Block 5 is done: the same 30 rows through `RetrieveAndGenerate` and the same `GoldCheck`.
- [ ] **D2. Side-by-side numbers vs pgvector RAG:** `docs/evals/managed-vs-own-rag-2026-11-24.md`: pgvector (vector, hybrid; measured) next to the KB (on aws-emulator; measured, labelled), plus what each side lets you control, latency and cost model. The real-Bedrock KB column waits for the real account (runbook in Block 7).

**What "measured on the emulator" means today:** the emulator's KB is real in shape (S3 data source, ingestion job, `Retrieve`, `RetrieveAndGenerate`, citations, session IDs) but not in behaviour: it searches by **keyword** (term-frequency vectors), stores **one cut-off passage per document**, and runs **no model** (the answer quotes the passages). So today's KB column measures *that* system. It proves the harness end to end and shows what the comparison will look like; it says nothing about Titan embeddings or Bedrock's chunking.

## Resources
- Microsoft Learn, Evaluate with caching and reporting (tutorial): https://learn.microsoft.com/en-us/dotnet/ai/evaluation/evaluate-with-reporting
- Bedrock Agent Runtime API, Retrieve: https://docs.aws.amazon.com/bedrock/latest/APIReference/API_agent-runtime_Retrieve.html
- Bedrock Agent Runtime API, RetrieveAndGenerate: https://docs.aws.amazon.com/bedrock/latest/APIReference/API_agent-runtime_RetrieveAndGenerate.html
- Bedrock User Guide, How content chunking works for knowledge bases: https://docs.aws.amazon.com/bedrock/latest/userguide/kb-chunking.html
- In the repo:
  - `AiLab.Bedrock/Program.cs` (the working `RetrieveAsync` / `RetrieveAndGenerateAsync` calls), `docker/bedrock-kb/bedrock-kb-emulator.sh`, `experiments.md` §20 (the emulator KB's limits and cut-off line ranges)
  - `docs/evals/hybrid-search-2026-11-04.md` and `eval-runs/retrieval/2026-11-05-*-heading.md` (pgvector's retrieval numbers: vector hit@5 1.00, MRR 0.73 on 24 English rows)
  - `AiLab.Evals/RetrievalEvalTests.cs`, `Retrieval/RetrievalMetrics.cs`, `Retrieval/RetrievalReport.cs`, `Evaluators/RecallAtKEvaluator.cs` (`IsHit`), `Evaluators/GoldCheck.cs`, `Summary/CostPerCorrectReport.cs`
  - `AiLab.Infrastructure/Providers/BedrockRuntimeClientFactory.cs`, `BedrockClientOptions.cs`, `EmulatorHttpClientFactory.cs`; `AiLab.Api.Tests/BedrockEmbeddingTests.cs` (real SDK + fake HTTP)
  - `AiLab.Domain/Interfaces/IAskService.cs`, `AiLab.Application/Services/AskService.cs`

## Scope rules for today
- **No AWS account, no Anthropic calls.** The KB runs on aws-emulator, after the safety check. `AILAB_EVAL_JUDGE=none` for any eval run.
- **Same inputs on both sides, or it isn't a comparison.** Same corpus (all of `data/corpus`, README included: 11 gold references point at `README.md`), same 30 questions, same gold lines, same top k (10, the retrieval runner's `Top`, so hit@k and MRR are computed at the same depth as week 5's reports), same scoring code (`RetrievalMetrics`, `GoldCheck`). Every difference that remains is written down.
- **Labels, always:** "pgvector (measured)", "KB on aws-emulator (measured, stand-in)", "KB on Bedrock (pending)". The emulator KB column is never called "Bedrock". Code decides the label from the endpoint: `Bedrock:ServiceUrl` **or** `AWS_ENDPOINT_URL` set → aws-emulator (a sourced `ai-lab-kb.env` sets `AWS_ENDPOINT_URL`, which the SDK copies into `ServiceURL`).
- **A passage that can't be located is listed, not dropped.**
- **"No data" never looks like 0%** (Thursday's lesson): a row the KB couldn't answer for a setup reason is "missing", not "miss".
- **`AiLab.Rag`'s `/ask` and `/search` don't change.** The KB is reached through a new adapter; the pgvector numbers are week 5's (re-run once in Task 4.3 to confirm nothing drifted).

---

## Block 0: Setup, the Knowledge Base on the same corpus (20 min) — done

### Task 0.1 — Branch and a green start ✅
- **Steps:**
  1. `git switch main && git pull && git switch -c stage-8-managed-vs-own-rag`
  2. Colima, Postgres and Ollama up; the safety check from `docker/aws-setup/README.md` → `SAFE`.
  3. `dotnet test`: write down the passing count.
- **Done when:** branch created, `SAFE`, tests green.
 > **Test summary:** total: 517, failed: 0, succeeded: 472, skipped: 45, duration: 9.5s

> Result (2026-11-25): branch `stage-8-managed-vs-own-rag` at `7bd60bc` (= `origin/main`), no `AILAB_*` variables set, safety check `SAFE` (account 000000000000), `dotnet test` 472 passed / 45 skipped / 0 failed (same as §24's end count).


### Task 0.2 — Rebuild the KB over the whole corpus ✅
- **You'll build:** a KB whose documents are exactly pgvector's: `README.md` + `docs/*.md`.
- **Open:** `docker/bedrock-kb/bedrock-kb-emulator.sh` (header comment, `DOCS_DIR`, step 4's `aws s3 sync` line).
- **Steps:**
  1. Read why the default (`data/corpus/docs`) isn't enough: 11 of the golden set's gold references are in `README.md`, and F5 and N7 need only README.
  2. Read why a plain re-run isn't enough either: the running emulator still holds KB `ai-lab-kb` from the docs-only run, with keys `architecture.md`, `contracts.md`, `decisions.md` at the bucket root. The script reuses the KB and data source by name, and `aws s3 sync` never deletes, so a re-run would index 7 documents (root copies + `docs/…`) under the same KB_ID, and every root-key hit would miss `gold.file`.
  3. Change the script: the default `DOCS_DIR` becomes `$REPO_ROOT/data/corpus` (and the header comment's "Defaults" line with it), and the `aws s3 sync` line gets `--delete`, so the bucket always mirrors the folder.
  4. Stop the emulator: `docker/bedrock-kb/bedrock-kb-emulator.sh stop`. Its state is in memory, so this wipes the old KB, and also week 7 Monday's emulator resources (budget, IAM user `ai-lab-dev`, policy); nothing today needs them, and `docker/aws-setup/` rebuilds them.
  5. Run the script with no argument (the new default): `docker/bedrock-kb/bedrock-kb-emulator.sh`.
  6. Check the S3 keys: `aws s3 ls s3://ai-lab-docs --recursive` must list exactly `README.md`, `docs/architecture.md`, `docs/contracts.md`, `docs/decisions.md`: the same paths as the golden set's `gold.file`.
  7. Check the ingestion job: 4 scanned, 4 indexed, 0 failed. Note the new `KB_ID` (different from `oDfkugl6Kz`; the env file is updated).
- **Check:** safety check `SAFE`, then `aws bedrock-agent-runtime retrieve --knowledge-base-id $KB_ID --retrieval-query '{"text":"tenant registration"}' --retrieval-configuration '{"vectorSearchConfiguration":{"numberOfResults":10}}'` returns 4 results, one per document, with `s3://ai-lab-docs/README.md` and `s3://ai-lab-docs/docs/…` URIs.
- **Done when:** 4 documents indexed, exactly 4 keys matching the gold paths, a new KB_ID, script default and `--delete` changed.
- **Why:** with README missing, the KB would lose 2 rows before the first query; with stale root keys, it would lose every docs row. Either way the comparison would measure the setup, not the pipeline.

**Done (handed over):** Claude changed the script in two places: `DOCS_DIR` now defaults to `$REPO_ROOT/data/corpus` (header "Defaults" line updated), and `aws s3 sync` got `--delete` with a one-line comment (`bash -n` OK). Then it stopped the emulator and re-ran the script with no argument. Choice for step 5's old question: the default changed (every consumer of the KB now wants the same documents as pgvector); another folder can still be passed as the first argument.

> Result (2026-11-25): safety check `SAFE`. Bucket holds exactly `README.md`, `docs/architecture.md`, `docs/contracts.md`, `docs/decisions.md`. KB `LOH6DH3TBj` (new; was `oDfkugl6Kz`), data source `jm8ALzODxZ`, ingestion `rBQQcMv713` COMPLETE: 4 scanned, 4 new indexed, 0 failed. Check (`tenant registration`, `numberOfResults` 10): 4 results, one per document: docs/architecture.md 0.211, docs/contracts.md 0.173, README.md 0.129, docs/decisions.md 0.094. Passages are whitespace-collapsed file prefixes: architecture 2,389 chars (lines 1–24), contracts 2,646 (1–63), decisions 2,283 (1–35), all three as in §20, and README 2,573 chars ≈ lines 1–29 of 122 (quick script check; Block 3's locator confirms it). Side effect: week 7 Monday's emulator resources (budget, IAM user, policy) are gone with the old state.

---

## Block 1: Understand both pipelines, write the rule (40 min) — done

### Task 1.1 — The two pipelines, step by step ✅
- **You'll build:** a table in your notes, one row per step, two columns (pgvector RAG | KB).
- **Steps:** fill each row from the code (pgvector) and §20 + the chunking doc (KB):
  1. Corpus and parsing.
  2. Chunking (heading chunks ~176 tokens | Bedrock default ~300 tokens; **emulator: one cut-off passage per file**).
  3. Embeddings (`nomic-embed-text` 768 | Titan v2 1024; **emulator: keyword TF vectors**).
  4. Store and search (pgvector HNSW cosine, vector / keyword / hybrid | S3 Vectors, `SEMANTIC` / `HYBRID`).
  5. The gate (distance ≤ 0.50, abstain without a model call | none: the KB always returns what it found).
  6. Generation and citations (`/ask`'s prompt, forced tool, `CitationCheck` | `RetrieveAndGenerate`; **emulator: quoted passages**).
- **Done when:** 6 rows filled; Claude checks each cell against the code and §20.
- **Why:** each row is a reason the numbers may differ; the D2 doc needs this table.

**The table (drafted by Claude, approved 2026-11-25).** Every score cell names its scale and direction (distance: lower is closer; relevance: higher is better). Sources: the code at the paths named, `experiments.md` §20, Task 0.2's result, and the AWS pages (kb-chunking, Retrieve, KnowledgeBaseVectorSearchConfiguration, RetrieveAndGenerate; read 2026-11-25).

| Step | pgvector RAG (`AiLab.Rag`) | KB on Bedrock (real) | KB on aws-emulator (today) | Same or different for the comparison? |
|---|---|---|---|---|
| 1. Corpus and parsing | Every `.md` under `Corpus:Path` (`data/corpus`): README.md + docs/architecture.md, contracts.md, decisions.md. `Source` = path relative to the corpus (`docs/contracts.md`), read as plain markdown (`/dev/ingest`, `DevEndpoints.cs:72-98`) | The S3 bucket behind the data source (here `s3://ai-lab-docs/`, synced from `data/corpus`). Document identity = the S3 URI (`s3://ai-lab-docs/docs/contracts.md`); default parsing reads the text, advanced parsers (pages, tables, figures) are an option | The same 4 keys (`README.md`, `docs/*.md`), 4 scanned / 4 indexed (Task 0.2) | **Same** documents and paths, once the adapter strips `s3://ai-lab-docs/` (Task 2.3) |
| 2. Chunking | `HeadingChunker`: one chunk per heading section (a parent holds only its intro), text prefixed with the heading path; a section over 2,000 chars is split in fixed windows (200 overlap, 100 min tail; `ChunkingOptions.Default`). 50 chunks, ≈ 176 tokens each (8,805 / 50). Each chunk keeps `StartLine`/`EndLine` | Default: ≈ 300 tokens per chunk, sentence boundaries kept (no overlap value given). Options: fixed-size (max tokens + overlap %), hierarchical (not recommended with S3 Vectors), semantic, none (one chunk per document), or your own Lambda. No heading-aware option. Chunks carry text + location, **no line numbers** | **One passage per file, cut off**: the file's start with all whitespace collapsed to single spaces, stopping mid-line: README 2,573 chars (≈ lines 1–29 of 122), architecture 2,389 (1–24 of 90), contracts 2,646 (1–63 of 152), decisions 2,283 (1–35 of 62) | **Different**: the main cause of any gap today. Most gold lines are never indexed on the emulator (Task 1.2's ceiling) |
| 3. Embeddings | `nomic-embed-text` via Ollama, 768 dimensions, local, $0 (Rag `Embedding:*`); task prefixes off (`UseTaskPrefixes` false). The question is embedded the same way | Titan Text Embeddings v2, 1024 dimensions (script: `EMBED_MODEL_ARN`, index `--dimension 1024`). Chosen at KB creation: another model means a new index and re-ingesting. Billed per token | The Titan model is configured but **never called**: the emulator scores by keyword term frequency (§20) | **Different** (and on real Bedrock still different: 768 nomic vs 1024 Titan) |
| 4. Store and search | Postgres + pgvector, HNSW, **cosine distance (lower is closer)**. Modes: `vector` (default, `Ask:SearchMode`), `keyword` (Postgres full text), `hybrid` (top 20 of each merged by RRF, k = 60). The eval runner asks for top 10 | S3 Vectors index (cosine). `overrideSearchType`: `SEMANTIC` or `HYBRID`, but HYBRID only on OpenSearch Serverless with a text field, so S3 Vectors is semantic only (by the API page's wording; not stated for S3 Vectors by name). Score = **relevance (higher is better)**, meaning not documented. `numberOfResults` 1–100 | Keyword term-frequency search; **relevance (higher is better)**, low and close: 0.09–0.22 on "tenant registration" (Task 0.2), 0.15–0.40 in §20. 4 documents, so top 10 always returns all 4 | **Different**: semantic vector vs keyword. Today's fair partner for the emulator is pgvector `keyword` as much as `vector`. Scales must never be compared |
| 5. The gate | `AskService.cs:33-66`: the smallest distance in the hits; none → `no_hits`, over `Ask:MaxDistance` 0.50 → `too_far`, else `passed`. Not passed → abstain **without a model call** | None. `Retrieve` returns the top N whatever their score; `RetrieveAndGenerate` always calls the model, and only the prompt can make it decline. A gate means `Retrieve` + your own threshold on a relevance score | None: every query gets all 4 passages, even a weak one | **Different on any account** (Task 5.4). No effect on retrieval metrics; matters for abstain rows and E5 |
| 6. Generation and citations | `prompts/ask-v1.md` (read per request) + `AskPrompt.Build`: excerpts numbered `<source n= file= section= lines=>`; rules: facts only from excerpts, cite `[n]`, `notInDocs` when not covered, ≤ 120 words, no double quotes. Typed `AskAnswer` via `GetResponseAsync<AskAnswer>(useJsonSchemaResponseFormat: false)`, one repair if unreadable. `CitationCheck`: every cited `[n]` must be a source sent (grounded / unverified / abstained); citations built from the server's map, with file + lines | `RetrieveAndGenerate`: retrieval + the chosen model (`modelArn`) in one call; the prompt can be replaced (`generationConfiguration.promptTemplate.textPromptTemplate`; placeholders not checked). Citations = an answer span (`generatedResponsePart.textResponsePart.span`) → `retrievedReferences` (content, S3 location, metadata; no score, no lines). Nothing checks that a span is supported. Server-side sessions | No model: the "answer" is a fixed notice + the retrieved passages quoted in full; each citation = one whole passage (Task 0.2's output, §20) | **Different**: on the emulator, answers measure retrieval, not generation (Task 5.3's label). Correction to row 6's wording above: "forced tool" is the Bedrock MEAI path's structured output (§22), not `/ask` on Haiku or llama3.2 |

**What this means for the numbers:** rows 2–4 differ today, row 2 most. Retrieval metrics are affected by rows 2–4 only; rows 5–6 matter in Block 5. Row 1 is the only one Task 0.2 could make the same, and it did.

**Done (handed over):** Claude filled the table from the code, §20, Task 0.2's result and the AWS pages; open points flagged in the draft (S3 Vectors hybrid inferred from the API page's wording, default-chunking overlap not given, prompt-template placeholders not checked).

> Result (2026-11-25): 6 rows × 3 pipelines filled and approved. Same today: row 1 (corpus and paths). Different: rows 2–4 (chunking the biggest: one cut-off passage per file on the emulator), 5 (no gate on any account) and 6 (no model on the emulator). The plan's "forced tool" wording for `/ask` corrected: that's the Bedrock MEAI path only.

### Task 1.2 — What the emulator's KB can and can't show ✅
- **Open:** `experiments.md` §20 ("What the emulator can't show", the cut-off line ranges: architecture.md 1–24, contracts.md 1–63, decisions.md 1–35).
- **Steps:**
  1. For each answerable row's gold lines, mark whether they fall inside the cut-off. README's range: count the characters of its passage from Task 0.2's check against the file (the passage is the file's start with whitespace collapsed, cut mid-line), or take it from Block 3's locator.
  2. Count: the maximum hit@k the emulator KB **can** reach, whatever the ranking (an `all` row needs every gold source inside its passage).
  3. Note what the ranking can still change: with 4 documents and top 10, every query returns all 4 passages, so on the emulator **hit@5 = hit@10 = the ceiling exactly**. Only hit@1, hit@3 and MRR carry ranking information.
  4. Write your prediction for KB hit@1, hit@3 and MRR before running anything.
- **Done when:** the ceiling and the prediction are written in the plan under this task.
- **Why:** a number you predicted is a number you understand. If the run's hit@5 differs from the ceiling in either direction, a source path or the locator is wrong.

**The ceiling (drafted by Claude, approved 2026-11-25).** Passage ranges from Task 0.2's KB, last line checked against the files: README.md 1–29, docs/architecture.md 1–24, docs/contracts.md 1–63, docs/decisions.md 1–35. A gold source is "in" when one of its lines is inside its file's range (`IsHit` works on whole lines). An `any` row can hit with one source in; an `all` row needs every source in.

| ID | Category | Mode | Gold sources (file:lines) | In the passage? | Row can hit? |
|---|---|---|---|---|---|
| A1 | Single fact | any | architecture:38 · README:9 | no · **yes** | **yes** |
| A3 | Single fact | any | contracts:45 | **yes** | **yes** |
| N1 | Single fact | any | README:87 · architecture:88 | no · no | no |
| N2 | Single fact | any | contracts:117–118 | no | no |
| N3 | Single fact | any | decisions:21 | **yes** | **yes** |
| F1 | Exact name or number | any | contracts:39 | **yes** | **yes** |
| F4 | Exact name or number | any | contracts:12 · README:81 | **yes** · no | **yes** |
| F5 | Exact name or number | any | README:71 | no | no |
| G2 | Exact name or number | any | architecture:46 | no | no |
| A15 | List | any | contracts:69–76 | no | no |
| B7 | List | any | architecture:46 | no | no |
| N4 | List | any | contracts:132–136 | no | no |
| B3 | Two sources needed | any | README:101–102 · architecture:67 | no · no | no |
| B6 | Two sources needed | **all** | architecture:46 · README:38 | no · no | no |
| G1 | Two sources needed | **all** | contracts:45 · contracts:128 | **yes** · no | no (needs both) |
| N5 | Two sources needed | any | architecture:84 · decisions:35 | no · **yes, by line** | **yes, by line** ¹ |
| N6 | Two sources needed | any | decisions:47 · README:105 | no · no | no |
| C1 | False premise | any | architecture:34 | no | no |
| C3 | False premise | any | architecture:46 | no | no |
| N8 | False premise | any | contracts:152 · architecture:73 | no · no | no |
| H6 | Typo or paraphrase | any | architecture:38 · README:9 | no · **yes** | **yes** |
| N7 | Typo or paraphrase | any | README:48–58 | no | no |
| D1 | Partial | any | architecture:34 · README:71 | no · no | no |
| D3 | Partial | any | contracts:142 | no | no |
| H5 | Non-English (apart) | any | architecture:38 · README:9 | no · **yes** | **yes** (reported apart) |

¹ N5: decisions.md line 35 is the passage's last line, cut mid-line. The passage holds "BillingService uses a JSON workflow state file for deterministic retry, deduplication, dead-letter, and", and the rest of the fact ("…This is not a distributed queue", the single-instance half of the gold answer) is cut off. `IsHit` counts the line, so the run will report a hit. That is a hit by line, not by text.

**Ceiling:** **7 of 24** English rows can hit (A1, A3, N3, F1, F4, N5¹, H6), so on the emulator hit@5 = hit@10 = **7/24 = 0.292** exactly. H5 (apart) can hit too. hit@1, hit@3 and MRR can't go above 0.292 either (MRR = 0.292 only if all 7 rank first). Compared with §20's 5 rows (made without README): all 5 are still in (A3, N3, F1, F4, N5), and README adds A1 and H6 (plus H5), all through README:9. README's other gold lines (38–105) are past its cut, so F5 and N7 stay out.

**Prediction (yours, before any run):**
- hit@1: 4/24 = 0.167 (A3, F1, F4, N5)
- hit@3: 6/24 = 0.250 (+ N3 at 2, A1 at 3)
- MRR: 0.212 = (1 + 1 + 1 + 1 + 1/2 + 1/3 + 1/4) / 24
- Why: rows whose words only appear in contracts.md's opening (A3, F1, F4) should rank first, and N5 matches decisions.md's last words. A1 and H6 depend on README beating architecture.md on "tenant", and yesterday's "tenant registration" check put README 3rd. H6's typos leave almost no matching words. hit@5 = 7/24 = 0.292 (the ceiling), with H6 at 4.

**Done (handed over):** Claude drafted the ceiling table (passage edges checked against the files) and an example prediction; the prediction above was adopted as mine.

> Result (2026-11-25): ceiling 7 of 24 English rows (A1, A3, N3, F1, F4, N5 by line, H6), so KB hit@5 = hit@10 = 0.292 exactly on the emulator; H5 (apart) can hit. README adds A1, H6, H5 (all via README:9) to §20's 5. G1 out (an `all` row with one of two lines stored). Prediction: hit@1 4/24 = 0.167, hit@3 6/24 = 0.250, MRR 0.212; every value ≤ the ceiling, arithmetic checked.

### Task 1.3 — Read the reporting tutorial again, for the parts you'll reuse ✅
- **Steps:** from the tutorial, note how a scenario run stores results and how a new execution name keeps two systems' results apart. Decide the execution names for Block 5 (e.g. `kb-emulator`).
- **Done when:** names decided and written down.

**Done (handed over):** notes and names by Claude.
- **How results are stored:** `DiskBasedReportingConfiguration.Create(storageRootPath, evaluators, chatConfiguration, enableResponseCaching, cachingKeys, executionName, tags)` (`EvalReporting.Create`) → each `CreateScenarioRunAsync(scenario, iterationName)` writes one result to `eval-runs/meai/results/<execution>/<scenario>/<iteration>.json`. Without `executionName`, MEAI names the execution after the time it ran, so two runs never share a folder by accident, and never by intent either. The report (`dotnet aieval report`) shows every execution under the storage root side by side, and `EvalSummary.Load(<execution folder>)` reads one execution back. The scenario name is `<test class>.<category>.<id>`; `EvalSummary.Parse` reads only the last two parts, so another test class still yields the same row ids.
- **Names chosen:**
  - Block 5 answers: **`kb-emulator`** (today) and **`kb-bedrock`** (the real account). One rule decides both, the endpoint rule from the scope rules (`Bedrock:ServiceUrl` or `AWS_ENDPOINT_URL` set → emulator). The name is a constant in the KB test, never `fixture.Settings.ExecutionName` or `AILAB_EVAL_EXECUTION`: with judge `none` the default would be `dev-haiku-judge-none`, an `/ask` wiring check's folder, and `GoldCheckReport` would mix the two.
  - Block 4 retrieval report (markdown, no MEAI storage): `eval-runs/retrieval/<date>-kb-emulator.md` (or `-kb-bedrock.md`), so a real-account run on the same day can't overwrite the emulator's file.
  - Neither name can come out of `EvalSettings.DefaultExecutionName` (its names start with `bedrock-`, `dev-`, `baseline-` or `matrix-`), and none exists under `eval-runs/meai/results/`.

> Result (2026-11-25): execution names `kb-emulator` / `kb-bedrock`, retrieval files `<date>-kb-emulator.md` / `-kb-bedrock.md`, both chosen by the endpoint rule; no collision with any existing or default name.

### Task 1.4 — The rule, written first ✅
- **Steps:** under this task, write:
  1. **Fairness rules:** the list from the scope rules, plus top k: 10 on both sides (`RetrievalEvalTests.Top`; the KB's `numberOfResults` = 10). `/ask` sends 5, which hit@5 covers; MRR is computed over the same 10 as week 5's reports (hybrid's 0.787 counts a hit at rank 6–10).
  2. **What decides "the KB matches our RAG" on retrieval:** e.g. hit@5 within 1 row and MRR within 0.05 of pgvector vector search on the 24 English rows.
  3. **What decides it on answers** (real account only): gold check within 2 of 16 on run 1, abstains ≥ 11/12, the 12-row hand pass.
  4. **What the emulator result is allowed to decide:** nothing about Bedrock; only whether the harness works.
- **Done when:** committed before Block 4's first run.

**The rule (Done (handed over), written 2026-11-25, before any KB query).**
1. **Fairness rules.** Same corpus (the 4 files of `data/corpus`, same paths, Task 0.2), same 25 rows with gold (24 English + H5 apart), same gold lines, same top k (10 on both sides; `/ask`'s 5 is covered by hit@5), same scoring code (`PassageLocator` → `RetrievedChunk` → `RetrievalMetrics.FirstHitRank` → `HitRate` / `Mrr`; `GoldCheck` for answers). A passage that can't be located is a non-hit and is listed. A row that fails for a setup reason (an HTTP error) is "missing", never "miss". Differences that remain and are written down, not removed: chunking, embeddings and search (Task 1.1, rows 2–4).
2. **"The KB matches our RAG" on retrieval (real account only):** on the 24 English rows, KB hit@5 within 1 row of pgvector `vector` (≥ 23/24, since vector's hit@5 is 24/24) **and** KB MRR within 0.05 of vector's (≥ 0.684 against week 5's 0.734, or whatever Task 4.3 measures today). Hybrid is reported next to it but isn't the bar (it's not `/ask`'s mode).
3. **"The KB matches our RAG" on answers (real account only):** gold check run 1 within 2 of HH's 16/16 (≥ 14/16), abstains ≥ 11/12 over 3 runs plus E5, and the 12-row hand pass ≥ 10/12. E5 can't pass without a gate (the KB has none), so on answers the KB needs Retrieve + our own gate, or it fails that bar: written down now, not discovered later.
4. **What the emulator run may decide:** only whether the harness works: the adapter reaches the KB, every passage is located (or listed), hit@5 equals Task 1.2's ceiling (7/24) exactly, and the report says "KB on aws-emulator (measured, stand-in)". It decides **nothing** about Bedrock's retrieval or answer quality, and its numbers never go in a "KB on Bedrock" column.

> Result (2026-11-25): rule written with the bars above; committed before Block 4's first run.

---

## Block 2: The Knowledge Base behind an interface (75 min) — done

### Task 2.1 — A port in Domain ✅
- **You'll build:** `AiLab.Domain/Interfaces/IKnowledgeBaseRetriever.cs` and a record `AiLab.Domain/Outputs/KnowledgeBasePassage.cs`: `KnowledgeBasePassage(string Source, string Text, double Score, int Rank)`.
- **Open:** `IAskService.cs` and `Domain/Outputs/` (where results like `SearchHit` live; `DTOs/` holds requests), for naming and comment style.
- **Steps:**
  1. One method: `RetrieveAsync(string query, int top, CancellationToken ct)` → `IReadOnlyList<KnowledgeBasePassage>`.
  2. `Source` is the path **relative to the corpus** (`docs/contracts.md`), not the `s3://` URI: the adapter converts it.
  3. A comment on `Score`: relevance, higher is better (the opposite of pgvector's distance, Week 7 Tuesday's warning).
- **Check:** `dotnet build`.
- **Done when:** builds; Domain references no AWS type.
- **Why:** the eval suite and, later, `/ask` depend on the port, not on the SDK (Clean Architecture, like `IChatClient` on the chat side).

**Done (handed over):** `AiLab.Domain/Interfaces/IKnowledgeBaseRetriever.cs` (`RetrieveAsync(query, top, ct)`) and `AiLab.Domain/Outputs/KnowledgeBasePassage.cs` (`Source`, `Text`, `Score`, `Rank`), with comments on the corpus-relative `Source`, the 1-based rank, and the score as relevance (higher is better, never fed into the distance gate).

> Result (2026-11-25): `dotnet build AiLab.Domain` 0 errors; `AiLab.Domain.csproj` still has no package or project reference.

### Task 2.2 — One client builder for the Agent Runtime ✅
- **You'll build:** `AiLab.Infrastructure/Providers/BedrockAgentRuntimeClientFactory.cs`, from the same `BedrockClientOptions` as `BedrockRuntimeClientFactory`.
- **Open:** `BedrockRuntimeClientFactory.cs`, `EmulatorHttpClientFactory.cs`.
- **Steps:**
  1. Add the `AWSSDK.BedrockAgentRuntime` package to Infrastructure, **same version as `AiLab.Bedrock`** (4.1.0.2), so `AWSSDK.Core` stays aligned.
  2. Build `AmazonBedrockAgentRuntimeConfig` exactly like the runtime config: `ServiceURL` or `RegionEndpoint`, the emulator CA, the SDK timeout and retries.
  3. Don't copy the endpoint, TLS or timeout code: both configs derive from the SDK's `ClientConfig`, so extract the shared setup (service URL or region endpoint, `AuthenticationRegion`, timeout, retries, the emulator CA with its "refused without ServiceUrl" guard) into one helper both factories call.
- **Check:** `dotnet build`; `dotnet list AiLab.Infrastructure package --include-transitive | grep AWSSDK` shows one `AWSSDK.Core` version.
- **Done when:** builds, one Core version.

**Done (handed over):** `AWSSDK.BedrockAgentRuntime` 4.1.0.2 in Infrastructure. The shared setup moved into `Providers/BedrockClientConfig.Apply<TConfig>(config, options)` (service URL or region endpoint + `AuthenticationRegion`, timeout, retries, the emulator CA with its "refused without ServiceUrl" guard), used by `BedrockRuntimeClientFactory` (now one line) and the new `BedrockAgentRuntimeClientFactory`. Comments in `BedrockClientOptions` and `EmulatorHttpClientFactory` updated.

> Result (2026-11-25): build 0 warnings / 0 errors; `dotnet list … --include-transitive | grep AWSSDK` → BedrockAgentRuntime 4.1.0.2, BedrockRuntime 4.1.0.2, **one** `AWSSDK.Core` 4.1.1.1. The existing `ChatClientFactoryTests`, `EmbeddingGeneratorFactoryTests` and `BedrockEmbeddingTests` pass (36/36) after the refactor.

### Task 2.3 — The adapter ✅
- **You'll build:** `AiLab.Infrastructure/KnowledgeBase/BedrockKnowledgeBaseRetriever.cs` implementing the port, and `AiLab.Infrastructure/KnowledgeBase/KnowledgeBaseServices.cs` with `AddKnowledgeBase(IConfiguration)`.
- **Open:** `AiLab.Bedrock/Program.cs` (the working `RetrieveAsync` call), the Retrieve API page (request: `knowledgeBaseId`, `retrievalQuery`, `retrievalConfiguration.vectorSearchConfiguration.numberOfResults`; response: `retrievalResults[].content.text`, `location.s3Location.uri`, `score`), `VectorStore/VectorStoreServices.cs` (`AddVectorStore(config)`: a registration that reads config up front).
- **Steps:**
  1. Settings: `KnowledgeBase:Id` and an optional `KnowledgeBase:SearchType` (`SEMANTIC` / `HYBRID`, unset = Bedrock's default).
  2. The adapter is **public**, with a constructor that takes `IAmazonBedrockAgentRuntime` and the settings: it never builds its own client. That keeps the factory `internal` and lets Task 2.4 pass in a client with fake credentials (no project has `InternalsVisibleTo`).
  3. Map each result to `KnowledgeBasePassage`: strip `s3://<bucket>/` from the URI, keep the rank (1-based, in the order returned). Send `numberOfResults` = the `top` passed in.
  4. AWS exceptions bubble up; `ProviderErrorClassifier` already knows `AmazonServiceException`.
  5. `AddKnowledgeBase(IConfiguration)`: registers the client (from `BedrockAgentRuntimeClientFactory` with `Bedrock:*`) and the adapter **only when `KnowledgeBase:Id` is set**. `AddInfrastructure` can't do this: it has no configuration at registration time. Nothing in `AiLab.Rag`, the API or the console calls it; Task 4.1's eval fixture does.
- **Check:** `dotnet build`.
- **Done when:** builds; `AddKnowledgeBase` registers nothing without the setting; `AiLab.Rag` unchanged.

**Done (handed over):** `AiLab.Infrastructure/KnowledgeBase/`: `KnowledgeBaseSettings` (`KnowledgeBase:Id`, `:SearchType` SEMANTIC/HYBRID checked at registration, `:ModelArn` for Block 5; `From(config)` → null without an Id), `BedrockKnowledgeBaseRetriever` (public, takes `IAmazonBedrockAgentRuntime` + settings; `numberOfResults` = top, 1–100 checked before any call; `overrideSearchType` only when set; `CorpusPath` = the key after `s3://<bucket>/`, unescaped, and a non-S3 location throws instead of inventing a source; rank = return order; AWS errors not caught), `KnowledgeBaseServices.AddKnowledgeBase(config)` (nothing registered without the Id; client from `Bedrock:*`, SDK defaults). Nothing in `AiLab.Rag`, the API or the console calls it.

> Result (2026-11-25): build 0 errors; `git diff --stat AiLab.Rag` empty.

### Task 2.4 — An offline test through the real SDK ✅
- **You'll build:** `AiLab.Api.Tests/BedrockKnowledgeBaseRetrieverTests.cs`.
- **Open:** `BedrockEmbeddingTests.cs` (lines 50–70: a real `AmazonBedrockRuntimeClient` built in the test with `BasicAWSCredentials("test", "test")` and a fake `HttpClientFactory`: copy the approach).
- **Steps:**
  1. Build a real `AmazonBedrockAgentRuntimeClient` the same way (fake credentials, a region, the fake `HttpClientFactory`) and pass it to the adapter's constructor.
  2. The fake handler answers `POST /knowledgebases/<id>/retrieve` with 2 results (`s3://ai-lab-docs/README.md` and `s3://ai-lab-docs/docs/contracts.md`, scores, texts).
  3. Assert: 2 passages, `Source` = `README.md` and `docs/contracts.md`, ranks 1 and 2, scores kept.
  4. Assert the request body had `numberOfResults` 10 (the `top` passed in) and the query text, and that the request was signed (`AWS4-HMAC-SHA256`).
  5. One more test: `AddKnowledgeBase` without `KnowledgeBase:Id` registers no `IKnowledgeBaseRetriever`.
- **Check:** `dotnet test --filter BedrockKnowledgeBaseRetrieverTests`.
- **Done when:** green, offline.

**Done (handed over):** `AiLab.Api.Tests/BedrockKnowledgeBaseRetrieverTests.cs`, 12 tests: a real `AmazonBedrockAgentRuntimeClient` (fake credentials, fake `HttpClientFactory`, no retries) answering like Retrieve: the request is `POST /knowledgebases/<id>/retrieve`, signed `AWS4-HMAC-SHA256`, body with the query text and `numberOfResults` 10, no `overrideSearchType` unless set (`SEMANTIC` test); passages `README.md` and `docs/contracts.md`, ranks 1 and 2, scores kept; top 0 / 101 refused before any call; `CorpusPath` on 3 URIs (incl. another bucket and `%20`) and a non-S3 location; `AddKnowledgeBase` without an Id registers nothing, with an Id resolves the retriever and a us-west-2 client without calling AWS, refuses `SearchType=keyword`, and refuses the emulator CA without `ServiceUrl`.

> Result (2026-11-25): `dotnet test AiLab.Api.Tests --filter BedrockKnowledgeBaseRetrieverTests` 12/12 passed, offline.

---

## Block 3: Passages → line ranges (45 min) — not done

### Task 3.1 — The passage locator
- **You'll build:** `AiLab.Evals/Retrieval/PassageLocator.cs`: given a corpus-relative path and a passage text, return the 1-based start and end lines in `data/corpus/<path>`, or "not found" with a reason.
- **Open:** `RecallAtKEvaluator.IsHit` (it needs file + start + end line), `RetrievedChunk`.
- **What a passage looks like (checked on the emulator, 2026-11-24):** not verbatim. All whitespace, newlines included, is collapsed to single spaces (`"# Architecture\n\n## System context…"` comes back as `"# Architecture ## System context…"`), and the passage stops mid-line. After collapsing whitespace on both sides, all 3 passages matched their file at offset 0. Real Bedrock may keep newlines: the locator must work either way.
- **Steps:**
  1. Read the file once (cache per path), as it is on disk.
  2. Build its normalised form (every run of whitespace → one space, trimmed) **and, alongside it, the original line number of each normalised character**. Lines are counted only here, in the original file: the passage may have no newlines to count.
  3. Normalise the passage the same way and find it in the file's normalised form.
  4. Start line = the line of the first matched character; end line = the line of the last matched character (a passage cut mid-line ends on that line).
  5. Not found → no range, a reason ("passage not found in docs/contracts.md"), never a guess. No prefix fallback: all real emulator passages match in full; if one is ever needed, its range is marked "located by prefix" and listed, never counted as a plain hit.
- **Check:** `dotnet build`.
- **Done when:** builds; plain values in, range or reason out; no line counted from the passage text.
- **Why:** the KB returns text, not line numbers. Without this, its passages can't be scored against the same gold lines as pgvector's chunks.

### Task 3.2 — Tests on the real corpus
- **Steps:**
  1. The emulator's real shape: the first 2,389 characters of `docs/architecture.md` with whitespace collapsed to single spaces (no newlines, cut mid-line 24) → lines 1–24 (§20's emulator range).
  2. A passage from the middle of `docs/contracts.md`, newlines kept (real Bedrock's possible shape) → its exact lines.
  3. The same passage with `\r\n` and doubled spaces → the same lines.
  4. A passage that ends mid-line → the end line is the line holding its last character.
  5. A text that isn't in the file → not found, with the file in the reason.
- **Check:** `dotnet test --filter PassageLocatorTests`.
- **Done when:** green.

---

## Block 4: Retrieval, scored on the KB → D1 (60 min) — not done

### Task 4.1 — A `kb` retrieval runner
- **You'll build:** `AiLab.Evals/KnowledgeBaseRetrievalEvalTests.cs` (a separate class, gated by `AILAB_EVAL`) with its own small fixture, and a source-aware `RetrievalReport`.
- **Open:** `RetrievalEvalTests.cs` (`Retrieval_hit_rate`, `Top`), `Hosting/EvalFixture.cs` (lines 50–66: why not reuse it), `Retrieval/RetrievalReport.cs` (`Build`, line 27's hard-coded header).
- **Steps:**
  1. Why a separate class, not `[InlineData("kb")]`: `EvalFixture` starts `AiLab.Rag` and probes `/search` (Postgres + Ollama), and the Rag host only has an `IKnowledgeBaseRetriever` if `AiLab.Rag/Program.cs` registers one, which today's scope forbids.
  2. The fixture: configuration from environment variables (`KnowledgeBase__Id`, `Bedrock__*`), a `ServiceCollection` with `AddKnowledgeBase(config)`, resolve `IKnowledgeBaseRetriever`. A clear failure if `KnowledgeBase:Id` is missing. No Rag host, no Ollama, no Postgres.
  3. For each answerable row (the same rows as `Retrieval_hit_rate`): call the retriever with the row's question and `RetrievalEvalTests.Top` (10).
  4. Map each passage through `PassageLocator` to a `RetrievedChunk`. A passage that can't be located counts as a **non-hit and is listed** (file + reason), not dropped.
  5. Score with `RetrievalMetrics.FirstHitRank`, exactly as for `/search`.
  6. `RetrievalReport.Build`: replace the hard-coded `` `GET /search?…` `` header with a source line passed in (the `/search` callers pass today's text, so their reports don't change), show the chunker as "n/a" for the KB, and add a **Not located** section (row, rank, file, reason; "none" when empty).
  7. The label in the header comes from the endpoint: `Bedrock:ServiceUrl` **or** `AWS_ENDPOINT_URL` set → "KB on aws-emulator (measured, stand-in)". Otherwise the label is "KB on Bedrock (measured)", which only a real-account run can produce.
  8. Write `eval-runs/retrieval/<date>-kb-emulator.md` (`-kb-bedrock.md` on the real account; Task 1.3).
- **Check:** `dotnet build`; offline tests still green, including `RetrievalReportTests` (the `/search` report is unchanged), plus a new test for the Not located section and the label.
- **Done when:** builds; the runner needs neither Ollama nor Postgres; unlocated passages listed; the label can't say Bedrock on the emulator.

### Task 4.2 — Run it on the emulator
- **Steps:**
  1. Safety check → `SAFE`.
  2. ```bash
     source ~/.aws/emulator/ai-lab-kb.env
     AILAB_EVAL=1 KnowledgeBase__Id=$KB_ID Bedrock__ServiceUrl=https://127.0.0.1:9297 \
     Bedrock__EmulatorCaPath=$HOME/.aws/emulator/9297/ca.pem \
     dotnet test AiLab.Evals --filter KnowledgeBaseRetrievalEvalTests
     ```
  3. Open `eval-runs/retrieval/<date>-kb-emulator.md`. Check the header says "KB on aws-emulator (measured, stand-in)" and read the Not located section.
  4. Compare with your Task 1.2 ceiling and prediction.
- **Done when:** the report exists; hit@5 **equals** the ceiling (4 documents, top 10: storage decides it; any difference means a source path or the locator is wrong: back to Block 3); the difference in hit@1, hit@3 and MRR from your prediction explained in one sentence. ✅ **D1 (retrieval)**

### Task 4.3 — pgvector again, same day, same corpus
- **Steps:**
  1. `AILAB_EVAL=1 dotnet test AiLab.Evals --filter "FullyQualifiedName~Retrieval_hit_rate&(DisplayName~vector|DisplayName~hybrid)"` (heading chunks).
  2. Check the vector run still matches week 5 (the runner already compares with Tuesday's ranks).
- **Done when:** both reports regenerated; any drift from week 5 explained.
- **Why:** a side-by-side from different days could be measuring a corpus change, not the pipelines.

---

## Block 5: Answers through the same suite (should-have, 90 min) — not done

### Task 5.1 — `RetrieveAndGenerate` behind the port
- **Steps:**
  1. Add `AskAsync(string question, CancellationToken ct)` to the KB port (or a second port, `IKnowledgeBaseAsk`: decide), returning the answer text and citations (passage source + text).
  2. Implement it in the adapter with `RetrieveAndGenerateAsync` (model ARN from `KnowledgeBase:ModelArn`).
  3. Offline test with the fake handler (answer + 2 citations).
- **Check:** `dotnet test --filter BedrockKnowledgeBase`.
- **Done when:** green.

### Task 5.2 — Score KB answers with the same code
- **Steps:**
  1. A test in `AiLab.Evals` (gated by `AILAB_EVAL`, using Task 4.1's fixture) that, per row × run: calls the KB ask, maps citations through `PassageLocator`, and runs `GoldCheck.Evaluate(row, answer, status, gate, citations)` with the answer, the status (`grounded` if it answered; the KB has no abstain status), the gate `"none"` (the KB has no gate: neither `too_far` nor `no_hits`, so E5 is scored Wrong, which is the honest result) and the located citations. Unlocated citations are listed, as in 4.1.
  2. Store results through the reporting configuration (judge `none`, execution `kb-emulator`) so `CostPerCorrectReport` and `GoldCheckReport` can read them.
  3. Cost: the emulator returns no token usage, and `CostPerCorrectReport` prices from model + tokens. A KB run without usage must show cost as `?` (unknown), never `$0`; add an offline test for that.
- **Check:** `dotnet build`; offline tests green.
- **Done when:** builds; E5 is Wrong for the KB; a usage-less run reads `?`, not $0.

### Task 5.3 — Run it, and label it honestly
- **Steps:**
  1. One run (`AILAB_EVAL_RUNS=1`) on the emulator.
  2. **Expected:** the emulator's answer quotes the retrieved passages, so the gold check finds a fact only if retrieval found its passage, and abstain rows fail (the KB never abstains; no gate).
  3. Write the label into the report: "emulator answers are quoted passages: this measures retrieval, not generation".
- **Done when:** results stored, report labelled.

### Task 5.4 — What the gate does that the KB doesn't
- **Steps:** count the rows where `/ask`'s gate abstained (E5, and any too-far row) and what the KB returned for the same rows. One paragraph for the D2 doc.
- **Done when:** paragraph written.
- **Why:** "no gate" is a real difference of the managed path on any account, not an emulator artefact.

---

## Block 6: Side by side → D2 (45 min) — not done

### Task 6.1 — The comparison doc
- **You'll build:** `docs/evals/managed-vs-own-rag-2026-11-24.md`.
- **Steps:** sections, in this order:
  1. **The question and the rule** (from Task 1.4).
  2. **Retrieval table** (24 English answerable rows): pgvector vector · pgvector hybrid · KB on aws-emulator · KB on Bedrock (pending). Columns: hit@1, hit@3, hit@5, MRR, rows not located.
  3. **Answers table** (if Block 5 is done): pgvector HH gold check (48/48, from Thursday) · KB on aws-emulator (proxy, labelled) · KB on Bedrock (pending).
  4. **The pipeline table** from Task 1.1, with one column for "what you control".
  5. **Latency** (median / p95 per query, both sides, same machine) and **cost model**: pgvector = Ollama/Haiku per answer (Thursday's numbers); KB = model tokens + embedding at ingestion + the vector store (link the Bedrock pricing page; no numbers you didn't check).
  6. **What the emulator column can't tell you**, in 3 bullets (§20).
- **Done when:** every number in it traces to a generated file; Claude checks each one.

### Task 6.2 — Apply the rule
- **Steps:** under the doc's rule, one paragraph: what the emulator run decides (the harness works / doesn't) and what it doesn't (anything about Bedrock), and what the real-account run must show.
- **Done when:** the paragraph is there and matches Task 1.4. ✅ **D2**

---

## Block 7: Real-account runbook + housekeeping (40 min) — not done

### Task 7.1 — Runbook: "Score the golden set on a real Knowledge Base"
- **Where:** `docker/aws-setup/real-account-gaps.md`, the managed-KB section (it says a real KB "isn't covered here").
- **Steps:** list, in order: what a real KB needs (S3 bucket with `data/corpus`, S3 Vectors index 1024 cosine, IAM role, KB with Titan v2, data source, ingestion), the IAM additions (`bedrock:Retrieve`, `bedrock:RetrieveAndGenerate` on the KB ARN), the two commands from Tasks 4.2 and 5.3 without emulator variables, and the cost to check first (vector store + ingestion embeddings + answer tokens) on the pricing page.
- **Done when:** the section exists; every step is marked "ran on the emulator" or "real account only".

### Task 7.2 — Record
- `experiments.md` section 25, "Managed vs our RAG on the golden set": setup, rule, the ceiling and prediction (1.2), the retrieval results (4.2, 4.3), answers (5.x if done), the gate difference, conclusion and decision.
- `CLAUDE.md`: Week 8 Tuesday entry (`IKnowledgeBaseRetriever`, `KnowledgeBasePassage`, `BedrockAgentRuntimeClientFactory` and the shared client-config helper, `AddKnowledgeBase(config)` and the `KnowledgeBase:*` keys, `PassageLocator`, `KnowledgeBaseRetrievalEvalTests`, the script's new default `data/corpus` and `--delete`).
- `EmulatorHttpClientFactory`'s comment: replace "Week 8 Block 4 (BedrockKbAskService in Infrastructure) reuses this one" with what actually reuses it now (the Agent Runtime factory).
- `README.md`: one line in the evals section.
- **Done when:** written; Claude checks the numbers against the generated files.

### Task 7.3 — Ship
- `dotnet test` green (count = start + new tests) → commit → open a PR.

---

## End of day
- [ ] D1 (retrieval scored on the emulator KB) and D2 (side-by-side doc) checked
- [ ] `docker/bedrock-kb/bedrock-kb-emulator.sh stop`
- [ ] Ask Claude for today's recap PDF (for OneDrive)

> **Short on time?** 0 → 2 → 3 → 4 → 6. Block 3's locator and Block 4's `kb` mode are the heart of D1: the same gold lines and the same metric on both sides.
