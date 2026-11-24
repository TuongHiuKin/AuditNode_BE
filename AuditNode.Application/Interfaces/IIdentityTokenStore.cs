using AuditNode.Domain.Entities;

namespace AuditNode.Application.Interfaces;

public interface IRefreshTokenStore
{
    Task<RefreshTokenIssue> CreateAsync(string userId, CancellationToken cancellationToken = default);
    Task<RefreshToken?> FindActiveAsync(string token, CancellationToken cancellationToken = default);
    Task RevokeAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task<int> RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed record RefreshTokenIssue(string Token, int ExpiresIn);

public interface IIdentityTokenIssuer
{
    AccessTokenIssue CreateAccessToken(string userId, string username, string? email, IEnumerable<string> roles);
}

public sealed record AccessTokenIssue(string Token, int ExpiresIn);
