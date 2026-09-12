using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api.Errors;

public static class ProblemDetailsExtensions
{
    /// <summary>
    /// The RFC 7807 extension member carrying our error codes. Declared once so the
    /// controller path and the global exception handler cannot drift apart.
    /// </summary>
    public const string ErrorDetailsExtensionKey = "errorDetails";

    public static ProblemDetails WithErrorCodes(this ProblemDetails problemDetails, IReadOnlyList<string> errorCodes)
    {
        ArgumentNullException.ThrowIfNull(problemDetails);
        ArgumentNullException.ThrowIfNull(errorCodes);

        problemDetails.Extensions[ErrorDetailsExtensionKey] = errorCodes;

        return problemDetails;
    }
}
