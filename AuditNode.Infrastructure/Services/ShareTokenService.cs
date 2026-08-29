using System.Data;
using System.Security.Cryptography;
using System.Text;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AuditNode.Infrastructure.Services;

public sealed class ShareTokenService(
    AuditDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<ShareTokenService> logger) : IShareTokenService
{
    private static readonly byte[] InvalidHash = new byte[SHA256.HashSizeInBytes];

    public async Task<ShareTokenMutationResult> CreateAsync(
        Guid labelId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actorUserId)) return Denied();
        if (expiresAt <= timeProvider.GetUtcNow()) return Invalid();

        await using var transaction = await BeginMutationAsync(cancellationToken);
        var label = await LockLabelAsync(labelId, cancellationToken);
        if (!OwnedBy(label, actorUserId)) return Denied();

        var rawToken = GenerateToken();
        var grant = new LabelGrant
        {
            Id = Guid.NewGuid(),
            OwnerUserId = label!.OwnerUserId!,
            LabelId = label.Id,
            TokenHash = Hash(rawToken),
            Permission = LabelGrantPermissions.Viewer,
            ExpiresAt = expiresAt.UtcDateTime,
            Version = 1,
            CreatedByUserId = actorUserId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            UpdatedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        context.LabelGrants.Add(grant);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict();
        }

        logger.LogInformation(
            "Anonymous viewer link created. LabelId={LabelId} GrantId={GrantId} ActorUserId={ActorUserId} ExpiresAt={ExpiresAt}",
            labelId, grant.Id, actorUserId, expiresAt);
        return new ShareTokenMutationResult(
            ShareTokenMutationStatus.Success,
            grant.Id,
            rawToken,
            expiresAt,
            grant.Version);
    }

    public async Task<ShareTokenResolutionDto?> ResolveAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        // Generated tokens are exactly 32 random bytes encoded as unpadded base64url. Reject
        // anything else before hashing or querying, while preserving the same generic denial.
        if (!IsGeneratedTokenFormat(rawToken)) return null;

        var suppliedHash = Hash(rawToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var grant = await context.LabelGrants.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TokenHash == suppliedHash)
            .Select(item => new
            {
                item.LabelId,
                item.OwnerUserId,
                item.Permission,
                item.TokenHash,
                item.ExpiresAt,
                item.RevokedAt,
                LabelOwnerUserId = item.Label == null ? null : item.Label.OwnerUserId
            })
            .SingleOrDefaultAsync(cancellationToken);

        var storedHash = TryDecodeHash(grant?.TokenHash);
        var hashMatches = CryptographicOperations.FixedTimeEquals(
            storedHash ?? InvalidHash,
            Convert.FromHexString(suppliedHash));

        if (!hashMatches ||
            grant is null ||
            grant.Permission != LabelGrantPermissions.Viewer ||
            grant.RevokedAt is not null ||
            grant.ExpiresAt is null ||
            grant.ExpiresAt <= now ||
            grant.LabelOwnerUserId is null ||
            !string.Equals(grant.OwnerUserId, grant.LabelOwnerUserId, StringComparison.Ordinal))
            return null;

        return new ShareTokenResolutionDto(
            grant.LabelId,
            grant.OwnerUserId,
            LabelGrantPermissions.Viewer);
    }

    public async Task<ShareTokenMutationResult> RevokeAsync(
        Guid labelId,
        Guid grantId,
        long version,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actorUserId) || version < 1) return Denied();

        await using var transaction = await BeginMutationAsync(cancellationToken);
        var label = await LockLabelAsync(labelId, cancellationToken);
        if (!OwnedBy(label, actorUserId)) return Denied();

        var grant = await LockGrantAsync(labelId, grantId, cancellationToken);
        if (grant is null ||
            grant.TokenHash is null ||
            grant.GranteeUserId is not null ||
            grant.Permission != LabelGrantPermissions.Viewer ||
            grant.RevokedAt is not null ||
            !string.Equals(grant.OwnerUserId, label!.OwnerUserId, StringComparison.Ordinal))
            return Denied();
        if (grant.Version != version) return Conflict();

        context.Entry(grant).Property(item => item.Version).OriginalValue = version;
        grant.RevokedAt = timeProvider.GetUtcNow().UtcDateTime;
        grant.UpdatedAt = grant.RevokedAt.Value;
        grant.Version++;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict();
        }

        logger.LogInformation(
            "Anonymous viewer link revoked. LabelId={LabelId} GrantId={GrantId} ActorUserId={ActorUserId}",
            labelId, grant.Id, actorUserId);
        return new ShareTokenMutationResult(
            ShareTokenMutationStatus.Success,
            grant.Id,
            Version: grant.Version);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

    private static bool IsGeneratedTokenFormat(string? rawToken)
    {
        if (rawToken is null || rawToken.Length != 43) return false;

        foreach (var character in rawToken)
        {
            var allowed = character is >= 'A' and <= 'Z' or
                >= 'a' and <= 'z' or
                >= '0' and <= '9' or
                '_' or '-';
            if (!allowed) return false;
        }

        return true;
    }

    private static byte[]? TryDecodeHash(string? hash)
    {
        if (hash?.Length != SHA256.HashSizeInBytes * 2) return null;
        try
        {
            return Convert.FromHexString(hash);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool OwnedBy(Label? label, string actorUserId) =>
        label?.OwnerUserId is not null &&
        string.Equals(label.OwnerUserId, actorUserId, StringComparison.Ordinal);

    private async Task<IDbContextTransaction?> BeginMutationAsync(CancellationToken cancellationToken) =>
        context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

    private async Task<Label?> LockLabelAsync(Guid labelId, CancellationToken cancellationToken) =>
        context.Database.IsRelational()
            ? await context.Labels
                .FromSqlInterpolated($"SELECT * FROM labels WHERE id = {labelId} FOR UPDATE")
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(cancellationToken)
            : await context.Labels.IgnoreQueryFilters()
                .SingleOrDefaultAsync(label => label.Id == labelId, cancellationToken);

    private async Task<LabelGrant?> LockGrantAsync(
        Guid labelId,
        Guid grantId,
        CancellationToken cancellationToken) =>
        context.Database.IsRelational()
            ? await context.LabelGrants
                .FromSqlInterpolated($"SELECT * FROM label_grants WHERE label_id = {labelId} AND id = {grantId} FOR UPDATE")
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(cancellationToken)
            : await context.LabelGrants.IgnoreQueryFilters().SingleOrDefaultAsync(
                grant => grant.LabelId == labelId && grant.Id == grantId,
                cancellationToken);

    private static ShareTokenMutationResult Denied() => new(ShareTokenMutationStatus.Denied);
    private static ShareTokenMutationResult Invalid() => new(ShareTokenMutationStatus.Invalid);
    private static ShareTokenMutationResult Conflict() => new(ShareTokenMutationStatus.Conflict);
}
