# Week 4 notes: embeddings, pgvector, chunking, RAG

## What I built
- Monday: I built the embedding infra and the /similarity command in the console chat.
- Tuesday: I configured pgvector in docker, built the store and the search service for vectors, and the AiLab.Rag host with GET /search.
- Wednesday: Fixed and Heading chunkers, TextChunk metadata and the ingest API, with a content hash so re-ingest only embeds changed chunks. Shared error handling for both APIs (AiLab.Web).
- Thursday: A Lab to review the work for the past 3 days.
- Friday: POST /ask API, ask prompt builder and ask-v1 prompt. Citation checker and a rate limiter. Eval of 10 questions on llama3.2 vs Haiku.
- Saturday: Failure log: 20 questions, 44 runs, retrieval vs generation errors.

## What I understand now that I didn't on Monday
- Embeddings capture the topic, not the meaning: "caching is enabled" and "caching is not enabled" score 0.94.
- How you chunk decides what retrieval can find: a 3-line ADR got lost in a fixed 2,000-character window, but ranked first as its own heading section.
- Content hashing makes re-ingest cheap: only the chunks that changed are embedded again.
- Distance alone can't catch a near-miss: "What is multi-tenancy?" sat closer to the docs than real questions, so only the model can say it's not in the docs.
- "Grounded" only proves the cited source was sent, not that the answer is right.
- On this corpus, RAG mostly fails at generation, not retrieval (in English).

## Numbers worth remembering
- Negation pair: 0.94 similarity, so embeddings see the topic, not the yes/no.
- Good hits: distance 0.21–0.45. The CSS question (E5) at 0.59 was the only one the 0.50 gate stopped.
- Ask eval: Haiku 8/10 correct, llama3.2 5/10.
- Cost: about $0.0004 per /ask on Haiku, $0 on llama3.2.
- Haiku needs ~1,030 output tokens for a list answer; 800 cut it off.

## Where my RAG fails
- 44 runs: 1 retrieval miss per model (the French question), against 21 generation codes on llama3.2 and 3 on Haiku.
- llama3.2 cites the wrong chunk, leaves facts out and invents numbers; Haiku's only failures were two cut-offs.
- The gate passed all three questions with no answer; only the model can catch those.
- First fix: Haiku for answers. Retrieval was right every time in English, so the model is where the errors are.

## Mistakes I made / bugs I found
- A bare `SocketException` mapped to 503, but `HttpClient` wraps one when Ollama is down, so an embedding outage looked like Postgres down.
- `ORDER BY round(distance)` made Postgres skip the HNSW index; order by the raw distance and round only for display.
- Ollama's JSON mode cut answers at the first unescaped `"`: valid JSON, half an answer. Fixed with a backticks rule and treating a missing `citedSources` as unreadable.
- Haiku's thinking used all 800 output tokens on list answers. Raised to 2,000.

## Open items carried into Week 5
- Section 12 draft marker, and the conclusions of sections 13 and 14 in my own words.
- The default chat provider for /ask (Haiku for answers, llama3.2 for development).
- Non-English questions miss at retrieval.
- Hybrid search (keywords + vectors) for exact identifiers and numbers.