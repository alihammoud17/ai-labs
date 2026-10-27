using System.Diagnostics;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Domain.Responses;

namespace AiLab.Rag.Endpoints;

public static class SearchEndpoint
{
    // The endpoint only maps; validation (empty q, top 1–20) is VectorSearchService's and comes back as a 400.
    // q is optional in the binding, so a missing q gets the same { error, code } body as an empty one.
    public static IEndpointConventionBuilder MapSearch(this IEndpointRouteBuilder app) =>
        app.MapGet("/search", async (IVectorSearchService search, IConfiguration config, ILoggerFactory loggers,
            CancellationToken cancellationToken, string? q = null, int top = 5) =>
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                IReadOnlyList<SearchHit> hits = await search.SearchAsync(q ?? "", top, cancellationToken);

                return Results.Ok(new SearchResponse(q!, top, config["VectorStore:Provider"] ?? "", stopwatch.ElapsedMilliseconds,
                    hits.Select((hit, i) => new SearchResponseHit(i + 1, hit.Key, hit.Text, hit.Source, hit.Score, Math.Round(1 - hit.Score, 4)))
                        .ToList()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return RagErrors.ToResult(ex, config, loggers.CreateLogger(typeof(SearchEndpoint).FullName!));
            }
        });
}
