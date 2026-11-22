namespace AuditNode.Application.Interfaces;

public sealed record IdentityAdminUserDto(string Id, string Username, string? Email, bool Enabled, bool IsSystemAdmin = false);
public sealed record CreateIdentityAdminUserDto(string Username, string Email, string Password);
public interface IIdentityAdminService
{
    Task<IReadOnlyList<IdentityAdminUserDto>> ListUsersAsync(string? search, int first, int max, CancellationToken cancellationToken = default);
    Task SetEnabledAsync(string userId, bool enabled, CancellationToken cancellationToken = default);
    Task CreateUserAsync(CreateIdentityAdminUserDto request, CancellationToken cancellationToken = default);
    Task SetSystemAdminAsync(string userId, bool enabled, CancellationToken cancellationToken = default);
    Task<IdentityAdminUserDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
}
