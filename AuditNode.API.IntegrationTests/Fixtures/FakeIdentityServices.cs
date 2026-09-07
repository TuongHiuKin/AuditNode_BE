using System.Security.Claims;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;

namespace AuditNode.API.IntegrationTests.Fixtures;

public sealed class FakeIdentityServices : IIdentityAuthService, IIdentityAdminService
{
    public bool RejectRefreshToken { get; set; }

    public Task<IdentityTokenSet> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new IdentityTokenSet("test-access-token", "test-refresh-token", 300, 3600));

    public Task RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IdentityTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (RejectRefreshToken)
            throw new IdentityAuthenticationException();

        return Task.FromResult(new IdentityTokenSet("refreshed-access-token", "rotated-refresh-token", 300, 3600));
    }

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public CurrentUserDto GetCurrentUser(ClaimsPrincipal principal) => new()
    {
        Id = principal.FindFirst("sub")?.Value ?? string.Empty,
        Username = principal.FindFirst("preferred_username")?.Value ?? string.Empty,
        Email = principal.FindFirst(ClaimTypes.Email)?.Value,
        Roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()
    };

    public Task<IReadOnlyList<IdentityAdminUserDto>> ListUsersAsync(string? search, int first, int max, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IdentityAdminUserDto>>([]);

    public Task SetEnabledAsync(string userId, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CreateUserAsync(CreateIdentityAdminUserDto request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetSystemAdminAsync(string userId, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IdentityAdminUserDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<IdentityAdminUserDto?>(null);
}
