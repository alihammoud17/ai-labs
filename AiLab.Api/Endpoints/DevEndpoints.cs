using AiLab.Domain.Constants;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Responses;

namespace AiLab.Api.Endpoints;

// Endpoints for Development only; Program.cs maps them inside its IsDevelopment() check
public static class DevEndpoints
{
    // The ticket eval: every TestTickets/ticket-*.json, N runs each, scored against expected.json.
    // No request timeout: 12 Haiku requests take minutes. maxCost stops it once the summed cost passes the limit.
    public static IEndpointConventionBuilder MapDevEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/dev/eval-tickets", async (ITicketEval eval, CancellationToken cancellationToken, int runs = 3, decimal? maxCost = null) =>
            runs is < 1 or > 10
                ? Results.BadRequest(new ErrorResponse("runs must be between 1 and 10.", ErrorCodes.InvalidRequest))
                : Results.Text(await eval.RunAsync(runs, maxCost, cancellationToken)))
        .WithTags("Dev")
        .WithSummary("Run and score every test ticket (Development only)")
        .WithDescription("Each run is a real model call: free on Ollama, about $0.0006 per ticket on Haiku. Replies are saved under eval-runs/.");
}
