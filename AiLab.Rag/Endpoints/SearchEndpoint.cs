using System.ComponentModel;
using System.Diagnostics;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Domain.Responses;
using AiLab.Rag.OpenApi;

namespace AiLab.Rag.Endpoints;

public static class SearchEndpoint
{
    // The endpoint only maps; validation (empty q, top 1–20, an unknown chunker) is VectorSearchService's and comes back as a 400.
    // q is optional in the binding, so a missing q gets the same { error, code } body as an empty one.
    public static IEndpointConventionBuilder MapSearch(this IEndpointRouteBuilder app) =>
        app.MapGet("/search", async (IVectorSearchService search, IConfiguration config, ILoggerFactory loggers,
            CancellationToken cancellationToken,
            [Description("The question or text to find similar chunks for. Required.")] string? q = null,
            [Description("How many chunks to return, closest first (1–20).")] int top = 5,
            [Description("Only chunks from this chunker: fixed, heading or lines. Empty or missing searches all of them.")] string? chunker = null) =>
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                // ?chunker= (empty) means no filter, like leaving it out
                string? filter = string.IsNullOrEmpty(chunker) ? null : chunker;
                IReadOnlyList<SearchHit> hits = await search.SearchAsync(q ?? "", top, filter, cancellationToken);

                return Results.Ok(new SearchResponse(q ?? "", top, filter, config["VectorStore:Provider"] ?? "", stopwatch.ElapsedMilliseconds,
                    hits.Select((hit, i) => new SearchResponseHit(
                            i + 1, hit.Chunk.Key, hit.Chunk.Text, hit.Chunk.Source, hit.Chunk.Chunker, hit.Chunk.Heading,
                            hit.Chunk.Position, hit.Chunk.StartLine, hit.Chunk.EndLine, hit.Score, Math.Round(1 - hit.Score, 4)))
                        .ToList()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return RagErrors.ToResult(ex, config, loggers.CreateLogger(typeof(SearchEndpoint).FullName!));
            }
        })
        // The endpoint returns IResult, so OpenAPI can't see the response types; they are declared here
        .WithName("Search")
        .WithTags("Search")
        .WithSummary("Find the stored chunks closest to a query")
        .WithDescription("""
            Embeds `q` with the embedding model and returns the `top` closest chunks from the vector store, closest first.

            **Reading the response:** `distance` is cosine distance, **lower = closer** (0 = the same text).
            `similarity` is 1 − distance, only to compare with the console's `/similarity` matrix.
            Closeness is about topic, not meaning: "Caching is enabled." and "Caching is not enabled." score almost the same.

            `chunker` limits the search to one chunking strategy (`fixed`, `heading` or `lines`); without it, all of them compete.
            Each hit says where it came from: `source`, `heading` (the section path), `position` and the line range.

            `provider` says which store answered (`postgres` or `inmemory`). Nothing has been stored until `POST /dev/seed`
            or `POST /dev/ingest` runs.
            """)
        .Produces<SearchResponse>(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
        .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
        .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
        .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
        .AddOpenApiOperationTransformer(RagExamples.ApplySearch);
}
