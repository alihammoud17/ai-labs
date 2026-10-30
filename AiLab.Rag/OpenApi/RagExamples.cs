using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AiLab.Rag.OpenApi;

// Examples shown in Swagger UI. The 200s are real replies (Ollama nomic-embed-text, Postgres; search and seed 2026-10-27,
// ingest and chunks 2026-10-28; the ingest one is a re-ingest after two edits, to show embedded / unchanged / deleted;
// ask 2026-10-30 on llama3.2, distances shortened to 4 decimals);
// the errors are the bodies Middleware/RagExceptionHandler.cs and the endpoints return.
public static class RagExamples
{
    public static Task ApplySearch(OpenApiOperation operation, OpenApiOperationTransformerContext _, CancellationToken __)
    {
        SetResponseExample(operation, "200", "\"Is caching turned off?\" on Postgres", """
            {
              "query": "Is caching turned off?",
              "top": 5,
              "chunker": null,
              "provider": "postgres",
              "elapsedMs": 18,
              "hits": [
                { "rank": 1, "key": "lines:similarity-sentences.txt:5", "text": "Caching is not enabled.", "source": "similarity-sentences.txt", "chunker": "lines", "heading": null, "position": 5, "startLine": 5, "endLine": 5, "distance": 0.1594, "similarity": 0.8406 },
                { "rank": 2, "key": "lines:similarity-sentences.txt:4", "text": "Caching is enabled.", "source": "similarity-sentences.txt", "chunker": "lines", "heading": null, "position": 4, "startLine": 4, "endLine": 4, "distance": 0.1643, "similarity": 0.8357 },
                { "rank": 3, "key": "lines:similarity-sentences.txt:3", "text": "API keys are scoped to a single tenant.", "source": "similarity-sentences.txt", "chunker": "lines", "heading": null, "position": 3, "startLine": 3, "endLine": 3, "distance": 0.5497, "similarity": 0.4503 },
                { "rank": 4, "key": "lines:similarity-sentences.txt:10", "text": "The car needs a new head gasket.", "source": "similarity-sentences.txt", "chunker": "lines", "heading": null, "position": 10, "startLine": 10, "endLine": 10, "distance": 0.5581, "similarity": 0.4419 },
                { "rank": 5, "key": "lines:similarity-sentences.txt:6", "text": "Responses are cached in Redis for five minutes.", "source": "similarity-sentences.txt", "chunker": "lines", "heading": null, "position": 6, "startLine": 6, "endLine": 6, "distance": 0.5658, "similarity": 0.4342 }
              ]
            }
            """);
        SetResponseExample(operation, "400", "top outside 1–20", """{ "error": "top must be between 1 and 20.", "code": "invalid_request" }""");
        SetResponseExample(operation, "409", "Nothing seeded yet", """{ "error": "Collection 'corpus' does not exist. Index some chunks first, or check VectorStore:Collection. In Development: POST /dev/seed or POST /dev/ingest.", "code": "collection_missing" }""");
        SetCommonErrors(operation);
        return Task.CompletedTask;
    }

    public static Task ApplySeed(OpenApiOperation operation, OpenApiOperationTransformerContext _, CancellationToken __)
    {
        SetResponseExample(operation, "200", "Ten sentences into Postgres", """{ "indexed": 10, "source": "similarity-sentences.txt", "provider": "postgres", "elapsedMs": 205 }""");
        SetResponseExample(operation, "400", "Seed file missing", """{ "error": "Seed file not found: /path/to/ai-labs/inputs/similarity-sentences.txt. Check Seed:Path (relative to the repo root).", "code": "seed_file_not_found" }""");
        SetCommonErrors(operation);
        return Task.CompletedTask;
    }

