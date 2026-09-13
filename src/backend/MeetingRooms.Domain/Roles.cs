using System.Collections.Frozen;

namespace MeetingRooms.Domain;

/// <summary>
/// The role names, declared once so that the seeder, the registration path and every
/// <c>[Authorize(Roles = ...)]</c> attribute cannot drift apart on a typo - a mistake that
/// presents as a 403 rather than as a compile error.
/// <para>
/// There is no promote-to-admin flow: self-registration always assigns <see cref="User"/>, and
/// the single admin account is seeded from configuration (<c>docs/requirements.md</c>).
/// </para>
/// </summary>
public static class Roles
{
    public const string User = "User";

    public const string Admin = "Admin";

    /// <summary>
    /// Every role the seeder creates. Frozen because it is built once at startup and only ever
    /// read afterwards.
    /// </summary>
    public static readonly IReadOnlySet<string> AllRoles = new[] { User, Admin }.ToFrozenSet();
}
