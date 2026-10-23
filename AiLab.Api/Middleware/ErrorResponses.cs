using AiLab.Domain.Constants;
using AiLab.Domain.Responses;
using Microsoft.AspNetCore.Diagnostics;

namespace AiLab.Api.Middleware;

public static class ErrorResponses
{
    // Unhandled exceptions become the same short { error, code } body as every other error: a 500, never a stack trace,
    // also in Development. A body that can't be read throws BadHttpRequestException; it keeps its 400.
    public static IApplicationBuilder UseErrorResponses(this IApplicationBuilder app) =>
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = context =>
            {
                Exception? ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                (int status, ErrorResponse body) = ex is BadHttpRequestException bad
                    ? (bad.StatusCode, new ErrorResponse("The request body could not be read.", ErrorCodes.InvalidRequest))
                    : (StatusCodes.Status500InternalServerError, new ErrorResponse("Something went wrong.", ErrorCodes.InternalError));
                context.Response.StatusCode = status;
                return context.Response.WriteAsJsonAsync(body);
            },
        });
}
