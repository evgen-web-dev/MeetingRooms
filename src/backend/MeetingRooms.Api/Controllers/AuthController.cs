using MeetingRooms.Api.Authentication;
using MeetingRooms.Api.DTOs;
using MeetingRooms.Application.DTOs.Auth;
using MeetingRooms.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRooms.Api.Controllers;

/// <summary>
/// Registration, login, and reading back who the caller is.
/// <para>
/// The route is written out rather than assembled from the <c>[controller]</c> token, so that
/// renaming this class cannot change a URL a client depends on.
/// </para>
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Creates an account. Every self-registered account gets the User role; there is no role
    /// choice and no promote-to-admin flow.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var registerResult = await _authService.RegisterAsync(request, cancellationToken);

        if (!registerResult.Succeeded)
        {
            return registerResult.ToProblemDetailsResult(this);
        }

        // 201 with no Location header: nothing in this API exposes a user as a resource, and
        // inventing an endpoint purely to have something to point at would be surface added
        // for a header. RFC 9110 allows the omission.
        return StatusCode(StatusCodes.Status201Created, registerResult.Value);
    }

    /// <summary>
    /// Exchanges credentials for an access token. There is no refresh token: the token's
    /// lifetime is the session.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var loginResult = await _authService.LoginAsync(request, cancellationToken);

        return loginResult.Succeeded
            ? Ok(loginResult.Value)
            : loginResult.ToProblemDetailsResult(this);
    }

    /// <summary>
    /// The caller, according to their own token. Reads claims and touches no database: every
    /// value is already inside the token that authorised the request.
    /// <para>
    /// It also earns its keep as a diagnostic. Issuance and validation have to agree on claim
    /// types, and a disagreement is invisible - the token looks right and role-gated endpoints
    /// answer 403. This endpoint is where that shows up instead.
    /// </para>
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<MeResponse> Me() =>
        Ok(new MeResponse(User.GetUserId(), User.GetEmail(), User.GetRoles()));
}
