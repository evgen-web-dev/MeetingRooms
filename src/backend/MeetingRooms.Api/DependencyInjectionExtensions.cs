using MeetingRooms.Api.Errors;
using MeetingRooms.Application.Results;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Turns a failed use case into the API's error response.
    /// <para>
    /// The <see cref="ProblemDetails"/> is built by MVC's own
    /// <see cref="Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory"/> rather than by
    /// hand, so <c>type</c> and <c>title</c> come from the same defaults the framework uses for
    /// its automatic 400s and 404s. Hand-rolled and framework-generated failures therefore look
    /// identical to a client by construction, instead of by keeping a second table in sync.
    /// </para>
    /// </summary>
    /// <param name="controller">The calling controller, which carries both the factory and the request's <see cref="HttpContext"/>.</param>
    /// <param name="title">Overrides the framework's default title for the status code.</param>
    public static ActionResult ToProblemDetailsResult(this OperationResult result, ControllerBase controller, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        if (result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Only a failed {nameof(OperationResult)} can be turned into a problem response.");
        }

        // Errors is guaranteed non-empty: OperationResult.Failure rejects an empty list.
        var statusCode = ErrorStatusCodeMapper.GetStatusCodeForError(result.Errors[0]);

        var problemDetails = controller.ProblemDetailsFactory
            .CreateProblemDetails(controller.HttpContext, statusCode, title)
            .WithErrorCodes(result.Errors);

        return new ObjectResult(problemDetails) { StatusCode = statusCode };
    }
}
