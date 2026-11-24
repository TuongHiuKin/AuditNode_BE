using System.Security.Claims;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace AuditNode.Infrastructure.Services;

/// <summary>
/// Local identity implementation. Replaces the Keycloak-backed service so the
/// application runs with nothing but PostgreSQL.
///
/// Access tokens are self-signed JWTs; refresh tokens are opaque values persisted
/// as hashes and rotated on every refresh.
/// </summary>
public sealed class IdentityAuthService(
    UserManager<AppUser> users,
    RoleManager<IdentityRole> roles,
    IIdentityTokenIssuer tokens,
    IRefreshTokenStore refreshTokens,
    ISystemAdminMutationLock systemAdminLock) : IIdentityAuthService, IIdentityAdminService
{
    public const string SystemAdminRole = "SystemAdmin";

    public async Task<IdentityTokenSet> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByNameAsync(request.Username);
        if (user is null || !IsEnabled(user))
            throw new IdentityAuthenticationException();

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw new IdentityAuthenticationException();
        }

        await users.ResetAccessFailedCountAsync(user);
        return await IssueAsync(user, cancellationToken);
    }

    public async Task RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!await roles.RoleExistsAsync(SystemAdminRole))
            await roles.CreateAsync(new IdentityRole(SystemAdminRole));

        var user = new AppUser
        {
            UserName = request.Username,
            Email = request.Email,
            EmailConfirmed = true,
        };

        var created = await users.CreateAsync(user, request.Password);
        if (created.Succeeded) return;

        if (created.Errors.Any(error => error.Code.StartsWith("Duplicate", StringComparison.Ordinal)))
            throw new IdentityConflictException();

        throw new IdentityAuthenticationException();
    }

    public async Task<IdentityTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var stored = await refreshTokens.FindActiveAsync(refreshToken, cancellationToken);
        if (stored is null)
            throw new IdentityAuthenticationException();

        var user = await users.FindByIdAsync(stored.UserId);
        if (user is null || !IsEnabled(user))
            throw new IdentityAuthenticationException();

        await refreshTokens.RevokeAsync(stored, cancellationToken);
        return await IssueAsync(user, cancellationToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var stored = await refreshTokens.FindActiveAsync(refreshToken, cancellationToken);
        if (stored is not null)
            await refreshTokens.RevokeAsync(stored, cancellationToken);
    }

    public CurrentUserDto GetCurrentUser(ClaimsPrincipal principal) => new()
    {
        Id = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        Username = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
        Email = principal.FindFirstValue(ClaimTypes.Email),
        Roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct().ToArray(),
    };

    private async Task<IdentityTokenSet> IssueAsync(AppUser user, CancellationToken cancellationToken)
    {
        var userRoles = await users.GetRolesAsync(user);
        var access = tokens.CreateAccessToken(user.Id, user.UserName ?? string.Empty, user.Email, userRoles);
        var refresh = await refreshTokens.CreateAsync(user.Id, cancellationToken);
        return new IdentityTokenSet(access.Token, refresh.Token, access.ExpiresIn, refresh.ExpiresIn);
    }

    // ---------------- administration ----------------

    public async Task<IReadOnlyList<IdentityAdminUserDto>> ListUsersAsync(
        string? search, int first, int max, CancellationToken cancellationToken = default)
    {
        IQueryable<AppUser> query = users.Users;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            query = query.Where(user =>
                user.UserName!.Contains(needle) || (user.Email != null && user.Email.Contains(needle)));
        }

        var page = query.OrderBy(user => user.UserName).Skip(first).Take(max).ToList();
        var result = new List<IdentityAdminUserDto>(page.Count);
        foreach (var user in page)
        {
            result.Add(new IdentityAdminUserDto(
                user.Id,
                user.UserName ?? string.Empty,
                user.Email,
                IsEnabled(user),
                await users.IsInRoleAsync(user, SystemAdminRole)));
        }
        return result;
    }

    public Task SetEnabledAsync(string userId, bool enabled, CancellationToken cancellationToken = default) =>
        systemAdminLock.ExecuteAsync(async _ =>
        {
            var user = await users.FindByIdAsync(userId) ?? throw new IdentityAuthenticationException();

            if (!enabled)
                await EnsureRemainingEnabledAdminAsync(user);

            user.LockoutEnd = enabled ? null : DateTimeOffset.UtcNow.AddYears(100);
            if (enabled) user.AccessFailedCount = 0;

            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded)
                throw new IdentityInvariantViolationException();
        }, cancellationToken);

    public async Task CreateUserAsync(CreateIdentityAdminUserDto request, CancellationToken cancellationToken = default)
    {
        var user = new AppUser
        {
            UserName = request.Username,
            Email = request.Email,
            EmailConfirmed = true,
        };

        var created = await users.CreateAsync(user, request.Password);
        if (created.Succeeded) return;

        if (created.Errors.Any(error => error.Code.StartsWith("Duplicate", StringComparison.Ordinal)))
            throw new IdentityConflictException();

        throw new IdentityAuthenticationException();
    }

    public Task SetSystemAdminAsync(string userId, bool enabled, CancellationToken cancellationToken = default) =>
        systemAdminLock.ExecuteAsync(async _ =>
        {
            var user = await users.FindByIdAsync(userId) ?? throw new IdentityAuthenticationException();
            var isAdmin = await users.IsInRoleAsync(user, SystemAdminRole);

            if (enabled == isAdmin) return;

            if (!enabled)
                await EnsureRemainingEnabledAdminAsync(user);

            var mutation = enabled
                ? await users.AddToRoleAsync(user, SystemAdminRole)
                : await users.RemoveFromRoleAsync(user, SystemAdminRole);

            if (!mutation.Succeeded)
                throw new IdentityInvariantViolationException();
        }, cancellationToken);

    public async Task<IdentityAdminUserDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return null;

        return new IdentityAdminUserDto(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email,
            IsEnabled(user),
            await users.IsInRoleAsync(user, SystemAdminRole));
    }

    /// <summary>Disabled identities carry a far-future lockout end.</summary>
    private static bool IsEnabled(AppUser user) =>
        user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow;

    /// <summary>
    /// Refuses to remove the last enabled SystemAdmin, which would lock every
    /// administrator out of the product.
    /// </summary>
    private async Task EnsureRemainingEnabledAdminAsync(AppUser target)
    {
        if (!await users.IsInRoleAsync(target, SystemAdminRole)) return;

        var admins = await users.GetUsersInRoleAsync(SystemAdminRole);
        if (IsEnabled(target) && admins.Count(IsEnabled) <= 1)
            throw new IdentityConflictException();
    }
}
