using System.Net;
using AiLab.Domain.Constants;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Web;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace AiLab.Rag.Middleware;

// The exceptions Rag knows, as { error, code }. Runs inside the shared UseErrorResponses (AiLab.Web), so the endpoints
// don't catch anything. An exception not mapped here returns false and becomes the shared 500 internal_error.
// Provider failures are recognised by IProviderErrorClassifier (Infrastructure), so this host knows no provider SDK types:
// Ollama's HttpRequestException and Bedrock's AmazonServiceException both become 502 embedding_unavailable.
public sealed class RagExceptionHandler(IConfiguration config, IProviderErrorClassifier providerErrors, ILogger<RagExceptionHandler> logger)
    : IExceptionHandler
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

        // Every model call /ask makes is wrapped in ModelCallException above, so a provider failure that gets here came from the
        // embedding provider (indexing, or embedding the query). The classifier is asked about every exception in the chain:
        // the connectors and the MEAI middleware may wrap the SDK's own. After Npgsql, whose timeouts wrap a TimeoutException.
        if (FindProviderFailure(ex) is { } failure)
        {
            logger.LogWarning(ex, "Embedding provider failed");
            return (StatusCodes.Status502BadGateway, ErrorCodes.EmbeddingUnavailable, $"The embedding provider failed. {EmbeddingHint(failure)}");
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

    // A fixed hint per provider; the provider's own message stays in the log (it can name the account or the endpoint)
    private string EmbeddingHint(Exception failure)
    {
        string model = config["Embedding:Model"] ?? "";
        if (config["Embedding:Provider"] == "bedrock")
            return $"Check Embedding:Model ({model}), that the model is available to this AWS account, and the Bedrock endpoint " +
                   "(Bedrock:Region, or Bedrock:ServiceUrl on the emulator) and credentials.";

        // Ollama, as the console's /similarity says it: no status code means nothing answered, 404 means the model isn't pulled
        bool notPulled = failure is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
            || failure.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
        return notPulled
            ? $"Is the model pulled? Run: ollama pull {(model.Length > 0 ? model : "nomic-embed-text")}"
            : $"Is Ollama running? Start it, and check Embedding:OllamaUrl ({config["Embedding:OllamaUrl"] ?? "http://localhost:11434"}).";
    }

    private Exception? FindProviderFailure(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (providerErrors.IsProviderFailure(ex))
                return ex;
        return null;
    }

    private static T? Find<T>(Exception? ex) where T : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is T match)
                return match;
        return null;
    }
}
