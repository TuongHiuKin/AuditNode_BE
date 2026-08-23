using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public sealed class ScopedResourcePolicy(AuditDbContext context, IWorkspaceAccessService accessService) : IScopedResourcePolicy
{
    public async Task<IReadOnlySet<Guid>?> GetGrantedFrameIdsAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default)
    {
        var access = await accessService.ResolveAsync(workspaceId, userId, cancellationToken);
        if (access is null) return new HashSet<Guid>();
        if (access.Scope.Mode == WorkspaceScopeModes.All) return null;
        return access.Scope.Mode == WorkspaceScopeModes.Frames ? access.Scope.Frames.Select(x => x.Id).ToHashSet() : new HashSet<Guid>();
    }
    public async Task<IReadOnlySet<Guid>?> GetReadableIdsAsync(Guid workspaceId, string userId, string resourceType, CancellationToken cancellationToken = default)
    {
        var access = await accessService.ResolveAsync(workspaceId, userId, cancellationToken);
        if (access is null) return new HashSet<Guid>();
        if (access.Scope.Mode == WorkspaceScopeModes.All) return null;
        if (access.Scope.Mode == WorkspaceScopeModes.Labels)
        {
            var labels = access.Scope.Labels.Select(x => x.Id).ToList();
            return resourceType == "server"
                ? (await context.ServerLabels.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspaceId && labels.Contains(x.LabelId)).Select(x => x.ServerId).Distinct().ToListAsync(cancellationToken)).ToHashSet()
                : (await context.ApplicationLabels.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspaceId && labels.Contains(x.LabelId)).Select(x => x.ApplicationId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        }
        var roots = access.Scope.Frames.Select(x => x.Id).ToHashSet();
        var nodes = await context.TopologyNodes.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspaceId).Select(x => new ScopedNode(x.Id, x.ParentNodeId, x.ReferenceId, x.NodeType)).ToListAsync(cancellationToken);
        var byId = nodes.ToDictionary(x => x.Id);
        return nodes.Where(x => x.ReferenceId.HasValue && string.Equals(x.NodeType, resourceType, StringComparison.OrdinalIgnoreCase) && IsDescendant(x.Id, roots, byId)).Select(x => x.ReferenceId!.Value).ToHashSet();
    }
    public async Task<bool> CanReadAsync(Guid workspaceId, string userId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default) =>
        await IsAllowedAsync(workspaceId, userId, resourceType, resourceId, false, cancellationToken);

    public async Task<bool> CanWriteAsync(Guid workspaceId, string userId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default) =>
        await IsAllowedAsync(workspaceId, userId, resourceType, resourceId, true, cancellationToken);

    public async Task<bool> CanCreateAsync(Guid workspaceId, string userId, string resourceType, IReadOnlyCollection<LabelDto> labels, CancellationToken cancellationToken = default)
    {
        var access = await accessService.ResolveAsync(workspaceId, userId, cancellationToken);
        if (access is null || access.EffectiveRole == WorkspaceRoles.Viewer) return false;
        if (access.Scope.Mode == WorkspaceScopeModes.All) return true;
        if (access.Scope.Mode == WorkspaceScopeModes.Frames) return false;
        var allowedIds = access.Scope.Labels.Select(x => x.Id).ToList();
        var requested = labels.Select(x => new { x.Key, x.Value }).Distinct().ToList();
        if (requested.Count == 0) return false;
        var allowedLabels = await context.Labels.IgnoreQueryFilters()
            .Where(label => label.WorkspaceId == workspaceId && allowedIds.Contains(label.Id))
            .Select(label => new { label.Key, label.Value }).ToListAsync(cancellationToken);
        return requested.All(item => allowedLabels.Any(label => label.Key == item.Key && label.Value == item.Value));
    }

    private async Task<bool> IsAllowedAsync(Guid workspaceId, string userId, string resourceType, Guid resourceId, bool write, CancellationToken cancellationToken)
    {
        var access = await accessService.ResolveAsync(workspaceId, userId, cancellationToken);
        if (access is null || write && access.EffectiveRole == WorkspaceRoles.Viewer) return false;
        if (access.Scope.Mode == WorkspaceScopeModes.All) return true;
        if (access.Scope.Mode == WorkspaceScopeModes.Labels)
        {
            var allowed = access.Scope.Labels.Select(x => x.Id).ToList();
            return resourceType == "server"
                ? await context.ServerLabels.IgnoreQueryFilters().AnyAsync(x => x.WorkspaceId == workspaceId && x.ServerId == resourceId && allowed.Contains(x.LabelId), cancellationToken)
                : await context.ApplicationLabels.IgnoreQueryFilters().AnyAsync(x => x.WorkspaceId == workspaceId && x.ApplicationId == resourceId && allowed.Contains(x.LabelId), cancellationToken);
        }

        var roots = access.Scope.Frames.Select(x => x.Id).ToHashSet();
        var nodes = await context.TopologyNodes.IgnoreQueryFilters().Where(x => x.WorkspaceId == workspaceId).Select(x => new ScopedNode(x.Id, x.ParentNodeId, x.ReferenceId, x.NodeType)).ToListAsync(cancellationToken);
        var byId = nodes.ToDictionary(x => x.Id);
        return nodes.Where(x => x.ReferenceId == resourceId && string.Equals(x.NodeType, resourceType, StringComparison.OrdinalIgnoreCase))
            .Any(node => IsDescendant(node.Id, roots, byId));
    }

    private static bool IsDescendant(Guid id, HashSet<Guid> roots, IReadOnlyDictionary<Guid, ScopedNode> nodes)
    {
        var visited = new HashSet<Guid>();
        var current = id;
        while (visited.Add(current) && nodes.TryGetValue(current, out var node))
        {
            if (roots.Contains(current)) return true;
            var parent = node.ParentNodeId;
            if (!parent.HasValue) return false;
            current = parent.Value;
        }
        return false;
    }

    private sealed record ScopedNode(Guid Id, Guid? ParentNodeId, Guid? ReferenceId, string NodeType);
}
