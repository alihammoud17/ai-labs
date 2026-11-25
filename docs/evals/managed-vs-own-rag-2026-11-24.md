# Managed vs our RAG on the golden set (2026-11-24/25)

The same 30-question golden set (`ask-set-v1`) against two RAG systems, scored by the same code: our pgvector RAG (`AiLab.Rag`) and a Bedrock Knowledge Base. There's no AWS account, so the Knowledge Base ran on **aws-emulator**, a stand-in that is real in shape (S3 data source, ingestion job, `Retrieve`, `RetrieveAndGenerate`, citations, sessions) but not in behaviour (keyword search, one cut-off passage per document, no model).

Labels used everywhere below: **pgvector (measured)**, **KB on aws-emulator (measured, stand-in)**, **KB on Bedrock (pending)**. The emulator column is never Bedrock.

Plan: [2026-11-24-week-8-managed-vs-own-rag.md](../plans/2026-11-24-week-8-managed-vs-own-rag.md). Every number traces to a generated file, named under each table (`eval-runs/` is git-ignored: regenerate with the commands in the plan, Tasks 4.2, 4.3 and 5.3).

## 1. The question and the rule

**Question:** does a managed Knowledge Base retrieve and answer as well as our own RAG on the same documents and questions?

**The rule** (plan Task 1.4, committed before the first KB query):
1. **Fairness:** same corpus (the 4 files of `data/corpus`, same paths), same 25 rows with gold (24 English + H5 apart), same gold lines, same top k (10 on both sides), same scoring code (`PassageLocator` → `RetrievalMetrics` for retrieval, `GoldCheck` for answers). A passage that can't be located is a non-hit and is listed; a setup failure is "missing", never "miss".
2. **Retrieval match (real account only):** KB hit@5 ≥ 23/24 and MRR within 0.05 of pgvector vector (≥ 0.684).
3. **Answer match (real account only):** gold check run 1 ≥ 14/16, abstains ≥ 11/12 plus E5, hand pass ≥ 10/12.
4. **The emulator run decides only whether the harness works:** every passage located (or listed), hit@5 equal to the predicted ceiling (7/24), the report labelled as the stand-in. Nothing about Bedrock.

## 2. Retrieval (24 English answerable rows, top 10)

