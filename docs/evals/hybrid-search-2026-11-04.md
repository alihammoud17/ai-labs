# Measuring retrieval on its own, and trying hybrid search (4 Nov 2026)

A page for someone new to this repo: what was built on Wednesday of week 5, why, and what it showed. Tuesday's page, [baseline-2026-11-03.md](baseline-2026-11-03.md), explains the project and the 30-question test set.

## The question of the day

`POST /ask` answers in two stages: **find** the chunks (retrieval), then **write** the answer from them (generation). Tuesday scored the whole thing with a judge model, which costs money and mixes the two stages. Wednesday asked two narrower questions:

1. **How good is the finding, on its own?** Without a chat model, so it's free and takes a second.
2. **Can it be improved** by adding keyword search next to vector search?

The second idea comes from Anthropic's article [Introducing Contextual Retrieval](https://www.anthropic.com/news/contextual-retrieval). Embeddings match meaning but can miss exact terms (an environment variable, an error code). Keyword search (BM25 there, Postgres full-text here) matches exact terms. Combining the two cut the article's retrieval failures by about half.

## The words used

| Term | Meaning here |
|---|---|
| **First hit rank** | The position of the first chunk that holds the gold answer (a chunk from the gold file whose lines contain a gold line). For questions needing two sources: where the second one shows up. |
| **hit@k** | The share of questions whose first hit is in the top k. hit@5 = "was the answer among the 5 chunks `/ask` sends?" |
| **MRR** | Mean reciprocal rank: the average of 1 / first hit rank. Rank 1 → 1, rank 2 → 0.5, rank 4 → 0.25, not found → 0. It rewards putting the answer *higher*. |
| **Vector search** | Closest embeddings (pgvector, cosine distance). What `/ask` used until now. |
| **Keyword search** | Postgres full-text search: the chunks sharing the most words (stems) with the question. |
| **Hybrid search** | Both, merged with **reciprocal rank fusion** (RRF): each chunk scores 1 / (60 + rank) in each list it's in, and the sums decide the order. |

## What was built

- **A free retrieval runner** (`AiLab.Evals/RetrievalEvalTests.cs`): calls only `GET /search` for the 25 answerable questions and writes hit@1/3/5/10 and MRR to `eval-runs/retrieval/`. Its vector run reproduces Tuesday's ranks, 25 of 25.
- **Keyword search in Postgres** (`PostgresKeywordSearch`). Written by hand in psql first (`eval/queries/`). Two findings shaped it:
  - Postgres's usual query (`plainto_tsquery`) requires **every** word. "Which environment variable *sets* the JWT secret?" found 0 chunks, because the docs never say "sets". Joining the words with OR puts the right chunk first.
  - Each keyword hit also carries its **cosine distance**, because `/ask` refuses to answer when the closest chunk is farther than 0.50.
- **RRF** (`RankFusion.Merge`): merges by position, because a distance (lower is better) and a text-match score (higher is better, no fixed scale) can't be added.
- **Search modes:** `GET /search?mode=vector|keyword|hybrid` and the setting `Ask:SearchMode` (still `vector`).

## What came out

| 24 English questions | hit@1 | hit@3 | hit@5 | MRR |
|---|---|---|---|---|
| vector (before) | 0.58 | 0.83 | **1.00** | 0.73 |
| keyword alone | 0.54 | 0.71 | 0.83 | 0.67 |
| **hybrid** | **0.67** | **0.92** | 0.96 | **0.79** |

- **Hybrid ranks the answer higher.** Seven questions moved up, each because the question's key terms (HMAC, JWT secret, lockout) are literally in the answering section.
- **Keyword alone is worse** than vector on every number. Merging is the point, not replacing.
- **One question got worse where it counts:** G1, "What is the contract version?". "contract" and "version" are everywhere in these docs, so the webhook contract section fell from rank 5 to 7, outside the 5 chunks `/ask` sends.

One Haiku run with hybrid ($0.19) checked what that means for the answers:

- **The French question is now answered**, correctly and in French, 3 runs out of 3 (before: "not in the docs"). One shared word, "tenant", pulled the right chunk in.
- **G1's answers became wrong:** "the docs don't give a webhook version" (they do: `2026-04-13`), 3 out of 3. The judge still gave them 5 for groundedness: it checks support, not completeness.
- The other improved questions were already answered right with the answer at rank 2 or 3, so their answers didn't change.

## The decision

The rule set before measuring: switch only if MRR goes up **and** no question drops out of the top 5. G1 drops out, so `/ask` **stays on vector search**. Hybrid stays available behind a setting. Ideas that might remove the G1 regression, each a free run of the retrieval eval: send 7 chunks in hybrid mode, weight the vector list above the keyword one, or add context to each chunk (the article's next step).

## Run it yourself

Needs Ollama (`nomic-embed-text`), Postgres (`colima start`, `docker compose up -d`) and the corpus ingested (`POST /dev/ingest?chunker=heading`, which also creates the full-text index).

```bash
# retrieval only, every mode, free (about 1 second)
AILAB_EVAL=1 dotnet test AiLab.Evals --filter RetrievalEvalTests

# try a search mode by hand
curl -G localhost:5074/search --data-urlencode "q=Which environment variable sets the JWT secret?" -d mode=hybrid -d chunker=heading

# the keyword query in psql, with each hit's cosine distance
eval/queries/cosine_distance.sh "What is the internal billing callback endpoint?"
```

Full numbers and every moved question: [experiments.md, section 16](../../experiments.md). Plan: [2026-11-04-week-5-hybrid-retrieval.md](../plans/2026-11-04-week-5-hybrid-retrieval.md).
