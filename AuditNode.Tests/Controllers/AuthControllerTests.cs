using System.Reflection;
using System.Security.Claims;
using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IIdentityAuthService> _service = new();
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _controller = new AuthController(_service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("username", "")]
    public async Task Login_MissingFields_ReturnsBadRequestWithoutCallingService(string username, string password)
    {
        var result = await _controller.Login(new LoginRequestDto { Username = username, Password = password }, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnsSafeUnauthorized()
    {
        _service.Setup(service => service.LoginAsync(It.IsAny<LoginRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdentityAuthenticationException());

        var result = await _controller.Login(ValidLogin(), default);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorized.Value.Should().NotBeNull();
        unauthorized.Value!.ToString().Should().NotContain("invalid_grant");
    }

    [Fact]
    public async Task Login_MissingConfiguration_ReturnsSafeServerError()
    {
        _service.Setup(service => service.LoginAsync(It.IsAny<LoginRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdentityConfigurationException());

        var result = await _controller.Login(ValidLogin(), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task Login_UpstreamUnavailable_ReturnsSafeServiceUnavailable()
    {
        _service.Setup(service => service.LoginAsync(It.IsAny<LoginRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdentityUpstreamUnavailableException());

        var result = await _controller.Login(ValidLogin(), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Login_Success_ReturnsAccessTokenAndSecureRefreshCookie()
    {
        var accessToken = Guid.NewGuid().ToString("N");
        var refreshToken = Guid.NewGuid().ToString("N");
        _service.Setup(service => service.LoginAsync(It.IsAny<LoginRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityTokenSet(accessToken, refreshToken, 300, 1800));

        var result = await _controller.Login(ValidLogin(), default);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<AuthenticationResponseDto>().Subject;
        response.AccessToken.Should().Be(accessToken);
        typeof(AuthenticationResponseDto).GetProperties().Should().NotContain(property =>
            property.Name.Contains("Refresh", StringComparison.OrdinalIgnoreCase));
        AssertSecureRefreshCookie(_controller.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Register_Success_ReturnsCreated()
    {
        _service.Setup(service => service.RegisterAsync(It.IsAny<RegisterRequestDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Register(ValidRegistration(), default);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
    }

    [Fact]
    public async Task Register_DuplicateIdentity_ReturnsConflict()
    {
        _service.Setup(service => service.RegisterAsync(It.IsAny<RegisterRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdentityConflictException());

        var result = await _controller.Register(ValidRegistration(), default);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Refresh_MissingCookie_ReturnsUnauthorizedWithoutCallingService()
    {
        var result = await _controller.Refresh(default);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refresh_Success_RotatesSecureCookieWithoutReturningRefreshToken()
    {
        var accessToken = Guid.NewGuid().ToString("N");
        SetRefreshCookie(Guid.NewGuid().ToString("N"));
        _service.Setup(service => service.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityTokenSet(accessToken, Guid.NewGuid().ToString("N"), 300, 1800));

        var result = await _controller.Refresh(default);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<RefreshResponseDto>().Subject;
        response.AccessToken.Should().Be(accessToken);
        typeof(RefreshResponseDto).GetProperties().Should().NotContain(property =>
            property.Name.Contains("Refresh", StringComparison.OrdinalIgnoreCase));
        AssertSecureRefreshCookie(_controller.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Logout_UpstreamUnavailable_StillExpiresCookie()
    {
        SetRefreshCookie(Guid.NewGuid().ToString("N"));
        _service.Setup(service => service.LogoutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdentityUpstreamUnavailableException());

        var result = await _controller.Logout(default);

        result.Should().BeOfType<NoContentResult>();
        var cookie = _controller.Response.Headers.SetCookie.ToString();
        cookie.Should().Contain("auditnode.refresh_token=");
        cookie.Should().MatchRegex("(?i)(expires=Thu, 01 Jan 1970|max-age=0)");
    }

    [Fact]
    public async Task Logout_MissingConfiguration_ReturnsSafeServerErrorAndExpiresCookie()
    {
        SetRefreshCookie(Guid.NewGuid().ToString("N"));
        _service.Setup(service => service.LogoutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdentityConfigurationException());

        var result = await _controller.Logout(default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        _controller.Response.Headers.SetCookie.ToString().Should().Contain("auditnode.refresh_token=");
    }

    [Fact]
    public void Me_ReturnsCurrentUserFromIdentityService()
    {
        var currentUser = new CurrentUserDto { Id = "user-id", Username = "user", Roles = ["Viewer"] };
        _service.Setup(service => service.GetCurrentUser(It.IsAny<ClaimsPrincipal>())).Returns(currentUser);

        var result = _controller.Me();

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(currentUser);
    }

    [Fact]
    public void Authorization_DefaultsToProtectedAndOnlyExpectedActionsAreAnonymous()
    {
        typeof(AuthController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        typeof(AuthController).GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();

        foreach (var action in new[] { nameof(AuthController.Login), nameof(AuthController.Register), nameof(AuthController.Refresh), nameof(AuthController.Logout) })
        {
            typeof(AuthController).GetMethod(action)!.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        }

        foreach (var action in new[] { nameof(AuthController.Me) })
        {
            typeof(AuthController).GetMethod(action)!.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        }
    }

    private static LoginRequestDto ValidLogin() => new() { Username = "user", Password = Guid.NewGuid().ToString("N") };

    private static RegisterRequestDto ValidRegistration() => new()
    {
        Username = "user",
        Email = "user@example.test",
        Password = Guid.NewGuid().ToString("N")
    };

    private void SetRefreshCookie(string value) =>
        _controller.Request.Headers.Cookie = $"auditnode.refresh_token={value}";

    private static void AssertSecureRefreshCookie(string cookie)
    {
        var normalized = cookie.ToLowerInvariant();
        normalized.Should().Contain("auditnode.refresh_token=");
        normalized.Should().Contain("httponly");
        normalized.Should().Contain("secure");
        normalized.Should().Contain("samesite=none");
        normalized.Should().Contain("path=/api/v1/auth");
    }
}
