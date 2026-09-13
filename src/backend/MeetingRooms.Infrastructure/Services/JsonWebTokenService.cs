using System.Globalization;
using System.Security.Claims;
using MeetingRooms.Application.Auth;
using MeetingRooms.Application.DTOs.Auth;
using MeetingRooms.Application.Interfaces;
using MeetingRooms.Application.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MeetingRooms.Infrastructure.Services;

/// <inheritdoc cref="IAccessTokenService"/>
public sealed class JsonWebTokenService : IAccessTokenService
{
    private readonly IOptions<JwtOptions> _jwtOptions;
    private readonly TimeProvider _timeProvider;

    public JsonWebTokenService(IOptions<JwtOptions> jwtOptions, TimeProvider timeProvider)
    {
        _jwtOptions = jwtOptions;
        _timeProvider = timeProvider;
    }

    public IssuedAccessToken Issue(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var options = _jwtOptions.Value;

        if (!options.TryGetSigningKeyBytes(out var signingKeyBytes))
        {
            // Unreachable: the same check runs at startup with ValidateOnStart, so a bad key
            // stops the application from booting rather than reaching a login.
            throw new InvalidOperationException(
                $"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)} is not a valid "
                + $"base64 key of at least {JwtOptions.MinimumSigningKeyBytes} bytes.");
        }

        // The composition root owns the clock, so expiry is derived from TimeProvider rather
        // than from DateTime.UtcNow - which is what lets a test control it.
        var issuedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = issuedAtUtc.AddMinutes(options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>(user.Roles.Count + 2)
        {
            new(AppClaimTypes.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(AppClaimTypes.Email, user.Email)
        };

        claims.AddRange(user.Roles.Select(role => new Claim(AppClaimTypes.Role, role)));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = issuedAtUtc,
            NotBefore = issuedAtUtc,
            Expires = expiresAtUtc,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(signingKeyBytes),
                SecurityAlgorithms.HmacSha256)
        };

        return new IssuedAccessToken(new JsonWebTokenHandler().CreateToken(tokenDescriptor), expiresAtUtc);
    }
}
