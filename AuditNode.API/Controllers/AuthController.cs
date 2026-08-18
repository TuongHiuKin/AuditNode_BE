using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshCookieName = "auditnode.refresh_token";
    private const string RefreshCookiePath = "/api/v1/auth";
    private readonly IIdentityAuthService _identityAuthService;

    public AuthController(IIdentityAuthService identityAuthService)
    {
        _identityAuthService = identityAuthService;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Username and password are required." });
        }

        try
        {
            var tokens = await _identityAuthService.LoginAsync(request, cancellationToken);
            SetRefreshCookie(tokens.RefreshToken, tokens.RefreshExpiresIn);
            return Ok(new AuthenticationResponseDto
            {
                AccessToken = tokens.AccessToken,
                ExpiresIn = tokens.ExpiresIn
            });
        }
        catch (IdentityAuthenticationException)
        {
            return Unauthorized(new { error = "Invalid username or password." });
        }
        catch (IdentityConfigurationException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Authentication service is not configured." });
        }
        catch (IdentityUpstreamUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Authentication service is unavailable." });
        }
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Username, email, and password are required." });
        }

        try
        {
            await _identityAuthService.RegisterAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created);
        }
        catch (IdentityConflictException)
        {
            return Conflict(new { error = "Username or email already exists." });
        }
        catch (IdentityConfigurationException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Authentication service is not configured." });
        }
        catch (IdentityUpstreamUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Authentication service is unavailable." });
        }
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new { error = "Refresh session is missing." });
        }

        try
        {
            var tokens = await _identityAuthService.RefreshAsync(refreshToken, cancellationToken);
            SetRefreshCookie(tokens.RefreshToken, tokens.RefreshExpiresIn);
            return Ok(new RefreshResponseDto
            {
                AccessToken = tokens.AccessToken,
                ExpiresIn = tokens.ExpiresIn
            });
        }
        catch (IdentityAuthenticationException)
        {
            ExpireRefreshCookie();
            return Unauthorized(new { error = "Refresh session is invalid." });
        }
        catch (IdentityConfigurationException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Authentication service is not configured." });
        }
        catch (IdentityUpstreamUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Authentication service is unavailable." });
        }
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        try
        {
            if (Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken) &&
                !string.IsNullOrWhiteSpace(refreshToken))
            {
                await _identityAuthService.LogoutAsync(refreshToken, cancellationToken);
            }
        }
        catch (IdentityAuthenticationException)
        {
            // The local session is still cleared when the upstream session is already invalid.
        }
        catch (IdentityUpstreamUnavailableException)
        {
            // Logout remains locally effective when Keycloak is temporarily unavailable.
        }
        catch (IdentityConfigurationException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Authentication service is not configured." });
        }
        finally
        {
            ExpireRefreshCookie();
        }

        return NoContent();
    }

    [HttpGet("me")]
    public IActionResult Me() => Ok(_identityAuthService.GetCurrentUser(User));

    private void SetRefreshCookie(string refreshToken, int refreshExpiresIn)
    {
        Response.Cookies.Append(RefreshCookieName, refreshToken, CookieOptions(
            TimeSpan.FromSeconds(Math.Max(refreshExpiresIn, 1))));
    }

    private void ExpireRefreshCookie()
    {
        Response.Cookies.Delete(RefreshCookieName, CookieOptions(TimeSpan.Zero));
    }

    private static CookieOptions CookieOptions(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        Path = RefreshCookiePath,
        IsEssential = true,
        MaxAge = maxAge
    };
}