    public static Task ApplyIngest(OpenApiOperation operation, OpenApiOperationTransformerContext _, CancellationToken __)
    {
        SetResponseExample(operation, "200", "Again after editing a sentence in decisions.md and removing the last section of contracts.md", """
            {
              "chunker": "both", "corpus": "/path/to/ai-labs/data/corpus", "provider": "postgres", "elapsedMs": 411,
              "files": [
                {"chunker": "fixed", "source": "README.md", "files": 1, "chunks": 5, "embedded": 0, "unchanged": 5, "deleted": 0, "minTokens": 144, "avgTokens": 428, "maxTokens": 500, "embeddingTokens": 0, "elapsedMs": 0},
                {"chunker": "fixed", "source": "docs/architecture.md", "files": 1, "chunks": 8, "embedded": 0, "unchanged": 8, "deleted": 0, "minTokens": 239, "avgTokens": 467, "maxTokens": 500, "embeddingTokens": 0, "elapsedMs": 0},
                {"chunker": "fixed", "source": "docs/contracts.md", "files": 1, "chunks": 5, "embedded": 1, "unchanged": 4, "deleted": 0, "minTokens": 148, "avgTokens": 428, "maxTokens": 500, "embeddingTokens": 158, "elapsedMs": 315},
                {"chunker": "fixed", "source": "docs/decisions.md", "files": 1, "chunks": 3, "embedded": 2, "unchanged": 1, "deleted": 0, "minTokens": 440, "avgTokens": 480, "maxTokens": 500, "embeddingTokens": 835, "elapsedMs": 53},
                {"chunker": "heading", "source": "README.md", "files": 1, "chunks": 11, "embedded": 0, "unchanged": 11, "deleted": 0, "minTokens": 16, "avgTokens": 181, "maxTokens": 381, "embeddingTokens": 0, "elapsedMs": 0},
                {"chunker": "heading", "source": "docs/architecture.md", "files": 1, "chunks": 12, "embedded": 0, "unchanged": 12, "deleted": 0, "minTokens": 72, "avgTokens": 296, "maxTokens": 513, "embeddingTokens": 0, "elapsedMs": 0},
                {"chunker": "heading", "source": "docs/contracts.md", "files": 1, "chunks": 14, "embedded": 0, "unchanged": 14, "deleted": 1, "minTokens": 58, "avgTokens": 149, "maxTokens": 390, "embeddingTokens": 0, "elapsedMs": 23},
                {"chunker": "heading", "source": "docs/decisions.md", "files": 1, "chunks": 12, "embedded": 1, "unchanged": 11, "deleted": 0, "minTokens": 47, "avgTokens": 122, "maxTokens": 360, "embeddingTokens": 68, "elapsedMs": 12}
              ],
              "totals": [
                {"chunker": "fixed", "source": null, "files": 4, "chunks": 21, "embedded": 3, "unchanged": 18, "deleted": 0, "minTokens": 144, "avgTokens": 450, "maxTokens": 500, "embeddingTokens": 993, "elapsedMs": 368},
                {"chunker": "heading", "source": null, "files": 4, "chunks": 49, "embedded": 1, "unchanged": 48, "deleted": 1, "minTokens": 16, "avgTokens": 186, "maxTokens": 513, "embeddingTokens": 68, "elapsedMs": 35}
              ]
            }
            """);
        SetResponseExample(operation, "400", "Corpus folder missing", """{ "error": "Corpus folder not found: /path/to/ai-labs/data/corpus. Check Corpus:Path (relative to the repo root).", "code": "corpus_not_found" }""");
        SetCommonErrors(operation);
        return Task.CompletedTask;
    }

    public static Task ApplyChunks(OpenApiOperation operation, OpenApiOperationTransformerContext _, CancellationToken __)
    {
        SetResponseExample(operation, "200", "contracts.md, heading chunker (the first 2 of 15 shown)", """
            {
              "chunker": "heading", "source": "docs/contracts.md", "count": 15,
              "chunks": [
                {"key": "heading:docs/contracts.md:1", "chunker": "heading", "source": "docs/contracts.md", "position": 1, "heading": "Service Contracts > Contract principles", "startLine": 3, "endLine": 5, "estimatedTokens": 77, "contentHash": "97bec3ff90b8fca40e4435eba1d4cf40149e9d20c950015e0dc9a4795c177ca6", "text": "Service Contracts > Contract principles\n\nInternal contracts are explicit, versioned, authenticated, traceable, and tenant-safe. Raw provider payloads do not become .NET domain contracts. External tenant and subscription identifiers must be verified against API-owned records before state changes are applied."},
                {"key": "heading:docs/contracts.md:2", "chunker": "heading", "source": "docs/contracts.md", "position": 2, "heading": "Service Contracts > Internal billing subscription events > Endpoint", "startLine": 9, "endLine": 15, "estimatedTokens": 66, "contentHash": "cd591e61789941a042bc05c6b6a8b5eda35f7f421340bad22c661c6b3400786d", "text": "Service Contracts > Internal billing subscription events > Endpoint\n\n```http\nPOST /api/internal/billing/subscription-events\n```\n\nThe .NET API consumes this contract and remains the internal subscription system of record. BillingService is the intended producer."}
              ]
            }
            """);
        SetResponseExample(operation, "400", "Unknown chunker", """{ "error": "chunker must be one of: fixed, heading, lines.", "code": "invalid_request" }""");
        SetResponseExample(operation, "409", "Nothing stored yet", """{ "error": "Collection 'corpus' does not exist. Index some chunks first, or check VectorStore:Collection. In Development: POST /dev/seed or POST /dev/ingest.", "code": "collection_missing" }""");
        SetCommonErrors(operation);
        return Task.CompletedTask;
    }

