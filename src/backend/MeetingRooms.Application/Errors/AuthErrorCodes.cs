namespace MeetingRooms.Application.Errors;

/// <summary>Business error codes for registration and login.</summary>
public static class AuthErrorCodes
{
    /// <summary>
    /// The single literal for every login failure - unknown email and wrong password alike.
    /// Two distinct codes would tell an attacker which addresses have accounts, and the leak
    /// reopens silently the moment a second literal appears here (lockout is the usual
    /// culprit), so it is cheap to preserve and invisible to add back later.
    /// </summary>
    public const string InvalidEmailOrPassword = "InvalidEmailOrPassword";

    /// <summary>
    /// Deliberately specific, unlike the login code above and unlike the reference project,
    /// which folds it into a generic code to hide account existence. A registration endpoint
    /// cannot hide it: the request fails either way, so the only thing a vague code achieves
    /// is a form that cannot tell the user what to fix.
    /// </summary>
    public const string EmailAlreadyRegistered = "EmailAlreadyRegistered";
}
