using System.Data;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AuditNode.Infrastructure.Services;

public sealed class LabelGrantService(
    AuditDbContext context,
    ICurrentUserService currentUser,
    IIdentityAdminService identities,
    TimeProvider timeProvider,
    ILogger<LabelGrantService> logger) : ILabelGrantService
{
    public async Task<IReadOnlyList<LabelGrantDto>?> ListAsync(
        Guid labelId,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actorUserId)) return null;
        var labelKind = await context.Labels.IgnoreQueryFilters().AsNoTracking()
            .Where(label => label.Id == labelId &&
                            label.OwnerUserId != null &&
                            label.OwnerUserId == actorUserId)
            .Select(label => label.Kind)
            .SingleOrDefaultAsync(cancellationToken);
        if (labelKind is null) return null;

        var grants = await context.LabelGrants.IgnoreQueryFilters().AsNoTracking()
            .Where(grant => grant.LabelId == labelId && grant.GranteeUserId != null)
            .OrderBy(grant => grant.CreatedAt)
            .ToListAsync(cancellationToken);
        return grants.Select(grant => ToDto(grant, labelKind)).ToList();
    }

    public async Task<LabelGrantMutationResult> CreateAsync(
        Guid labelId,
        CreateLabelGrantDto request,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actorUserId)) return Denied();
        if (!ValidPermission(request.Permission) ||
            string.IsNullOrWhiteSpace(request.GranteeUserId) ||
            !ValidExpiry(request.ExpiresAt))
            return Invalid();

        // Authenticate the catalog owner before consulting the identity directory so callers
        // cannot use this service to probe whether an arbitrary account exists or is enabled.
        if (!await IsOwnerAsync(labelId, actorUserId, cancellationToken)) return Denied();

        var identity = await identities.GetUserAsync(request.GranteeUserId, cancellationToken);
        if (identity is null || !identity.Enabled) return Invalid();

        await using var transaction = await BeginMutationAsync(cancellationToken);
        var label = await LockLabelAsync(labelId, cancellationToken);
        if (!OwnedBy(label, actorUserId) || request.GranteeUserId == actorUserId) return Denied();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await LockUnrevokedUserGrantAsync(labelId, request.GranteeUserId, cancellationToken);
        Guid? replacedExpiredGrantId = null;
        if (existing is not null)
        {
            if (!ConsistentUnrevokedUserGrant(label!, existing)) return Denied();
            if (existing.ExpiresAt is null || existing.ExpiresAt > now) return Conflict();

            // The partial unique index covers every unrevoked user grant, including an expired
            // one. Revoke and flush the historical row first, while retaining the label lock and
            // transaction, so the replacement insert remains atomic and index-safe.
            context.Entry(existing).Property(item => item.Version).OriginalValue = existing.Version;
            existing.RevokedAt = now;
            existing.UpdatedAt = now;
            existing.Version++;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return Conflict();
            }
            replacedExpiredGrantId = existing.Id;
        }

        var grant = new LabelGrant
        {
            Id = Guid.NewGuid(),
            OwnerUserId = label!.OwnerUserId!,
            LabelId = label.Id,
            GranteeUserId = request.GranteeUserId,
            Permission = request.Permission,
            ExpiresAt = request.ExpiresAt?.UtcDateTime,
            Version = 1,
            CreatedByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
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
            "Label user grant created. LabelId={LabelId} GrantId={GrantId} ReplacedExpiredGrantId={ReplacedExpiredGrantId} ActorUserId={ActorUserId} GranteeUserId={GranteeUserId} Permission={Permission}",
            labelId, grant.Id, replacedExpiredGrantId, actorUserId, request.GranteeUserId, request.Permission);
        return Success(grant, label.Kind);
    }

    public async Task<LabelGrantMutationResult> UpdateAsync(
        Guid labelId,
        Guid grantId,
        UpdateLabelGrantDto request,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actorUserId)) return Denied();
        if (!ValidPermission(request.Permission) || request.Version < 1 || !ValidExpiry(request.ExpiresAt))
            return Invalid();

        if (!await IsOwnerAsync(labelId, actorUserId, cancellationToken)) return Denied();
        var granteeUserId = await context.LabelGrants.IgnoreQueryFilters().AsNoTracking()
            .Where(grant => grant.Id == grantId &&
                            grant.LabelId == labelId &&
                            grant.OwnerUserId == actorUserId &&
                            grant.GranteeUserId != null &&
                            grant.TokenHash == null &&
                            grant.RevokedAt == null)
            .Select(grant => grant.GranteeUserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (granteeUserId is null) return Denied();

        // Keycloak cannot participate in the PostgreSQL transaction. Validate first, then lock
        // and re-check every database invariant before applying the mutation.
        var identity = await identities.GetUserAsync(granteeUserId, cancellationToken);
        if (identity is null || !identity.Enabled) return Invalid();

        await using var transaction = await BeginMutationAsync(cancellationToken);
        var label = await LockLabelAsync(labelId, cancellationToken);
        if (!OwnedBy(label, actorUserId)) return Denied();

        var grant = await LockGrantAsync(labelId, grantId, cancellationToken);
        if (!ConsistentUnrevokedUserGrant(label!, grant)) return Denied();
        if (grant!.Version != request.Version) return Conflict();
        if (!string.Equals(grant.GranteeUserId, granteeUserId, StringComparison.Ordinal)) return Denied();

        context.Entry(grant).Property(item => item.Version).OriginalValue = request.Version;
        grant.Permission = request.Permission;
        grant.ExpiresAt = request.ExpiresAt?.UtcDateTime;
        grant.Version++;
        grant.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (!await SaveMutationAsync(transaction, cancellationToken)) return Conflict();
        logger.LogInformation(
            "Label user grant updated. LabelId={LabelId} GrantId={GrantId} ActorUserId={ActorUserId} Permission={Permission}",
            labelId, grant.Id, actorUserId, grant.Permission);
        return Success(grant, label!.Kind);
    }

    public async Task<LabelGrantMutationResult> RevokeAsync(
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
        if (!ConsistentUnrevokedUserGrant(label!, grant)) return Denied();
        if (grant!.Version != version) return Conflict();

        context.Entry(grant).Property(item => item.Version).OriginalValue = version;
        grant.RevokedAt = timeProvider.GetUtcNow().UtcDateTime;
        grant.UpdatedAt = grant.RevokedAt.Value;
        grant.Version++;

        if (!await SaveMutationAsync(transaction, cancellationToken)) return Conflict();
        logger.LogInformation(
            "Label user grant revoked. LabelId={LabelId} GrantId={GrantId} ActorUserId={ActorUserId}",
            labelId, grant.Id, actorUserId);
        return Success(grant, label!.Kind);
    }

    private bool ValidExpiry(DateTimeOffset? expiresAt) =>
        expiresAt is null || expiresAt > timeProvider.GetUtcNow();

    private static bool ValidPermission(string permission) =>
        permission is LabelGrantPermissions.Viewer or LabelGrantPermissions.Editor;

    private static bool OwnedBy(Label? label, string actorUserId) =>
        label?.OwnerUserId is not null &&
        string.Equals(label.OwnerUserId, actorUserId, StringComparison.Ordinal);

    private static bool ConsistentUnrevokedUserGrant(Label label, LabelGrant? grant) =>
        grant is not null &&
        grant.GranteeUserId is not null &&
        grant.TokenHash is null &&
        grant.RevokedAt is null &&
        string.Equals(grant.OwnerUserId, label.OwnerUserId, StringComparison.Ordinal);

    private Task<bool> IsOwnerAsync(Guid labelId, string? userId, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(userId)
            ? Task.FromResult(false)
            : context.Labels.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                label => label.Id == labelId &&
                         label.OwnerUserId != null &&
                         label.OwnerUserId == userId,
                cancellationToken);

    private async Task<bool> SaveMutationAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

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

    private async Task<LabelGrant?> LockUnrevokedUserGrantAsync(
        Guid labelId,
        string granteeUserId,
        CancellationToken cancellationToken) =>
        context.Database.IsRelational()
            ? await context.LabelGrants
                .FromSqlInterpolated($"SELECT * FROM label_grants WHERE label_id = {labelId} AND grantee_user_id = {granteeUserId} AND revoked_at IS NULL FOR UPDATE")
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(cancellationToken)
            : await context.LabelGrants.IgnoreQueryFilters().SingleOrDefaultAsync(
                grant => grant.LabelId == labelId &&
                         grant.GranteeUserId == granteeUserId &&
                         grant.RevokedAt == null,
                cancellationToken);

    private static LabelGrantDto ToDto(LabelGrant grant, string labelKind)
    {
        var sharesAllOwnerResources = labelKind == LabelKinds.Owner;
        return new(
        grant.Id,
        grant.LabelId,
        grant.GranteeUserId!,
        grant.Permission,
        ToOffset(grant.ExpiresAt),
        ToOffset(grant.RevokedAt),
        grant.Version,
        sharesAllOwnerResources,
        sharesAllOwnerResources ? LabelShareWarningCodes.OwnerLabelSharesAllOwnerResources : null);
    }

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
            : null;

    private static LabelGrantMutationResult Success(LabelGrant grant, string labelKind) =>
        new(LabelGrantMutationStatus.Success, ToDto(grant, labelKind));

    private static LabelGrantMutationResult Denied() => new(LabelGrantMutationStatus.Denied);
    private static LabelGrantMutationResult Invalid() => new(LabelGrantMutationStatus.Invalid);
    private static LabelGrantMutationResult Conflict() => new(LabelGrantMutationStatus.Conflict);
}
