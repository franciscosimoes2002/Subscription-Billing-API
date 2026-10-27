using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubscriptionBilling.Api.Http;
using SubscriptionBilling.Application.Auth;

namespace SubscriptionBilling.Api.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController(AuthService auth) : ApiControllerBase
{
    /// <summary>Creates an account. Every self-registered account has the <c>User</c> role.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        Respond(await auth.RegisterAsync(request, cancellationToken), user => StatusCode(StatusCodes.Status201Created, user));

    /// <summary>Exchanges credentials for a short-lived bearer token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        Respond(await auth.LoginAsync(request, cancellationToken), Ok);

    /// <summary>The authenticated user's profile.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        Respond(await auth.GetProfileAsync(CurrentUserId, cancellationToken), Ok);
}
