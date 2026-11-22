using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public sealed class LabelAccessService(
    AuditDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : ILabelAccessService
{
    public Task<IReadOnlyList<Guid>> GetReadableServerIdsAsync(
        CatalogView view,
        CancellationToken cancellationToken = default) =>
        GetReadableIdsAsync(view, isServer: true, cancellationToken);

    public Task<IReadOnlyList<Guid>> GetReadableApplicationIdsAsync(
        CatalogView view,
        CancellationToken cancellationToken = default) =>
        GetReadableIdsAsync(view, isServer: false, cancellationToken);

    public async Task<ResourceLabelAccessDto?> GetServerAccessAsync(
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        var resource = await context.Servers.IgnoreQueryFilters().AsNoTracking()
            .Where(server => server.Id == serverId)
            .Select(server => new ResourceOwner(server.Id, server.OwnerUserId))
            .SingleOrDefaultAsync(cancellationToken);

        return await ResolveResourceAsync(resource, isServer: true, cancellationToken);
    }

    public async Task<ResourceLabelAccessDto?> GetApplicationAccessAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var resource = await context.Applications.IgnoreQueryFilters().AsNoTracking()
            .Where(application => application.Id == applicationId)
            .Select(application => new ResourceOwner(application.Id, application.OwnerUserId))
            .SingleOrDefaultAsync(cancellationToken);

        return await ResolveResourceAsync(resource, isServer: false, cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> GetReadableIdsAsync(
        CatalogView view,
        bool isServer,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId)) return [];

        if (view == CatalogView.Mine)
        {
            return isServer
                ? await context.Servers.IgnoreQueryFilters().AsNoTracking()
                    .Where(resource => resource.OwnerUserId != null && resource.OwnerUserId == userId)
                    .Select(resource => resource.Id).ToListAsync(cancellationToken)
                : await context.Applications.IgnoreQueryFilters().AsNoTracking()
                    .Where(resource => resource.OwnerUserId != null && resource.OwnerUserId == userId)
                    .Select(resource => resource.Id).ToListAsync(cancellationToken);
        }

        if (view != CatalogView.Shared) throw new ArgumentOutOfRangeException(nameof(view));

        var scopes = await ActiveUserGrants(userId)
            .Select(grant => new GrantScope(
                grant.LabelId,
                grant.Label!.OwnerUserId,
                grant.Label.Kind))
            .ToListAsync(cancellationToken);
        var ownerScopes = scopes
            .Where(scope => scope.Kind == LabelKinds.Owner && scope.OwnerUserId is not null)
            .Select(scope => scope.OwnerUserId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var businessLabelIds = scopes
            .Where(scope => scope.Kind == LabelKinds.Business)
            .Select(scope => scope.LabelId)
            .Distinct()
            .ToList();

        if (isServer)
        {
            return await context.Servers.IgnoreQueryFilters().AsNoTracking()
                .Where(resource => resource.OwnerUserId != null && resource.OwnerUserId != userId &&
                    (ownerScopes.Contains(resource.OwnerUserId) ||
                     context.ServerLabels.IgnoreQueryFilters().Any(link =>
                         link.ServerId == resource.Id &&
                         link.OwnerUserId == resource.OwnerUserId &&
                         businessLabelIds.Contains(link.LabelId))))
                .Select(resource => resource.Id)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        return await context.Applications.IgnoreQueryFilters().AsNoTracking()
            .Where(resource => resource.OwnerUserId != null && resource.OwnerUserId != userId &&
                (ownerScopes.Contains(resource.OwnerUserId) ||
                 context.ApplicationLabels.IgnoreQueryFilters().Any(link =>
                     link.ApplicationId == resource.Id &&
                     link.OwnerUserId == resource.OwnerUserId &&
                     businessLabelIds.Contains(link.LabelId))))
            .Select(resource => resource.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task<ResourceLabelAccessDto?> ResolveResourceAsync(
        ResourceOwner? resource,
        bool isServer,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (resource?.OwnerUserId is null || string.IsNullOrWhiteSpace(userId)) return null;

        if (string.Equals(resource.OwnerUserId, userId, StringComparison.Ordinal))
        {
            return new ResourceLabelAccessDto(
                resource.Id,
                resource.OwnerUserId,
                LabelEffectivePermission.Owner,
                [],
                Capabilities(LabelEffectivePermission.Owner));
        }

        var businessLabelIds = isServer
            ? await context.ServerLabels.IgnoreQueryFilters().AsNoTracking()
                .Where(link => link.ServerId == resource.Id && link.OwnerUserId == resource.OwnerUserId)
                .Select(link => link.LabelId).Distinct().ToListAsync(cancellationToken)
            : await context.ApplicationLabels.IgnoreQueryFilters().AsNoTracking()
                .Where(link => link.ApplicationId == resource.Id && link.OwnerUserId == resource.OwnerUserId)
                .Select(link => link.LabelId).Distinct().ToListAsync(cancellationToken);

        var grants = await ActiveUserGrants(userId)
            .Where(grant => grant.OwnerUserId == resource.OwnerUserId &&
                (grant.Label!.Kind == LabelKinds.Owner || businessLabelIds.Contains(grant.LabelId)))
            .Select(grant => new { grant.LabelId, grant.Permission })
            .ToListAsync(cancellationToken);
        if (grants.Count == 0) return null;

        var permission = grants.Any(grant => grant.Permission == LabelGrantPermissions.Editor)
            ? LabelEffectivePermission.Editor
            : LabelEffectivePermission.Viewer;
        return new ResourceLabelAccessDto(
            resource.Id,
            resource.OwnerUserId,
            permission,
            grants.Select(grant => grant.LabelId).Distinct().ToList(),
            Capabilities(permission));
    }

    private IQueryable<LabelGrant> ActiveUserGrants(string userId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return context.LabelGrants.IgnoreQueryFilters().AsNoTracking()
            .Where(grant =>
                grant.GranteeUserId == userId &&
                grant.TokenHash == null &&
                grant.RevokedAt == null &&
                (grant.ExpiresAt == null || grant.ExpiresAt > now) &&
                grant.OwnerUserId != string.Empty &&
                grant.Label != null &&
                grant.Label.OwnerUserId != null &&
                grant.Label.OwnerUserId == grant.OwnerUserId);
    }

    private static LabelAccessCapabilities Capabilities(LabelEffectivePermission permission) => permission switch
    {
        LabelEffectivePermission.Owner => new(true, true, true, true, true, false, true),
        LabelEffectivePermission.Editor => new(true, true, false, false, false, false, false),
        _ => new(true, false, false, false, false, false, false)
    };

    private sealed record ResourceOwner(Guid Id, string? OwnerUserId);
    private sealed record GrantScope(Guid LabelId, string? OwnerUserId, string Kind);
}
