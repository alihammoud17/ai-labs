using AiLab.Api.OpenApi;
using AiLab.Domain.DTOs;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Responses;

namespace AiLab.Api.Endpoints;

public static class AnalyzeTicketEndpoint
{
    // The endpoint only validates and maps outcomes to status codes; the work is in TicketAnalyzer.
    // The CancellationToken is the request's (client disconnect, later the request timeout) and goes all the way down.
    public static IEndpointConventionBuilder MapAnalyzeTicket(this IEndpointRouteBuilder app) =>
        app.MapPost("/analyze-ticket", async (AnalyzeTicketRequest request, ITicketAnalyzer analyzer, CancellationToken cancellationToken) =>
        {
            if (request.Validate() is { } errors)
                return Results.ValidationProblem(errors);

            try
            {
                return Results.Ok(await analyzer.AnalyzeAsync(request, cancellationToken));
            }
            catch (ModelCallException ex)
            {
                return Results.Json(new ErrorResponse(ex.Message, ex.Code), statusCode: StatusCodes.Status502BadGateway);
            }
            catch (TicketRejectedException ex)
            {
                return Results.Json(new ErrorResponse(ex.Message, ex.Code), statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        // The endpoint returns IResult, so OpenAPI can't see the response types; they are declared here
        .WithName("AnalyzeTicket")
        .WithTags("Tickets")
        .WithSummary("Analyze a support ticket")
        .WithDescription("""
            An AI model reads the ticket and the component's recent changes (the tool GetRecentChanges, up to 3 tool round
            trips, then one final answer).

            **It can:** restate the problem, suggest a probable cause from the component's recent deployed changes,
            and draft test cases.

            **It can't:** see code, logs, tenant data or anything outside the change log. It can't confirm that a change
            really explains the problem, and it often says the cause is unknown. Answers vary between calls.

            **Reading the response:** `aiAnalysis` is the model's guess. `verification` is checked by the server against
            what the tool returned in this request:
            - `grounded`: the cause cites only versions the tool returned. Real evidence, not proof: the change may still not explain the problem.
            - `unknown`: the model says the cause is unknown.
            - `unverified`: the cause cites a version the tool didn't return, or names a cause without a version. Treat it as a guess.

            `evidence` lists the change-log entries a grounded cause relies on, copied by the server.
            Each call costs real money on Anthropic (about $0.001 on Haiku); Ollama is free.
            """)
        .Produces<AnalyzeTicketResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
        .Produces<ErrorResponse>(StatusCodes.Status502BadGateway)
        .Produces<ErrorResponse>(StatusCodes.Status504GatewayTimeout)
        .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
        .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
            AnalyzeTicketExamples.Apply(operation, context.ApplicationServices.GetRequiredService<ITestTicketStore>(), cancellationToken))
        // Last: it returns the general IEndpointConventionBuilder, which has no Produces
        .WithRequestTimeout("analyze-ticket");
}
