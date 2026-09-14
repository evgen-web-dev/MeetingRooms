using System.Collections.Frozen;
using MeetingRooms.Application.Errors;

namespace MeetingRooms.Api.Errors;

/// <summary>
/// The single place an Application error code becomes an HTTP status code.
/// Each feature phase adds its own rows here; nothing else in the API decides status.
/// </summary>
public static class ErrorStatusCodeMapper
{
    private static readonly IReadOnlyDictionary<string, int> StatusCodesMap = BuildStatusCodesMap();

    /// <summary>
    /// Unmapped codes fall back to 500 on purpose: a missing row is a bug, and a bug
    /// should be loud rather than quietly presenting itself to the client as a 400.
    /// </summary>
    public static int GetStatusCodeForError(string error, int fallbackStatusCode = StatusCodes.Status500InternalServerError) =>
        StatusCodesMap.TryGetValue(error, out var statusCode)
            ? statusCode
            : fallbackStatusCode;

    private static IReadOnlyDictionary<string, int> BuildStatusCodesMap()
    {
        var statusCodesMap = new Dictionary<string, int>
        {
            [GenericErrorCodes.UnexpectedError] = StatusCodes.Status500InternalServerError,

            // 401 rather than 400: the credentials were understood and rejected.
            [AuthErrorCodes.InvalidEmailOrPassword] = StatusCodes.Status401Unauthorized,

            // 409 rather than 400: the request is well formed, and conflicts with state that
            // already exists.
            [AuthErrorCodes.EmailAlreadyRegistered] = StatusCodes.Status409Conflict,

            [RoomErrorCodes.RoomNotFound] = StatusCodes.Status404NotFound,

            // 409 rather than 403: the caller is allowed to delete rooms, and this one is
            // refused because of state - a booking that cannot be cancelled - not permission.
            [RoomErrorCodes.RoomHasBookedSlots] = StatusCodes.Status409Conflict,

            [BookingErrorCodes.SlotNotFound] = StatusCodes.Status404NotFound,

            // The conflict contract the assignment names, and 409 rather than 400 for the same
            // reason as the row above: the request is well formed and the caller is permitted -
            // somebody else simply got there first.
            [BookingErrorCodes.SlotAlreadyBooked] = StatusCodes.Status409Conflict,

            // Also state rather than input. The slot exists and the request said nothing wrong;
            // the window it names has closed.
            [BookingErrorCodes.SlotHasEnded] = StatusCodes.Status409Conflict
        };

        // Every password-policy failure is the same answer - the submitted password is not
        // acceptable - so the set is enumerated rather than written out as six identical rows.
        foreach (var passwordPolicyErrorCode in PasswordPolicyErrorCodes.All)
        {
            statusCodesMap[passwordPolicyErrorCode] = StatusCodes.Status400BadRequest;
        }

        return statusCodesMap.ToFrozenDictionary();
    }
}
