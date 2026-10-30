using System.Net;
using AiLab.Domain.Constants;
using AiLab.Domain.Exceptions;
using AiLab.Web;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace AiLab.Rag.Middleware;

// The exceptions Rag knows, as { error, code }. Runs inside the shared UseErrorResponses (AiLab.Web), so the endpoints
// don't catch anything. An exception not mapped here returns false and becomes the shared 500 internal_error.
public sealed class RagExceptionHandler(IConfiguration config, ILogger<RagExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception ex, CancellationToken cancellationToken)
    {
        if (Map(ex) is not { } error)
            return false;
        await ErrorResponses.WriteAsync(context, error.Status, error.Code, error.Message);
        return true;
    }

    private (int Status, string Code, string Message)? Map(Exception ex)
    {
        // POST /ask's model call (AskService): provider_unavailable, reply_unreadable or reply_cut_off. First, because a chat
        // provider failure wraps an HttpRequestException too, and that must not read as the embedding provider below.
        // AskService logged the details; the message is short and safe to return.
        if (ex is ModelCallException model)
            return (StatusCodes.Status502BadGateway, model.Code, model.Message);

        // The connectors wrap driver errors (Postgres: VectorStoreException "Call to vector store failed."), so look down the chain.
        // Not a bare SocketException: HttpClient wraps one too when Ollama is down, and that is a 502 below
        if (Find<NpgsqlException>(ex) is not null)
        {
            logger.LogWarning(ex, "Vector store unreachable");
            return (StatusCodes.Status503ServiceUnavailable, ErrorCodes.VectorStoreUnavailable,
                "The vector store is unreachable. Is Postgres running? Start it with: docker compose up -d (port 5433).");
        }

        // Same hints as the console's /similarity: no status code means nothing answered, 404 means the model isn't pulled
        if (Find<HttpRequestException>(ex) is { } http)
        {
            logger.LogWarning(ex, "Embedding provider failed");
            string hint = http.StatusCode is HttpStatusCode.NotFound || http.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? $"Is the model pulled? Run: ollama pull {config["Embedding:Model"] ?? "nomic-embed-text"}"
                : $"Is Ollama running? Start it, and check Embedding:OllamaUrl ({config["Embedding:OllamaUrl"] ?? "http://localhost:11434"}).";
            return (StatusCodes.Status502BadGateway, ErrorCodes.EmbeddingUnavailable, $"The embedding provider failed. {hint}");
        }

        return ex switch
        {
            // Only the caller's own mistakes; any other ArgumentException is one of our checks failing, so the shared 500
            InvalidRequestException invalid => (StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, invalid.Message),
            VectorCollectionMissingException missing => (StatusCodes.Status409Conflict, ErrorCodes.CollectionMissing,
                $"{missing.Message} In Development: POST /dev/seed or POST /dev/ingest."),
            _ => null,
        };
    }

    private static T? Find<T>(Exception? ex) where T : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is T match)
                return match;
        return null;
    }
}
