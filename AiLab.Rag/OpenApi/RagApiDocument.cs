using AiLab.Domain.Responses;
using Microsoft.OpenApi;

namespace AiLab.Rag.OpenApi;

public static class RagApiDocument
{
    // OpenAPI document at /openapi/v1.json, built from the endpoint metadata and the records' [Description]s
    public static IServiceCollection AddRagApiDocument(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "AiLab vector search API",
                    Version = "v1",
                    Description = """
                        Stores short texts with their embeddings (nomic-embed-text on Ollama, 768 numbers each) in a vector store
                        (Postgres with pgvector, or in memory) and returns the texts closest to a query.
                        The retrieval half of RAG, in a lab project: POST /dev/ingest (the markdown corpus) or POST /dev/seed
                        (ten sentences) first, then GET /search.
                        """,
                };
                return Task.CompletedTask;
            });

            // ErrorResponse is shared with the ticket API, whose [Description] lists the ticket codes; this document lists its own.
            // Changed here, not on the record, so the ticket API's document stays as it is.
            options.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type == typeof(ErrorResponse)
                    && schema.Properties?.TryGetValue("code", out IOpenApiSchema? code) == true && code is OpenApiSchema editable)
                    editable.Description = "Stable code to branch on: invalid_request, seed_file_not_found, corpus_not_found, collection_missing, "
                        + "embedding_unavailable, vector_store_unavailable or internal_error.";
                return Task.CompletedTask;
            });
        });
}
