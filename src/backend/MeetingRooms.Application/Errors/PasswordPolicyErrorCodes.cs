using System.Collections.Frozen;

namespace MeetingRooms.Application.Errors;

/// <summary>
/// ASP.NET Core Identity's own password-policy codes, declared here as constants so that the
/// Infrastructure map that lets them through and the API map that gives them a status code
/// reference one spelling rather than two string literals that can drift apart.
/// <para>
/// The policy itself is Identity's and stays there: restating it in a request validator would
/// mean maintaining the same rules in two places. The visible consequence is that a weak
/// password comes back as a <c>ProblemDetails</c> carrying these codes rather than as a
/// field-keyed <c>ValidationProblemDetails</c>.
/// </para>
/// </summary>
public static class PasswordPolicyErrorCodes
{
    public const string PasswordTooShort = "PasswordTooShort";
    public const string PasswordRequiresDigit = "PasswordRequiresDigit";
    public const string PasswordRequiresLower = "PasswordRequiresLower";
    public const string PasswordRequiresUpper = "PasswordRequiresUpper";
    public const string PasswordRequiresNonAlphanumeric = "PasswordRequiresNonAlphanumeric";
    public const string PasswordRequiresUniqueChars = "PasswordRequiresUniqueChars";

    /// <summary>
    /// Every code above. Lets both maps enumerate the set instead of repeating six rows, so
    /// adding a seventh code is one line rather than three.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new[]
    {
        PasswordTooShort,
        PasswordRequiresDigit,
        PasswordRequiresLower,
        PasswordRequiresUpper,
        PasswordRequiresNonAlphanumeric,
        PasswordRequiresUniqueChars
    }.ToFrozenSet();
}
