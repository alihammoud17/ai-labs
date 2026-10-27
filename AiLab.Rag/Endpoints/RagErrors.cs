using System.Net;
using System.Net.Sockets;
using AiLab.Domain.Constants;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Responses;
using Npgsql;

namespace AiLab.Rag.Endpoints;

// Exceptions as the same short { error, code } body as the API. The endpoints call this from their catch blocks:
// the API's UseErrorResponses lives in AiLab.Api, and moving it to a shared place waits for POST /ask.
public static class RagErrors
{
    public static IResult ToResult(Exception ex, IConfiguration config, ILogger logger)
    {
        // The connectors wrap driver errors (Postgres: VectorStoreException "Call to vector store failed."), so look down the chain
        if (Find<NpgsqlException>(ex) is not null || Find<SocketException>(ex) is not null)
        {
            logger.LogWarning(ex, "Vector store unreachable");
            return Error(StatusCodes.Status503ServiceUnavailable, ErrorCodes.VectorStoreUnavailable,
                "The vector store is unreachable. Is Postgres running? Start it with: docker compose up -d (port 5433).");
        }

        // Same hints as the console's /similarity: no status code means nothing answered, 404 means the model isn't pulled
        if (Find<HttpRequestException>(ex) is { } http)
        {
            logger.LogWarning(ex, "Embedding provider failed");
            string hint = http.StatusCode is HttpStatusCode.NotFound || http.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? $"Is the model pulled? Run: ollama pull {config["Embedding:Model"] ?? "nomic-embed-text"}"
                : $"Is Ollama running? Start it, and check Embedding:OllamaUrl ({config["Embedding:OllamaUrl"] ?? "http://localhost:11434"}).";
            return Error(StatusCodes.Status502BadGateway, ErrorCodes.EmbeddingUnavailable, $"The embedding provider failed. {hint}");
        }

        switch (ex)
        {
            case ArgumentException argument: // includes ArgumentOutOfRangeException; the messages are the service's own checks
                return Error(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, PlainMessage(argument));

            case VectorCollectionMissingException missing:
                return Error(StatusCodes.Status409Conflict, ErrorCodes.CollectionMissing, $"{missing.Message} In Development: POST /dev/seed.");

            default:
                logger.LogError(ex, "Unhandled error");
                return Error(StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "Something went wrong.");
        }
    }

    public static IResult Error(int status, string code, string message) =>
        Results.Json(new ErrorResponse(message, code), statusCode: status);

    // .NET appends " (Parameter 'top')" and "Actual value was 0." to the message; the caller only needs the sentence
    private static string PlainMessage(ArgumentException ex) =>
        ex.ParamName is { } name && ex.Message.IndexOf($" (Parameter '{name}')", StringComparison.Ordinal) is var at and >= 0
            ? ex.Message[..at]
            : ex.Message;

    private static T? Find<T>(Exception? ex) where T : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is T match)
                return match;
        return null;
    }
}
