using AuditNode.API.IntegrationTests.Fixtures;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Identity;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuditNode.API.IntegrationTests.Auth;

/// <summary>
/// Exercises the real ASP.NET Identity implementation against a migrated
/// PostgreSQL database. Nothing is faked here: password hashing, role storage,
/// refresh-token rotation and the last-admin invariant all run for real.
/// </summary>
[Trait("Category", "ApiIntegration")]
public sealed class IdentityAuthServiceTests(AuditNodeWebApplicationFactory factory)
    : IClassFixture<AuditNodeWebApplicationFactory>
{
    private const string Password = "Sup3rSecret!pass";

    [Fact]
    public async Task Registered_user_can_log_in_and_receives_tokens()
    {
        var username = Unique("login");
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = RealAuthService(scope.ServiceProvider);

        await auth.RegisterAsync(new RegisterRequestDto { Username = username, Email = $"{username}@example.test", Password = Password });

        var tokens = await auth.LoginAsync(new LoginRequestDto { Username = username, Password = Password });

        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.ExpiresIn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var username = Unique("wrongpw");
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = RealAuthService(scope.ServiceProvider);
        await auth.RegisterAsync(new RegisterRequestDto { Username = username, Email = $"{username}@example.test", Password = Password });

        var act = () => auth.LoginAsync(new LoginRequestDto { Username = username, Password = "not-the-password" });

        await act.Should().ThrowAsync<IdentityAuthenticationException>();
    }

    [Fact]
    public async Task Duplicate_registration_is_a_conflict()
    {
        var username = Unique("dup");
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = RealAuthService(scope.ServiceProvider);
        var request = new RegisterRequestDto { Username = username, Email = $"{username}@example.test", Password = Password };
        await auth.RegisterAsync(request);

        var act = () => auth.RegisterAsync(request);

        await act.Should().ThrowAsync<IdentityConflictException>();
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_stops_working()
    {
        var username = Unique("rotate");
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = RealAuthService(scope.ServiceProvider);
        await auth.RegisterAsync(new RegisterRequestDto { Username = username, Email = $"{username}@example.test", Password = Password });
        var first = await auth.LoginAsync(new LoginRequestDto { Username = username, Password = Password });

        var rotated = await auth.RefreshAsync(first.RefreshToken);

        rotated.RefreshToken.Should().NotBe(first.RefreshToken);

        // Replaying the rotated-away token must fail: refresh tokens are single use.
        var replay = () => auth.RefreshAsync(first.RefreshToken);
        await replay.Should().ThrowAsync<IdentityAuthenticationException>();
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var username = Unique("logout");
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = RealAuthService(scope.ServiceProvider);
        await auth.RegisterAsync(new RegisterRequestDto { Username = username, Email = $"{username}@example.test", Password = Password });
        var tokens = await auth.LoginAsync(new LoginRequestDto { Username = username, Password = Password });

        await auth.LogoutAsync(tokens.RefreshToken);

        var act = () => auth.RefreshAsync(tokens.RefreshToken);
        await act.Should().ThrowAsync<IdentityAuthenticationException>();
    }

    [Fact]
    public async Task Disabled_user_cannot_log_in_and_existing_session_stops_refreshing()
    {
        var username = Unique("disabled");
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = RealAuthService(scope.ServiceProvider);
        await auth.RegisterAsync(new RegisterRequestDto { Username = username, Email = $"{username}@example.test", Password = Password });
        var tokens = await auth.LoginAsync(new LoginRequestDto { Username = username, Password = Password });
        var user = await UserManager(scope.ServiceProvider).FindByNameAsync(username);

        await auth.SetEnabledAsync(user!.Id, enabled: false);

        var login = () => auth.LoginAsync(new LoginRequestDto { Username = username, Password = Password });
        await login.Should().ThrowAsync<IdentityAuthenticationException>();
        var refresh = () => auth.RefreshAsync(tokens.RefreshToken);
        await refresh.Should().ThrowAsync<IdentityAuthenticationException>();
    }

    [Fact]
    public async Task The_last_enabled_system_admin_cannot_be_disabled()
    {
        var username = Unique("lastadmin");
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = RealAuthService(scope.ServiceProvider);
        await auth.CreateUserAsync(new CreateIdentityAdminUserDto(username, $"{username}@example.test", Password));
        var admin = await UserManager(scope.ServiceProvider).FindByNameAsync(username);
        await auth.SetSystemAdminAsync(admin!.Id, enabled: true);

        // Demote every other administrator so this one is provably the last.
        var others = await auth.ListUsersAsync(null, 0, 1000);
        foreach (var other in others.Where(user => user.Id != admin.Id && user.IsSystemAdmin))
            await auth.SetSystemAdminAsync(other.Id, enabled: false);

        var act = () => auth.SetEnabledAsync(admin.Id, enabled: false);

        await act.Should().ThrowAsync<IdentityConflictException>();
    }

    [Fact]
    public async Task Re_registering_a_disabled_user_name_is_a_conflict_not_a_takeover()
    {
        var username = Unique("taken");
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = RealAuthService(scope.ServiceProvider);
        await auth.RegisterAsync(new RegisterRequestDto { Username = username, Email = $"{username}@example.test", Password = Password });
        var user = await UserManager(scope.ServiceProvider).FindByNameAsync(username);
        await auth.SetEnabledAsync(user!.Id, enabled: false);

        var act = () => auth.RegisterAsync(new RegisterRequestDto
        {
            Username = username,
            Email = $"other-{username}@example.test",
            Password = Password,
        });

        await act.Should().ThrowAsync<IdentityConflictException>();
    }

    [Fact]
    public async Task Access_token_carries_roles_the_api_can_authorise_on()
    {
        var username = Unique("claims");
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = RealAuthService(scope.ServiceProvider);
        await auth.CreateUserAsync(new CreateIdentityAdminUserDto(username, $"{username}@example.test", Password));
        var user = await UserManager(scope.ServiceProvider).FindByNameAsync(username);
        await auth.SetSystemAdminAsync(user!.Id, enabled: true);

        var tokens = await auth.LoginAsync(new LoginRequestDto { Username = username, Password = Password });

        var principal = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ValidateToken(tokens.AccessToken, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = TestJwtIssuer.Issuer,
                ValidAudience = TestJwtIssuer.Audience,
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(TestJwtIssuer.SigningKey)),
                RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);

        principal.IsInRole(IdentityAuthService.SystemAdminRole).Should().BeTrue();
        principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value.Should().Be(user.Id);
    }

    /// <summary>
    /// Resolves the real implementation, bypassing the fixture's fake registration.
    /// </summary>
    private static IdentityAuthService RealAuthService(IServiceProvider provider) => new(
        provider.GetRequiredService<UserManager<AppUser>>(),
        provider.GetRequiredService<RoleManager<IdentityRole>>(),
        provider.GetRequiredService<IIdentityTokenIssuer>(),
        provider.GetRequiredService<IRefreshTokenStore>(),
        provider.GetRequiredService<ISystemAdminMutationLock>());

    private static UserManager<AppUser> UserManager(IServiceProvider provider) =>
        provider.GetRequiredService<UserManager<AppUser>>();

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];
}
