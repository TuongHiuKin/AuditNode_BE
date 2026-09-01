using System.Data;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public sealed class OwnerGraphAccessService(
    AuditDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IOwnerGraphAccessService
{
    public async Task<OwnerGraphAccessDto?> ResolveAsync(
        string ownerUserId,
        bool lockForWrite = false,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actorUserId) || string.IsNullOrWhiteSpace(ownerUserId)) return null;

        var allServers = await context.Servers.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId)
            .Select(item => item.Id)
            .ToHashSetAsync(cancellationToken);
        var allApplications = await context.Applications.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId)
            .Select(item => item.Id)
            .ToHashSetAsync(cancellationToken);

        if (string.Equals(actorUserId, ownerUserId, StringComparison.Ordinal))
            return new(ownerUserId, LabelEffectivePermission.Owner,
                allServers, allApplications, allServers, allApplications);

        List<LabelGrant> grants;
        if (lockForWrite && context.Database.IsRelational())
        {
            if (context.Database.CurrentTransaction is null)
                throw new InvalidOperationException("Graph write authorization requires an active transaction.");
            grants = await context.LabelGrants.FromSqlInterpolated(
                    $"SELECT * FROM label_grants WHERE owner_user_id = {ownerUserId} AND grantee_user_id = {actorUserId} AND revoked_at IS NULL ORDER BY id FOR UPDATE")
                .IgnoreQueryFilters().ToListAsync(cancellationToken);
        }
        else
        {
            grants = await context.LabelGrants.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.OwnerUserId == ownerUserId && item.GranteeUserId == actorUserId && item.RevokedAt == null)
                .OrderBy(item => item.Id).ToListAsync(cancellationToken);
        }

        // Capture time only after acquiring grant row locks. A command that waited behind an
        // update/revoke must authorize against the post-wait expiry state, not a stale timestamp.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        grants = grants.Where(item => item.TokenHash is null &&
                                      (item.ExpiresAt is null || item.ExpiresAt > now) &&
                                      item.Permission is LabelGrantPermissions.Viewer or LabelGrantPermissions.Editor)
            .ToList();
        if (grants.Count == 0) return null;

        var grantLabelIds = grants.Select(item => item.LabelId).Distinct().ToArray();
        var labels = await context.Labels.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && grantLabelIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Kind })
            .ToListAsync(cancellationToken);
        var validLabelIds = labels.Select(item => item.Id).ToHashSet();
        grants = grants.Where(item => validLabelIds.Contains(item.LabelId)).ToList();
        if (grants.Count == 0) return null;

        var readableOwner = grants.Any(grant => labels.Any(label => label.Id == grant.LabelId && label.Kind == LabelKinds.Owner));
        var editableOwner = grants.Any(grant => grant.Permission == LabelGrantPermissions.Editor &&
                                                labels.Any(label => label.Id == grant.LabelId && label.Kind == LabelKinds.Owner));
        var readableBusiness = grants.Where(grant => labels.Any(label => label.Id == grant.LabelId && label.Kind == LabelKinds.Business))
            .Select(grant => grant.LabelId).Distinct().ToArray();
        var editableBusiness = grants.Where(grant => grant.Permission == LabelGrantPermissions.Editor &&
                                                      labels.Any(label => label.Id == grant.LabelId && label.Kind == LabelKinds.Business))
            .Select(grant => grant.LabelId).Distinct().ToArray();

        if (lockForWrite && context.Database.IsRelational() && grantLabelIds.Length > 0)
        {
            _ = await context.ServerLabels.FromSqlInterpolated(
                    $"SELECT * FROM server_labels WHERE owner_user_id = {ownerUserId} AND label_id = ANY({grantLabelIds}) ORDER BY server_id, label_id FOR SHARE")
                .IgnoreQueryFilters().ToListAsync(cancellationToken);
            _ = await context.ApplicationLabels.FromSqlInterpolated(
                    $"SELECT * FROM application_labels WHERE owner_user_id = {ownerUserId} AND label_id = ANY({grantLabelIds}) ORDER BY application_id, label_id FOR SHARE")
                .IgnoreQueryFilters().ToListAsync(cancellationToken);
        }

        var readableServers = readableOwner ? allServers : await context.ServerLabels.IgnoreQueryFilters().AsNoTracking()
            .Where(link => link.OwnerUserId == ownerUserId && readableBusiness.Contains(link.LabelId))
            .Select(link => link.ServerId).Distinct().ToHashSetAsync(cancellationToken);
        var readableApplications = readableOwner ? allApplications : await context.ApplicationLabels.IgnoreQueryFilters().AsNoTracking()
            .Where(link => link.OwnerUserId == ownerUserId && readableBusiness.Contains(link.LabelId))
            .Select(link => link.ApplicationId).Distinct().ToHashSetAsync(cancellationToken);
        var editableServers = editableOwner ? allServers : await context.ServerLabels.IgnoreQueryFilters().AsNoTracking()
            .Where(link => link.OwnerUserId == ownerUserId && editableBusiness.Contains(link.LabelId))
            .Select(link => link.ServerId).Distinct().ToHashSetAsync(cancellationToken);
        var editableApplications = editableOwner ? allApplications : await context.ApplicationLabels.IgnoreQueryFilters().AsNoTracking()
            .Where(link => link.OwnerUserId == ownerUserId && editableBusiness.Contains(link.LabelId))
            .Select(link => link.ApplicationId).Distinct().ToHashSetAsync(cancellationToken);
        var effective = editableServers.Count > 0 || editableApplications.Count > 0 || editableOwner
            ? LabelEffectivePermission.Editor
            : LabelEffectivePermission.Viewer;
        return new(ownerUserId, effective, readableServers, readableApplications, editableServers, editableApplications);
    }
}
