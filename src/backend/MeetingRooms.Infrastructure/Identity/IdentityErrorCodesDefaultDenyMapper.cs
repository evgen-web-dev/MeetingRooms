using MeetingRooms.Application.Errors;
using Microsoft.AspNetCore.Identity;

namespace MeetingRooms.Infrastructure.Identity;

/// <summary>
/// Translates ASP.NET Core Identity's error codes into this application's, on a default-deny
/// basis: a code has to appear in the map for the operation that produced it, or it is dropped.
/// <para>
/// Default-deny rather than pass-everything-through is what stops a future Identity version
/// from opening a leak by adding a code nobody here has considered. The cost is that an
/// unmapped failure arrives as <see cref="GenericErrorCodes.UnexpectedError"/> - a 500, which
/// is the correct answer for a failure this application cannot classify.
/// </para>
/// </summary>
internal static class IdentityErrorCodesDefaultDenyMapper
{
    /// <summary>A null value means "emit Identity's own code"; a non-null value replaces it.</summary>
    private static readonly IReadOnlyDictionary<string, string?> UserCreationMap =
        new Dictionary<string, string?>
        {
            // The policy failures are safe to surface verbatim: they describe the submitted
            // password, and say nothing about whether an account exists.
            [PasswordPolicyErrorCodes.PasswordTooShort] = null,
            [PasswordPolicyErrorCodes.PasswordRequiresDigit] = null,
            [PasswordPolicyErrorCodes.PasswordRequiresLower] = null,
            [PasswordPolicyErrorCodes.PasswordRequiresUpper] = null,
            [PasswordPolicyErrorCodes.PasswordRequiresNonAlphanumeric] = null,
            [PasswordPolicyErrorCodes.PasswordRequiresUniqueChars] = null,

            // The user name is the email address, so both arrive at the same meaning.
            [IdentityCodes.DuplicateUserName] = AuthErrorCodes.EmailAlreadyRegistered,
            [IdentityCodes.DuplicateEmail] = AuthErrorCodes.EmailAlreadyRegistered
        };

    private static readonly IReadOnlyDictionary<string, string?> AddToRoleMap =
        new Dictionary<string, string?>
        {
            // AddToRoleAsync does not only write the join row - it re-runs user validation, so
            // user-name and email codes can come back from a role assignment. Found the
            // expensive way in the reference project, kept here because it is still true.
            [IdentityCodes.DuplicateUserName] = AuthErrorCodes.EmailAlreadyRegistered,
            [IdentityCodes.DuplicateEmail] = AuthErrorCodes.EmailAlreadyRegistered
        };

    /// <summary>
    /// Role codes pass through verbatim: only the seeder creates roles, at startup, and its
    /// failure becomes an exception message in a log rather than a response to anyone.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> RoleCreationMap =
        new Dictionary<string, string?>
        {
            [IdentityCodes.InvalidRoleName] = null,
            [IdentityCodes.DuplicateRoleName] = null
        };

    public static IReadOnlyList<string> ForUserCreation(IEnumerable<IdentityError> errors) =>
        Map(errors, UserCreationMap);

    public static IReadOnlyList<string> ForAddToRole(IEnumerable<IdentityError> errors) =>
        Map(errors, AddToRoleMap);

    public static IReadOnlyList<string> ForRoleCreation(IEnumerable<IdentityError> errors) =>
        Map(errors, RoleCreationMap);

    private static IReadOnlyList<string> Map(
        IEnumerable<IdentityError> errors,
        IReadOnlyDictionary<string, string?> map)
    {
        var mapped = new List<string>();

        // Distinct, and in the order Identity reported them. The user name is the email
        // address, so a duplicate registration fails as DuplicateUserName *and* DuplicateEmail,
        // and both translate to one code - which would otherwise reach the client twice.
        var alreadyMapped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var error in errors)
        {
            if (map.TryGetValue(error.Code, out var replacement) && alreadyMapped.Add(replacement ?? error.Code))
            {
                mapped.Add(replacement ?? error.Code);
            }
        }

        // Every code was dropped, which means Identity failed for a reason this application has
        // never classified. Returning an empty list is not an option - OperationResult.Failure
        // rejects one - and inventing a plausible-looking code would be a lie, so it becomes
        // the same unclassified failure a thrown exception would.
        return mapped.Count > 0 ? mapped : [GenericErrorCodes.UnexpectedError];
    }

    /// <summary>
    /// Identity's own code strings. They are compared as text either way; naming them here
    /// keeps the literals out of the maps above.
    /// </summary>
    private static class IdentityCodes
    {
        public const string DuplicateUserName = "DuplicateUserName";
        public const string DuplicateEmail = "DuplicateEmail";
        public const string InvalidRoleName = "InvalidRoleName";
        public const string DuplicateRoleName = "DuplicateRoleName";
    }
}
