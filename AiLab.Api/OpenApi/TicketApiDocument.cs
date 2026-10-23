using Microsoft.OpenApi;

namespace AiLab.Api.OpenApi;

public static class TicketApiDocument
{
    // OpenAPI document at /openapi/v1.json, built from the endpoint metadata and the records' [Description]s
    public static IServiceCollection AddTicketApiDocument(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "AiLab ticket analysis API",
                Version = "v1",
                Description = """
                    Analyzes support tickets for a multi-tenant SaaS with an LLM (Ollama llama3.2 by default, or Claude Haiku).
                    The model calls one tool, GetRecentChanges, to read a fake change log, then returns a summary,
                    a probable cause and test cases. A lab project: the response includes diagnostics (tool calls, tokens, cost).
                    """,
            };
            return Task.CompletedTask;
        }));
}
