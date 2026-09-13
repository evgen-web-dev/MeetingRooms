namespace MeetingRooms.Application.Options;

/// <summary>
/// Access token configuration. Everything except <see cref="SigningKey"/> is plain
/// configuration and lives in <c>appsettings.json</c>; the key comes from user-secrets locally
/// and from an App Service application setting in Azure, and never from a file in this
/// repository.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HS256 signs with a symmetric key, and a key shorter than the hash it feeds is a weakness
    /// the library will not report at configuration time - only by throwing when the first
    /// token is issued, which is a 500 on someone's first login rather than a refusal to boot.
    /// </summary>
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Base64-encoded key bytes, not a passphrase.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenLifetimeMinutes { get; set; }

    /// <summary>
    /// Decodes <see cref="SigningKey"/> and checks its length. Used by startup validation and
    /// by token issuance, so the check and the use cannot disagree about what a valid key is.
    /// </summary>
    public bool TryGetSigningKeyBytes(out byte[] signingKeyBytes)
    {
        signingKeyBytes = [];

        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            return false;
        }

        var buffer = new byte[((SigningKey.Length * 3) / 4) + 3];

        if (!Convert.TryFromBase64String(SigningKey, buffer, out var bytesWritten)
            || bytesWritten < MinimumSigningKeyBytes)
        {
            return false;
        }

        signingKeyBytes = buffer[..bytesWritten];

        return true;
    }
}
