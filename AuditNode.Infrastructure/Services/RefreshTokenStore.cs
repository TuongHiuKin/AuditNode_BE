using System.Security.Cryptography;
using System.Text;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

/// <summary>
/// Persists refresh tokens as SHA-256 hashes. The plaintext token never reaches
/// the database, so a read-only credential leak cannot be replayed.
/// </summary>
public sealed class RefreshTokenStore(AuditDbContext context, TimeProvider timeProvider) : IRefreshTokenStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public async Task<RefreshTokenIssue> CreateAsync(string userId, CancellationToken cancellationToken = default)
    {
        var plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var token = new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(plaintext),
            CreatedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow().Add(Lifetime),
        };
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
        return new RefreshTokenIssue(plaintext, (int)Lifetime.TotalSeconds);
    }

    public Task<RefreshToken?> FindActiveAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = Hash(token);
        var now = timeProvider.GetUtcNow();
        return context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(
                stored => stored.TokenHash == hash && stored.RevokedAt == null && stored.ExpiresAt > now,
                cancellationToken);
    }

    public async Task RevokeAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await context.RefreshTokens
            .Where(stored => stored.Id == token.Id && stored.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.RevokedAt, now), cancellationToken);
    }

    public Task<int> RevokeAllForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        return context.RefreshTokens
            .Where(stored => stored.UserId == userId && stored.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.RevokedAt, now), cancellationToken);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
