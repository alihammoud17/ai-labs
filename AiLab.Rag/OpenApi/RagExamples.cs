using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AiLab.Rag.OpenApi;

// Examples shown in Swagger UI. The 200s are real replies (Ollama nomic-embed-text, Postgres, 2026-10-27);
// the errors are the bodies RagErrors returns.
public static class RagExamples
{
    public static Task ApplySearch(OpenApiOperation operation, OpenApiOperationTransformerContext _, CancellationToken __)
    {
        SetResponseExample(operation, "200", "\"Is caching turned off?\" on Postgres", """
            {
              "query": "Is caching turned off?",
              "top": 5,
              "provider": "postgres",
              "elapsedMs": 18,
              "hits": [
                { "rank": 1, "key": "similarity-sentences:5", "text": "Caching is not enabled.", "source": "similarity-sentences.txt", "distance": 0.1594, "similarity": 0.8406 },
                { "rank": 2, "key": "similarity-sentences:4", "text": "Caching is enabled.", "source": "similarity-sentences.txt", "distance": 0.1643, "similarity": 0.8357 },
                { "rank": 3, "key": "similarity-sentences:3", "text": "API keys are scoped to a single tenant.", "source": "similarity-sentences.txt", "distance": 0.5497, "similarity": 0.4503 },
                { "rank": 4, "key": "similarity-sentences:10", "text": "The car needs a new head gasket.", "source": "similarity-sentences.txt", "distance": 0.5581, "similarity": 0.4419 },
                { "rank": 5, "key": "similarity-sentences:6", "text": "Responses are cached in Redis for five minutes.", "source": "similarity-sentences.txt", "distance": 0.5658, "similarity": 0.4342 }
              ]
            }
            """);
        SetResponseExample(operation, "400", "top outside 1–20", """{ "error": "top must be between 1 and 20.", "code": "invalid_request" }""");
        SetResponseExample(operation, "409", "Nothing seeded yet", """{ "error": "Collection 'chunks' does not exist. Index some chunks first, or check VectorStore:Collection. In Development: POST /dev/seed.", "code": "collection_missing" }""");
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
