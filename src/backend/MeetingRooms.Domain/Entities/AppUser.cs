using Microsoft.AspNetCore.Identity;

namespace MeetingRooms.Domain.Entities;

/// <summary>
/// The application's user. Deliberately empty: <see cref="IdentityUser{TKey}"/> already carries
/// everything this project stores about a person - an email, a password hash, the normalised
/// lookup keys - and <c>docs/requirements.md</c> asks for nothing beyond email and password.
/// <para>
/// The <c>int</c> key is a decision, not a default. Identity's own default is a string holding a
/// GUID; slots reference their booker by a foreign key, so that default would put an
/// <c>nvarchar(450)</c> column on the one table the booking path writes to.
/// </para>
/// <para>
/// This type is the single exception to "Domain depends on nothing": it forces a reference to
/// <c>Microsoft.Extensions.Identity.Stores</c>. The alternative was keeping the Identity user in
/// Infrastructure and giving domain entities a bare user id with no navigation property. See
/// <c>docs/decisions.md</c>.
/// </para>
/// </summary>
public sealed class AppUser : IdentityUser<int>
{
}
