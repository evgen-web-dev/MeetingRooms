using MeetingRooms.Api.Errors;
using MeetingRooms.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api.ExceptionHandlers;

/// <summary>
/// The safety net. Expected failures are returned as an <c>OperationResult</c> and never
/// reach here, so anything this handler sees is by definition unclassified - a bug. It is
/// logged in full server-side and reported to the client as nothing but
/// <c>UnexpectedError</c>.
/// </summary>
public sealed class AppExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<AppExceptionHandler> _logger;

    public AppExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<AppExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path.Value);

        var statusCode = ErrorStatusCodeMapper.GetStatusCodeForError(GenericErrorCodes.UnexpectedError);

        // The middleware writes the response, so the status has to be on the response
        // itself, not only on the payload.
        httpContext.Response.StatusCode = statusCode;

        // Detail is deliberately left unset: the exception's message never reaches the client.
        // Title and Type are left unset too, so the writer fills them from the framework's
        // defaults for this status - the same values every other error response carries.
        var problemDetails = new ProblemDetails { Status = statusCode }
            .WithErrorCodes([GenericErrorCodes.UnexpectedError]);

        // False means no writer accepted the request (an Accept header that rules out our
        // formats). Returning it lets the middleware fall back to an empty 500 rather than
        // claiming a response was written.
        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }
}
