using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AiLab.Rag.OpenApi;

// Examples shown in Swagger UI. The 200s are real replies (Ollama nomic-embed-text, Postgres; search and seed 2026-10-27,
// ingest and chunks 2026-10-28);
// the errors are the bodies RagErrors returns.
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
        SetResponseExample(operation, "409", "Nothing seeded yet", """{ "error": "Collection 'corpus' does not exist. Index some chunks first, or check VectorStore:Collection. In Development: POST /dev/seed.", "code": "collection_missing" }""");
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
        SetResponseExample(operation, "200", "The corpus with both chunkers, on Postgres", """
            {
              "chunker": "both", "corpus": "/path/to/ai-labs/data/corpus", "provider": "postgres", "elapsedMs": 1671,
              "files": [
                {"chunker": "fixed", "source": "README.md", "files": 1, "chunks": 5, "minTokens": 144, "avgTokens": 428, "maxTokens": 500, "embeddingTokens": 2166, "elapsedMs": 637},
                {"chunker": "fixed", "source": "docs/architecture.md", "files": 1, "chunks": 8, "minTokens": 239, "avgTokens": 467, "maxTokens": 500, "embeddingTokens": 3423, "elapsedMs": 166},
                {"chunker": "fixed", "source": "docs/contracts.md", "files": 1, "chunks": 5, "minTokens": 224, "avgTokens": 443, "maxTokens": 500, "embeddingTokens": 2377, "elapsedMs": 125},
                {"chunker": "fixed", "source": "docs/decisions.md", "files": 1, "chunks": 3, "minTokens": 444, "avgTokens": 481, "maxTokens": 500, "embeddingTokens": 1242, "elapsedMs": 67},
                {"chunker": "heading", "source": "README.md", "files": 1, "chunks": 11, "minTokens": 16, "avgTokens": 181, "maxTokens": 381, "embeddingTokens": 2051, "elapsedMs": 171},
                {"chunker": "heading", "source": "docs/architecture.md", "files": 1, "chunks": 12, "minTokens": 72, "avgTokens": 296, "maxTokens": 513, "embeddingTokens": 3230, "elapsedMs": 184},
                {"chunker": "heading", "source": "docs/contracts.md", "files": 1, "chunks": 15, "minTokens": 58, "avgTokens": 145, "maxTokens": 390, "embeddingTokens": 2290, "elapsedMs": 193},
                {"chunker": "heading", "source": "docs/decisions.md", "files": 1, "chunks": 12, "minTokens": 47, "avgTokens": 123, "maxTokens": 360, "embeddingTokens": 1234, "elapsedMs": 121}
              ],
              "totals": [
                {"chunker": "fixed", "source": null, "files": 4, "chunks": 21, "minTokens": 144, "avgTokens": 454, "maxTokens": 500, "embeddingTokens": 9208, "elapsedMs": 995},
                {"chunker": "heading", "source": null, "files": 4, "chunks": 50, "minTokens": 16, "avgTokens": 184, "maxTokens": 513, "embeddingTokens": 8805, "elapsedMs": 669}
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
        SetResponseExample(operation, "409", "Nothing stored yet", """{ "error": "Collection 'corpus' does not exist. Index some chunks first, or check VectorStore:Collection. In Development: POST /dev/seed.", "code": "collection_missing" }""");
        SetCommonErrors(operation);
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
}
