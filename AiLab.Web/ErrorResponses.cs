using AiLab.Domain.Constants;
using AiLab.Domain.Responses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Web;

// Every error from AiLab.Api and AiLab.Rag is the same short { error, code } body.
// A host calls AddErrorResponses() on its services and UseErrorResponses() first in its pipeline, and adds an
// IExceptionHandler (AddExceptionHandler<T>) for the exceptions only it knows.
public static class ErrorResponses
{
    public static IServiceCollection AddErrorResponses(this IServiceCollection services)
    {
        // Outside Development, minimal APIs answer a request they can't read (a body that isn't JSON, a query value that
        // isn't a number) with an empty 400 and no exception. Throwing lets UseErrorResponses give it the usual body.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        services.AddExceptionHandler<BadRequestHandler>();
        return services;
    }

    // Exceptions become { error, code }. First the IExceptionHandlers (the host's, and BadRequestHandler for unreadable
    // requests) map the exceptions they know; whatever is left is a 500 "Something went wrong.", never a stack trace, also in
    // Development. Handled exceptions aren't logged by the middleware (.NET 10), so a handler logs what it needs; the 500s are.
    public static IApplicationBuilder UseErrorResponses(this IApplicationBuilder app) =>
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = context => WriteAsync(context, StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "Something went wrong."),
        });

    /// <summary>An error as an endpoint result, for errors an endpoint finds itself (a missing file, a bad parameter).</summary>
    public static IResult Result(int status, string code, string message) =>
        Results.Json(new ErrorResponse(message, code), statusCode: status);

    /// <summary>Writes an error straight to the response, for exception handlers.</summary>
    public static Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(message, code));
    }

    // The caller's request couldn't be read: its own 400 (or 413 for a body that's too large), not a bug of ours
    private sealed class BadRequestHandler : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            if (exception is not BadHttpRequestException bad)
                return false;
            await WriteAsync(context, bad.StatusCode, ErrorCodes.InvalidRequest, "The request could not be read.");
            return true;
        }
    }
}
