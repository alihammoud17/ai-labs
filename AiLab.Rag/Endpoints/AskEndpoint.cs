using AiLab.Domain.DTOs;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Responses;
using AiLab.Rag.OpenApi;

namespace AiLab.Rag.Endpoints;

public static class AskEndpoint
{
    // The rate limiter policy on /ask (AddRateLimiter in Program.cs)
    public const string RateLimitPolicy = "ask";

    // The endpoint only maps; validation, the gate, generation and the citation check are AskService's. Its exceptions
    // (400, 409, 502, 503) come back through RagExceptionHandler, an unreadable body (or "top": "5") through AiLab.Web's 400.
    public static IEndpointConventionBuilder MapAsk(this IEndpointRouteBuilder app) =>
        app.MapPost("/ask", async (AskRequest request, IAskService ask, CancellationToken cancellationToken) =>
            Results.Ok(await ask.AskAsync(request, cancellationToken)))
        .RequireRateLimiting(RateLimitPolicy)
        // The endpoint returns IResult, so OpenAPI can't see the response types; they are declared here
        .WithName("Ask")
        .WithTags("Ask")
        .WithSummary("Answer a question from the ingested docs, with numbered citations")
        .WithDescription("""
            Finds the `top` closest sections of the docs (heading chunker), and if the closest one is near enough, asks the model
            to answer from those sections only, citing them as `[n]`.

            **Reading the response:** `answer` is the model's text. `grounding` and `citations` are written by the server:
            - `grounded`: every `[n]` the answer cites is a section that was sent. Where the words came from, not proof they're right.
            - `abstained`: the docs don't answer it. Either no section was close enough (`retrieval.gate` is `no_hits` or
              `too_far`, and no model call was made) or the model said so (`answer` is one sentence saying the docs don't cover it).
            - `unverified`: the answer cites nothing, or cites a number that wasn't sent. Treat it as unsupported.

            `citations` lists the cited sections from the server's own list (file, heading, lines, distance), never from the
            model's text. `retrieval.bestDistance` is the cosine distance of the closest section (lower = closer);
            above `Ask:MaxDistance` the question abstains.

            Limited to 10 requests per minute per client IP (`Ask:PermitsPerMinute`); over it: 429 `rate_limited`.
            Nothing has been stored until `POST /dev/ingest` runs.
            """)
        .Produces<AskResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
        .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
        .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
        .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
        .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
        .AddOpenApiOperationTransformer(RagExamples.ApplyAsk);
}
