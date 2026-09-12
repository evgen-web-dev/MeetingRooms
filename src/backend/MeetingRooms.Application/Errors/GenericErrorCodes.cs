namespace MeetingRooms.Application.Errors;

/// <summary>
/// Error codes that are not specific to any one feature area.
/// Feature areas get their own class (AuthErrorCodes, BookingErrorCodes, ...).
/// </summary>
public static class GenericErrorCodes
{
    /// <summary>
    /// Nothing classified this failure. Emitted only by the global exception handler:
    /// if a use case returns it deliberately, that is a missing error code, not a design.
    /// </summary>
    public const string UnexpectedError = "UnexpectedError";
}
