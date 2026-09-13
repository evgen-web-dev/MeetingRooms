namespace MeetingRooms.Application.Options;

/// <summary>
/// The seeded administrator's credentials. Both properties are nullable and neither is
/// validated at startup on purpose: absent credentials are a warning in Development and a hard
/// failure everywhere else, and <c>ValidateOnStart</c> cannot express that distinction - it
/// would refuse to boot a developer's machine that has no admin configured.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }
}