| System | hit@1 | hit@3 | hit@5 | hit@10 | MRR | Passages not located | Latency per query, median / p95 |
|---|---|---|---|---|---|---|---|
| pgvector (measured), vector: `/ask`'s mode | 0.583 | 0.833 | **1.000** | 1.000 | **0.734** | n/a (chunks carry lines) | 12 / 17 ms |
| pgvector (measured), hybrid | 0.667 | 0.917 | 0.958 | 1.000 | 0.787 | n/a | 18 / 40 ms |
| pgvector (measured), keyword (closest to the emulator's search) | 0.542 | 0.708 | 0.833 | 0.958 | 0.665 | n/a | 17 / 32 ms |
| KB on aws-emulator (measured, stand-in) | 0.125 | 0.292 | **0.292** | 0.292 | **0.194** | 0 of 100 | 2 / 5 ms |
| KB on Bedrock (pending) | pending | pending | pending | pending | pending | pending | pending |

Sources: `eval-runs/retrieval/2026-11-25-vector.md`, `-hybrid.md`, `-keyword.md`, `-kb-emulator.md`. H5 (French, apart): vector rank 9, hybrid 5, keyword 2, KB on aws-emulator 3. The three pgvector runs equal their 2026-11-05 heading reports row for row (no drift); vector equals Tuesday's MEAI ranks on 25 of 25. Latency is client side on the same machine: pgvector includes the Ollama embedding of the question; the emulator's keyword scoring embeds nothing, so its 2 ms says nothing about real Bedrock.

**Why the KB column is 0.292:** the emulator stores each file as one passage cut after 2,283–2,646 characters (README lines 1–29, architecture 1–24, contracts 1–63, decisions 1–35), so only 7 of 24 rows have a gold line it ever indexed (A1, A3, N3, F1, F4, N5, H6; N5 only by line: decisions.md line 35 is cut mid-fact). That ceiling was computed before the run (plan Task 1.2) and the run hit it exactly: storage, not ranking, decides hit@5 here.

**On the 7 rows the emulator can reach**, first-hit rank per system:

| Row | pgvector vector | pgvector hybrid | pgvector keyword | KB on aws-emulator |
|---|---|---|---|---|
| A1 | 2 | 1 | 1 | 3 |
| A3 | 1 | 1 | 2 | 1 |
| N3 | 1 | 1 | 1 | 2 |
| F1 | 2 | 1 | 1 | 1 |
| F4 | 1 | 2 | 4 | 1 |
| N5 | 1 | 1 | 1 | 2 |
| H6 | 4 | 3 | 3 | 3 |

With 4 whole-file passages there are only 4 ranks to fill, so a rank-1 KB hit means "the right file scored highest", not "the right section". Against the prediction made before the run (hit@1 0.167, hit@3 0.250, MRR 0.212): N5 came 2nd (README's opening outscored decisions.md on "retry/dead-letter") and H6 3rd.

## 3. Answers

| System | Gold check, run 1 (/16) | Answer rows, run 1 (/11) | Abstains (/4 per run) | E5 refused | Cost per correct |
|---|---|---|---|---|---|
| pgvector (measured), `/ask` heading + Haiku (HH, week 5 runs) | **16** | 11 | 12/12 over 3 runs | 3/3 (gate, no model call) | $0.000428 |
| KB on aws-emulator (measured, stand-in): **a retrieval proxy** | 3 (A3, F1, F4) | 3 | 0/4 | 0/1 | ? (no token counts, model not priced) |
| KB on Bedrock (pending) | pending | pending | pending | pending | pending |

Sources: HH from `docs/evals/cost-per-correct-2026-11-19.md` (48/48 over 3 runs, 16/16 on run 1, cost) and `eval-runs/meai/goldcheck-week5.md` (answer rows 11/11, abstains 12/12, E5 3/3); KB from `eval-runs/meai/kb-emulator-summary.md` (execution `kb-emulator`, 1 run, 30 stored). **Emulator answers are quoted passages (no model runs): this measures retrieval, not generation.** The 3 correct rows are exactly the answer rows whose gold facts sit inside the stored passages; every cited passage was located.

**What the gate does that the KB doesn't.** On `/ask` the distance gate stopped exactly one question, E5 ("How do I center a div in CSS?"), in all 3 runs on both models (best distance 0.5938 > `Ask:MaxDistance` 0.50), with no model call. The other abstain rows (D2, D9, INV, N9) passed the gate and were refused by the model (`notInDocs`, prompt rule). The KB has no such step: for E5 and all 4 abstain rows it returned all 4 passages and an answer citing them. On real Bedrock `RetrieveAndGenerate` would send those passages to the model every time, billed, and rely on its prompt to decline; `Retrieve` returns its top N whatever the score. That's the managed one-call path on any account, not an emulator artefact. Keeping a gate means `Retrieve` + our own threshold on a **relevance** score (re-tuned: never 0.50 from the distance scale) + our own model call.

## 4. The two pipelines, and what you control

| Step | pgvector RAG | KB on Bedrock (real) | KB on aws-emulator (today) | What you control |
|---|---|---|---|---|
| 1. Corpus | `data/corpus` (4 `.md`), `Source` = corpus-relative path | S3 bucket synced from `data/corpus`, identity = S3 URI | the same 4 keys | Ours: everything. KB: what's in the bucket; the adapter maps URI → path. **Same today.** |
| 2. Chunking | `HeadingChunker`: one chunk per heading section (≈ 176 tokens), line numbers kept | default ≈ 300 tokens (sentence boundaries), or fixed / hierarchical / semantic / none / Lambda; no heading option; no line numbers | one whitespace-collapsed prefix per file, cut mid-line | Ours: the code. KB: a menu set at data-source creation (or your own Lambda). **Different: the main cause today.** |
| 3. Embeddings | `nomic-embed-text`, 768, local, $0 | Titan v2, 1024, billed per token; fixed at KB creation (new model = new index) | Titan configured, never called | Ours: a config value (`Embedding:*`). KB: chosen once. **Different.** |
| 4. Search | pgvector HNSW, cosine **distance** (lower is closer); vector / keyword / hybrid (RRF) | S3 Vectors; `SEMANTIC` (HYBRID only on OpenSearch Serverless); relevance **score** (higher is better) | keyword term frequency, relevance 0.09–0.40 | Ours: mode, top, fusion. KB: top 1–100, search type where the store allows. **Different; scales never compared.** |
| 5. Gate | distance > 0.50 or no hits → abstain, no model call | none | none | Ours: the threshold. KB: nothing in the one-call API. **Different on any account.** |
| 6. Generation | `ask-v1.md`, typed `AskAnswer`, one repair, `CitationCheck` (numbered sources, file + lines) | the model you name; replaceable prompt template; citations = answer spans → passages; nothing checks support; server sessions | no model: notice + quoted passages | Ours: prompt, format, checks. KB: model and prompt template. **Different.** |

## 5. Latency and cost model

**Latency (same machine, client side):** retrieval in section 2. Answers: `/ask` HH p95 3.47 s (week 5's stored runs, `cost-per-correct-2026-11-19.md`); the emulator's `RetrieveAndGenerate` took median 5 ms / p95 10 ms because no model runs, so it can't be compared. A real latency comparison needs the real account.

**Cost model:**
- **pgvector RAG:** embeddings and Postgres local ($0); generation is `/ask`'s model: HH $0.46 per 1,000 answers, $0.000428 per correct answer (measured, `cost-per-correct-2026-11-19.md`).
- **KB on Bedrock:** three meters, none measured here: (1) the answer model's tokens per `RetrieveAndGenerate` (the service reports no usage back, so it must be read from the bill or CloudWatch; Nova Lite's list price is in `ModelPrices`, $0.06 / $0.24 per 1M); (2) Titan embedding tokens at ingestion and for every query; (3) the vector store (S3 Vectors storage and queries). Prices: [Amazon Bedrock pricing](https://aws.amazon.com/bedrock/pricing/), [Amazon S3 pricing](https://aws.amazon.com/s3/pricing/) (S3 Vectors). Titan and S3 Vectors prices were not checked today, so no figure is given.

## 6. What the emulator column can't tell you

- **Chunking and embeddings:** it stores one cut-off passage per file and scores by keyword term frequency; Titan is never called. Real Bedrock's ~300-token chunks would index the 17 rows the emulator can't reach, and semantic search would rank paraphrases the keyword scorer misses.
- **Answer quality:** no model runs. The answers are a fixed notice plus the passages, so the gold check measures whether retrieval kept the fact, and no abstain row can pass.
- **Latency and cost:** a local process with no model and no embedding call. Its milliseconds and its missing token counts say nothing about Bedrock's.

## 7. Applying the rule

**What the emulator run decides: the harness works, end to end.** The adapter reached the Knowledge Base through the same Bedrock client rules as chat (`BedrockClientConfig`); all 100 retrieved passages and every cited passage were placed in their files by `PassageLocator`; hit@5 equalled the ceiling predicted before the run (7/24) exactly, so no source path or line mapping is off; the answers were stored as `kb-emulator`, read back by the same `GoldCheckReport` and `CostPerCorrectReport` as `/ask`'s executions, with cost "?" rather than $0; every report says "KB on aws-emulator (measured, stand-in)". **What it doesn't decide: anything about Bedrock.** The 0.292 / 0.194 and 3/16 are the stand-in's storage limit, not Bedrock's retrieval or answer quality, and they fill no "KB on Bedrock" cell (bars 2 and 3 are untested, not failed). **What the real-account run must show** (runbook: `docker/aws-setup/real-account-gaps.md`, "Score the golden set on a real Knowledge Base"): KB hit@5 ≥ 23/24 and MRR ≥ 0.684 (or within 0.05 of that day's vector run) for retrieval; gold check ≥ 14/16 on run 1, abstains ≥ 11/12 and the hand pass ≥ 10/12 for answers. E5 can't pass without a gate, so on answers the one-call KB fails that bar by design, which is why a managed path for `/ask` would be `Retrieve` + our own gate and prompt. Until then `/ask` stays on our pgvector RAG (heading + Haiku).