    public static Task ApplyAsk(OpenApiOperation operation, OpenApiOperationTransformerContext _, CancellationToken __)
    {
        AddResponseExample(operation, "200", "Grounded: \"How are internal billing callbacks authenticated?\"", """
            {
              "answer": "Internal billing callbacks use a versioned DTO and timestamped HMAC over the raw body [1]. The key is `BillingIntegration:SharedSecret` [2].",
              "grounding": { "status": "grounded", "reason": "Cites [1], [2], all among the 5 sources sent." },
              "citations": [
                { "n": 1, "source": "docs/decisions.md", "heading": "Decisions and Open Review Items > Accepted decisions > Contracts are authenticated and versioned", "startLine": 15, "endLine": 17, "distance": 0.2818 },
                { "n": 2, "source": "docs/contracts.md", "heading": "Service Contracts > Internal billing subscription events > Authentication", "startLine": 17, "endLine": 41, "distance": 0.2966 }
              ],
              "retrieval": { "chunker": "heading", "top": 5, "bestDistance": 0.2818, "gate": "passed" },
              "usage": { "model": "llama3.2", "inputTokens": 1937, "outputTokens": 56 },
              "timings": { "retrievalMs": 13, "generationMs": 9838 }
            }
            """);
        AddResponseExample(operation, "200", "Abstained at the gate, no model call: \"What is the best recipe for banana bread?\"", """
            {
              "answer": "",
              "grounding": { "status": "abstained", "reason": "The closest section of the docs (distance 0.6001) is farther than 0.5; the docs probably don't cover this." },
              "citations": [],
              "retrieval": { "chunker": "heading", "top": 5, "bestDistance": 0.6001, "gate": "too_far" },
              "usage": { "model": null, "inputTokens": null, "outputTokens": null },
              "timings": { "retrievalMs": 17, "generationMs": null }
            }
            """);
        AddResponseExample(operation, "200", "Abstained by the model (notInDocs): \"Which email provider sends the verification emails?\"", """
            {
              "answer": "The documentation doesn't cover which email provider sends the verification emails.",
              "grounding": { "status": "abstained", "reason": "The model said the sources don't answer the question." },
              "citations": [],
              "retrieval": { "chunker": "heading", "top": 5, "bestDistance": 0.3882, "gate": "passed" },
              "usage": { "model": "llama3.2", "inputTokens": 2525, "outputTokens": 31 },
              "timings": { "retrievalMs": 27, "generationMs": 2187 }
            }
            """);
        SetResponseExample(operation, "400", "Question too long", """{ "error": "Question must be at most 500 characters.", "code": "invalid_request" }""");
        SetResponseExample(operation, "409", "Nothing ingested yet", """{ "error": "Collection 'corpus' does not exist. Index some chunks first, or check VectorStore:Collection. In Development: POST /dev/seed or POST /dev/ingest.", "code": "collection_missing" }""");
        SetResponseExample(operation, "429", "Over the limit", """{ "error": "Too many questions: at most 10 per minute. Try again later.", "code": "rate_limited" }""");
        SetCommonErrors(operation);
        // Four codes share the 502: the embedding one from SetCommonErrors, plus the model's three
        AddResponseExample(operation, "502", "Chat model unreachable", """{ "error": "The model provider is unavailable.", "code": "provider_unavailable" }""");
        AddResponseExample(operation, "502", "Unreadable reply, after one repair", """{ "error": "The model's reply could not be read as an answer.", "code": "reply_unreadable" }""");
        AddResponseExample(operation, "502", "Reply cut off at Ask:MaxOutputTokens", """{ "error": "The model's reply was cut off before it finished.", "code": "reply_cut_off" }""");
        return Task.CompletedTask;
    }

    private static void SetCommonErrors(OpenApiOperation operation)
    {
        SetResponseExample(operation, "502", "Ollama not running", """{ "error": "The embedding provider failed. Is Ollama running? Start it, and check Embedding:OllamaUrl (http://localhost:11434).", "code": "embedding_unavailable" }""");
        SetResponseExample(operation, "503", "Postgres not running", """{ "error": "The vector store is unreachable. Is Postgres running? Start it with: docker compose up -d (port 5433).", "code": "vector_store_unavailable" }""");
        SetResponseExample(operation, "500", "A bug of ours", """{ "error": "Something went wrong.", "code": "internal_error" }""");
    }

    private static void SetResponseExample(OpenApiOperation operation, string status, string summary, string json)
    {
        if (operation.Responses?.TryGetValue(status, out var response) == true
            && response.Content?.TryGetValue("application/json", out var media) == true)
            media.Examples = new Dictionary<string, IOpenApiExample> { [summary] = new OpenApiExample { Summary = summary, Value = JsonNode.Parse(json) } };
    }

    // Next to the examples already there, for a status with several codes
    private static void AddResponseExample(OpenApiOperation operation, string status, string summary, string json)
    {
        if (operation.Responses?.TryGetValue(status, out var response) == true
            && response.Content?.TryGetValue("application/json", out var media) == true)
        {
            media.Examples ??= new Dictionary<string, IOpenApiExample>();
            media.Examples[summary] = new OpenApiExample { Summary = summary, Value = JsonNode.Parse(json) };
        }
    }
}
