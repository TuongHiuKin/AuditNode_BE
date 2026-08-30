using AuditNode.Domain.Entities;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AuditNode.Infrastructure.Repositories;

public class TopologyRepository : ITopologyRepository
{
    private readonly AuditDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantProvider _tenant;
    private readonly IOwnerGraphAccessService _graphAccess;

    public TopologyRepository(
        AuditDbContext context,
        ICurrentUserService currentUser,
        ITenantProvider tenant,
        IOwnerGraphAccessService graphAccess)
    {
        _context = context;
        _currentUser = currentUser;
        _tenant = tenant;
        _graphAccess = graphAccess;
    }

    public async Task<TopologyStateDto> GetTopologyStateAsync(string? ownerUserId = null)
    {
        var scope = await ResolveReadScopeAsync(ownerUserId);
        if (scope is null || string.IsNullOrWhiteSpace(_currentUser.UserId)) return new();
        var transitionalWorkspaceIds = await TransitionalWorkspaceIdsAsync(scope.OwnerUserId);
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead)
            : null;
        var topologyVersion = await _context.OwnerCatalogStates.AsNoTracking()
            .Where(state => state.OwnerUserId == scope.OwnerUserId)
            .Select(state => state.TopologyVersion)
            .SingleOrDefaultAsync();
        var allowedServers = scope.ReadableServerIds;
        var allowedApps = scope.ReadableApplicationIds;
        var allowedMappings = (await _context.PortMappings.IgnoreQueryFilters().Where(x => x.OwnerUserId == scope.OwnerUserId && allowedApps.Contains(x.AppId) && allowedServers.Contains(x.ServerId)).Select(x => x.Id).ToListAsync()).ToHashSet();
        var rawNodes = await _context.TopologyNodes.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(node => node.OwnerUserId == scope.OwnerUserId ||
                           node.OwnerUserId == null && transitionalWorkspaceIds.Contains(node.WorkspaceId))
            .OrderBy(node => node.Id)
            .Select(node => new TopologyNodeDto
            {
                Id = node.Id,
                NodeType = node.NodeType,
                Label = node.Label,
                X = node.X,
                Y = node.Y,
                Width = node.Width,
                Height = node.Height,
                ParentNodeId = node.ParentNodeId,
                ReferenceId = node.ReferenceId
            }).ToListAsync();
        var rawById = rawNodes.ToDictionary(x => x.Id);
        var grantedNodeIds = scope.EffectivePermission == LabelEffectivePermission.Owner
            ? rawNodes.Select(node => node.Id).ToHashSet()
            : rawNodes.Where(node => node.NodeType.Equals("server", StringComparison.OrdinalIgnoreCase)
                ? node.ReferenceId.HasValue && allowedServers.Contains(node.ReferenceId.Value)
                : node.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase)
                    ? node.ReferenceId.HasValue && allowedMappings.Contains(node.ReferenceId.Value)
                    : false).Select(x => x.Id).ToHashSet();
        var layoutContextIds = grantedNodeIds.ToHashSet();
        foreach (var id in grantedNodeIds)
        {
            var cursor = rawById[id].ParentNodeId;
            while (cursor.HasValue && layoutContextIds.Add(cursor.Value) && rawById.TryGetValue(cursor.Value, out var parent))
            {
                cursor = parent.ParentNodeId;
            }
        }
        var nodes = rawNodes.Where(x => layoutContextIds.Contains(x.Id)).ToList();
        foreach (var node in nodes.Where(x => x.ParentNodeId.HasValue && !layoutContextIds.Contains(x.ParentNodeId.Value))) node.ParentNodeId = null;
        var opaqueSalt = $"{_currentUser.UserId}:{scope.OwnerUserId}";
        if (scope.EffectivePermission != LabelEffectivePermission.Owner)
        {
            var restrictedContainers = layoutContextIds.Except(grantedNodeIds)
                .ToDictionary(id => id, id => OpaqueId(opaqueSalt, id));
            foreach (var node in nodes)
            {
                if (node.ParentNodeId.HasValue && restrictedContainers.TryGetValue(node.ParentNodeId.Value, out var opaqueParent))
                    node.ParentNodeId = opaqueParent;
                if (!restrictedContainers.TryGetValue(node.Id, out var opaqueId)) continue;
                node.Id = opaqueId;
                node.Label = "Restricted Container";
                node.ReferenceId = null;
                node.X = 0;
                node.Y = 0;
                node.Width = null;
                node.Height = null;
                node.IsRestricted = true;
            }
        }
        var rawEdges = await _context.TopologyEdges.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(edge => edge.OwnerUserId == scope.OwnerUserId ||
                           edge.OwnerUserId == null && transitionalWorkspaceIds.Contains(edge.WorkspaceId))
            .OrderBy(edge => edge.Id)
            .Select(edge => new TopologyEdgeDto
            {
                Id = edge.Id,
                SourceNodeId = edge.SourceNodeId,
                TargetNodeId = edge.TargetNodeId,
                SourceHandle = edge.SourceHandle,
                TargetHandle = edge.TargetHandle,
                EdgeType = edge.EdgeType,
                Label = edge.Label,
                ReferenceId = edge.ReferenceId
            }).ToListAsync();
        var edges = new List<TopologyEdgeDto>();
        foreach (var edge in rawEdges.Where(x => grantedNodeIds.Contains(x.SourceNodeId) || grantedNodeIds.Contains(x.TargetNodeId)))
        {
            var sourceVisible = grantedNodeIds.Contains(edge.SourceNodeId);
            var targetVisible = grantedNodeIds.Contains(edge.TargetNodeId);
            if (!sourceVisible)
            {
                var opaque = OpaqueId(opaqueSalt, edge.SourceNodeId);
                nodes.Add(Restricted(opaque));
                edge.SourceNodeId = opaque;
                edge.Id = OpaqueId(opaqueSalt, edge.Id);
                edge.ReferenceId = null;
            }
            if (!targetVisible)
            {
                var opaque = OpaqueId(opaqueSalt, edge.TargetNodeId);
                nodes.Add(Restricted(opaque));
                edge.TargetNodeId = opaque;
                edge.Id = OpaqueId(opaqueSalt, edge.Id);
                edge.ReferenceId = null;
            }
            if (!sourceVisible || !targetVisible)
            {
                edge.SourceHandle = string.Empty;
                edge.TargetHandle = string.Empty;
                edge.EdgeType = "restricted";
                edge.Label = string.Empty;
                edge.ReferenceId = null;
            }
            edges.Add(edge);
        }
        nodes = nodes.DistinctBy(x => x.Id).ToList();
        var result = new TopologyStateDto { Version = topologyVersion, Nodes = nodes, Edges = edges };
        if (transaction is not null) await transaction.CommitAsync();
        return result;
    }

    private static TopologyNodeDto Restricted(Guid id) => new() { Id = id, NodeType = "restricted", Label = "External Resource (Restricted)", IsRestricted = true };
    private static Guid OpaqueId(string userId, Guid value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{value:N}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private async Task<Guid?> ResolveTransitionalWorkspaceAsync(string ownerUserId)
    {
        if (_tenant.WorkspaceId.HasValue && _tenant.WorkspaceId != Guid.Empty &&
            await _context.Workspaces.IgnoreQueryFilters().AnyAsync(item => item.Id == _tenant.WorkspaceId && item.OwnerUserId == ownerUserId))
            return _tenant.WorkspaceId;
        return await _context.Workspaces.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId)
            .OrderByDescending(item => item.IsPersonal)
            .ThenBy(item => item.Id)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync();
    }

    private async Task<OwnerCatalogState> LockOwnerStateAsync(string ownerUserId)
    {
        if (_context.Database.IsRelational())
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO owner_catalog_states (owner_user_id, topology_version, updated_at) VALUES ({ownerUserId}, 0, CURRENT_TIMESTAMP) ON CONFLICT (owner_user_id) DO NOTHING");
            return await _context.OwnerCatalogStates.FromSqlInterpolated(
                    $"SELECT * FROM owner_catalog_states WHERE owner_user_id = {ownerUserId} FOR UPDATE")
                .SingleAsync();
        }
        var existing = await _context.OwnerCatalogStates.SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId);
        if (existing is not null) return existing;
        existing = new OwnerCatalogState { OwnerUserId = ownerUserId };
        _context.OwnerCatalogStates.Add(existing);
        return existing;
    }

    public async Task<TopologyStateStatus> SaveTopologyStateAsync(SaveTopologyStateDto state)
    {
        if (state?.Version is null || state.Nodes is null || state.Edges is null || state.Dependencies is null ||
            state.Nodes.Any(item => item is null) || state.Edges.Any(item => item is null) ||
            state.Dependencies.Any(item => item is null))
            return TopologyStateStatus.InvalidRequest;
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : null;
        try
        {
            if (string.IsNullOrWhiteSpace(_currentUser.UserId))
                return TopologyStateStatus.Forbidden;
            var ownerUserId = _currentUser.UserId!;
            var workspaceId = await ResolveTransitionalWorkspaceAsync(ownerUserId);
            if (!workspaceId.HasValue) return TopologyStateStatus.Forbidden;
            var ownerState = await LockOwnerStateAsync(ownerUserId);
            if (ownerState.TopologyVersion != state.Version.Value)
                return TopologyStateStatus.Conflict;

            var validation = await ValidateStateAsync(state, ownerUserId, workspaceId.Value);
            if (validation != TopologyStateStatus.Success)
                return validation;

            var existingNodes = await _context.TopologyNodes.IgnoreQueryFilters()
                .Where(n => n.WorkspaceId == workspaceId.Value &&
                            (n.OwnerUserId == ownerUserId || n.OwnerUserId == null))
                .ToDictionaryAsync(n => n.Id);
            foreach (var nodeDto in state.Nodes)
            {
                if (existingNodes.TryGetValue(nodeDto.Id, out var existingNode))
                {
                    existingNode.NodeType = nodeDto.NodeType;
                    existingNode.Label = nodeDto.Label;
                    existingNode.X = nodeDto.X;
                    existingNode.Y = nodeDto.Y;
                    existingNode.Width = nodeDto.Width;
                    existingNode.Height = nodeDto.Height;
                    existingNode.ParentNodeId = nodeDto.ParentNodeId;
                    existingNode.ReferenceId = nodeDto.ReferenceId;
                    _context.TopologyNodes.Update(existingNode);
                }
                else
                {
                    var newNode = new TopologyNode
                    {
                        Id = nodeDto.Id,
                        WorkspaceId = workspaceId.Value,
                        OwnerUserId = ownerUserId,
                        NodeType = nodeDto.NodeType,
                        Label = nodeDto.Label,
                        X = nodeDto.X,
                        Y = nodeDto.Y,
                        Width = nodeDto.Width,
                        Height = nodeDto.Height,
                        ParentNodeId = nodeDto.ParentNodeId,
                        ReferenceId = nodeDto.ReferenceId
                    };
                    _context.TopologyNodes.Add(newNode);
                }
            }

            var incomingIds = state.Nodes.Select(n => n.Id).ToHashSet();
            var nodesToDelete = existingNodes.Values.Where(n => !incomingIds.Contains(n.Id));

            var existingEdges = await _context.TopologyEdges.IgnoreQueryFilters()
                .Where(edge => edge.WorkspaceId == workspaceId.Value &&
                               (edge.OwnerUserId == ownerUserId || edge.OwnerUserId == null))
                .ToDictionaryAsync(edge => edge.Id);
            var persistedEdges = new Dictionary<Guid, TopologyEdge>();
            foreach (var edgeDto in state.Edges)
            {
                if (!existingEdges.TryGetValue(edgeDto.Id, out var edge))
                {
                    edge = new TopologyEdge { Id = edgeDto.Id, WorkspaceId = workspaceId.Value, OwnerUserId = ownerUserId };
                    _context.TopologyEdges.Add(edge);
                }

                edge.SourceNodeId = edgeDto.SourceNodeId;
                edge.TargetNodeId = edgeDto.TargetNodeId;
                edge.SourceHandle = edgeDto.SourceHandle;
                edge.TargetHandle = edgeDto.TargetHandle;
                edge.EdgeType = edgeDto.EdgeType;
                edge.Label = edgeDto.Label;
                edge.ReferenceId = edgeDto.ReferenceId;
                persistedEdges.Add(edge.Id, edge);
            }

            var incomingEdgeIds = state.Edges.Select(edge => edge.Id).ToHashSet();
            _context.TopologyEdges.RemoveRange(existingEdges.Values.Where(edge => !incomingEdgeIds.Contains(edge.Id)));
            _context.TopologyNodes.RemoveRange(nodesToDelete);

            var dependencyValidation = await ValidateAndApplyDependenciesAsync(state, persistedEdges, ownerUserId, workspaceId.Value);
            if (dependencyValidation != TopologyStateStatus.Success)
                return dependencyValidation;

            ownerState.TopologyVersion++;
            ownerState.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            if (transaction is not null) await transaction.CommitAsync();
            return TopologyStateStatus.Success;
        }
        catch (Exception)
        {
            if (transaction is not null) await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<TopologyStateStatus> ValidateAndApplyDependenciesAsync(
        SaveTopologyStateDto state,
        IReadOnlyDictionary<Guid, TopologyEdge> persistedEdges,
        string ownerUserId,
        Guid workspaceId)
    {
        var payload = state.Dependencies;
        if (payload is null || payload.Any(item =>
                item.SourceAppId == Guid.Empty || item.DestAppId == Guid.Empty ||
                item.DestinationPortMappingId == Guid.Empty || item.SourceAppId == item.DestAppId))
            return TopologyStateStatus.InvalidDependency;
        var keys = payload.Select(DependencyKey).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            return TopologyStateStatus.InvalidDependency;

        var appIds = payload.SelectMany(item => new[] { item.SourceAppId, item.DestAppId }).Distinct().ToArray();
        var validAppCount = await _context.Applications.IgnoreQueryFilters()
            .CountAsync(item => item.WorkspaceId == workspaceId &&
                                (item.OwnerUserId == ownerUserId || item.OwnerUserId == null) &&
                                appIds.Contains(item.Id));
        if (validAppCount != appIds.Length) return TopologyStateStatus.InvalidDependency;
        var destinationPortIds = payload.Select(item => item.DestinationPortMappingId).Distinct().ToArray();
        var destinationPorts = await _context.PortMappings.IgnoreQueryFilters()
            .Where(item => item.WorkspaceId == workspaceId &&
                           (item.OwnerUserId == ownerUserId || item.OwnerUserId == null) &&
                           destinationPortIds.Contains(item.Id))
            .Select(item => new { item.Id, item.AppId })
            .ToDictionaryAsync(item => item.Id, item => item.AppId);
        if (destinationPorts.Count != destinationPortIds.Length ||
            payload.Any(item => destinationPorts[item.DestinationPortMappingId] != item.DestAppId))
            return TopologyStateStatus.InvalidDependency;

        var existing = await _context.AppDependencies.IgnoreQueryFilters()
            .Where(item => item.WorkspaceId == workspaceId &&
                           (item.OwnerUserId == ownerUserId || item.OwnerUserId == null)).ToListAsync();
        var desiredKeys = keys.ToHashSet(StringComparer.Ordinal);
        var referencedIds = state.Edges!.Where(edge => edge.ReferenceId.HasValue)
            .Select(edge => edge.ReferenceId!.Value).ToHashSet();
        if (existing.Where(item => referencedIds.Contains(item.Id)).Any(item => !desiredKeys.Contains(DependencyKey(item))))
            return TopologyStateStatus.InvalidDependency;
        var canonicalExisting = existing.GroupBy(DependencyKey).ToDictionary(
            group => group.Key,
            group => group.OrderByDescending(item => referencedIds.Contains(item.Id)).First());
        _context.AppDependencies.RemoveRange(existing.Where(item =>
            !desiredKeys.Contains(DependencyKey(item)) || canonicalExisting[DependencyKey(item)].Id != item.Id));
        var additions = payload
            .Where(item => !canonicalExisting.ContainsKey(DependencyKey(item)))
            .Select(item => new AppDependency
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                OwnerUserId = ownerUserId,
                SourceAppId = item.SourceAppId,
                DestAppId = item.DestAppId,
                DestPortId = item.DestinationPortMappingId,
                ConnectionType = "Manual",
                CreatedAt = DateTime.UtcNow
            }).ToList();
        await _context.AppDependencies.AddRangeAsync(additions);

        var canonicalByKey = canonicalExisting.Values.Concat(additions)
            .ToDictionary(DependencyKey, StringComparer.Ordinal);
        var nodeById = state.Nodes!.ToDictionary(item => item.Id);
        var deploymentIds = nodeById.Values
            .Where(item => item.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) && item.ReferenceId.HasValue)
            .Select(item => item.ReferenceId!.Value).Distinct().ToArray();
        var deploymentApps = await _context.PortMappings.IgnoreQueryFilters()
            .Where(item => item.WorkspaceId == workspaceId &&
                           (item.OwnerUserId == ownerUserId || item.OwnerUserId == null) &&
                           deploymentIds.Contains(item.Id))
            .Select(item => new { item.Id, item.AppId })
            .ToDictionaryAsync(item => item.Id, item => item.AppId);
        var assignedDependencyIds = new HashSet<Guid>();
        foreach (var edge in state.Edges!)
        {
            var source = nodeById[edge.SourceNodeId];
            var target = nodeById[edge.TargetNodeId];
            if (!source.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) ||
                !target.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!source.ReferenceId.HasValue || !target.ReferenceId.HasValue ||
                !deploymentApps.TryGetValue(source.ReferenceId.Value, out var sourceAppId) ||
                !deploymentApps.TryGetValue(target.ReferenceId.Value, out var targetAppId))
                return TopologyStateStatus.InvalidDependency;
            var key = $"{sourceAppId:N}|{targetAppId:N}|{target.ReferenceId.Value:N}";
            if (!canonicalByKey.TryGetValue(key, out var dependency))
                return TopologyStateStatus.InvalidDependency;
            if (!assignedDependencyIds.Add(dependency.Id))
                return TopologyStateStatus.InvalidReference;
            persistedEdges[edge.Id].ReferenceId = dependency.Id;
        }
        return TopologyStateStatus.Success;
    }

    private static string DependencyKey(DependencyItemDto item) =>
        $"{item.SourceAppId:N}|{item.DestAppId:N}|{item.DestinationPortMappingId:N}";

    private static string DependencyKey(AppDependency item) =>
        $"{item.SourceAppId:N}|{item.DestAppId:N}|{item.DestPortId:N}";

    private async Task<TopologyStateStatus> ValidateStateAsync(SaveTopologyStateDto? state, string ownerUserId, Guid workspaceId)
    {
        if (state?.Nodes is null || state.Edges is null)
            return TopologyStateStatus.InvalidRequest;

        if (state.Nodes.Any(node => node.Id == Guid.Empty) ||
            state.Edges.Any(edge => edge.Id == Guid.Empty) ||
            state.Nodes.Select(node => node.Id).Distinct().Count() != state.Nodes.Count ||
            state.Edges.Select(edge => edge.Id).Distinct().Count() != state.Edges.Count ||
            state.Nodes.Select(node => node.Id).Intersect(state.Edges.Select(edge => edge.Id)).Any())
            return TopologyStateStatus.DuplicateId;

        var incomingNodeIds = state.Nodes.Select(item => item.Id).ToArray();
        var incomingEdgeIds = state.Edges.Select(item => item.Id).ToArray();
        var foreignNodeIdExists = await _context.TopologyNodes.IgnoreQueryFilters().AsNoTracking().AnyAsync(item =>
            incomingNodeIds.Contains(item.Id) &&
            !(item.WorkspaceId == workspaceId && (item.OwnerUserId == ownerUserId || item.OwnerUserId == null)));
        var foreignEdgeIdExists = await _context.TopologyEdges.IgnoreQueryFilters().AsNoTracking().AnyAsync(item =>
            incomingEdgeIds.Contains(item.Id) &&
            !(item.WorkspaceId == workspaceId && (item.OwnerUserId == ownerUserId || item.OwnerUserId == null)));
        if (foreignNodeIdExists || foreignEdgeIdExists)
            return TopologyStateStatus.InvalidReference;

        var selectedExistingEdgeIds = await _context.TopologyEdges.IgnoreQueryFilters().AsNoTracking()
            .Where(item => incomingEdgeIds.Contains(item.Id) && item.WorkspaceId == workspaceId &&
                           (item.OwnerUserId == ownerUserId || item.OwnerUserId == null))
            .Select(item => item.Id)
            .ToListAsync();
        var nodeCrossTableCollision = await _context.TopologyEdges.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(item => incomingNodeIds.Contains(item.Id)) ||
            await _context.AppDependencies.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => incomingNodeIds.Contains(item.Id));
        var edgeNodeCollision = await _context.TopologyNodes.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(item => incomingEdgeIds.Contains(item.Id));
        var newEdgeDependencyCollision = await _context.AppDependencies.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(item => incomingEdgeIds.Contains(item.Id) && !selectedExistingEdgeIds.Contains(item.Id));
        if (nodeCrossTableCollision || edgeNodeCollision || newEdgeDependencyCollision)
            return TopologyStateStatus.DuplicateId;

        var supportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "frame", "group", "server", "application"
        };
        if (state.Nodes.Any(node =>
                !supportedTypes.Contains(node.NodeType) ||
                !double.IsFinite(node.X) || !double.IsFinite(node.Y) ||
                (node.Width.HasValue && (!double.IsFinite(node.Width.Value) || node.Width <= 0)) ||
                (node.Height.HasValue && (!double.IsFinite(node.Height.Value) || node.Height <= 0))))
            return TopologyStateStatus.InvalidRequest;

        var byId = state.Nodes.ToDictionary(node => node.Id);
        foreach (var node in state.Nodes)
        {
            if (!node.ParentNodeId.HasValue)
                continue;
            if (node.ParentNodeId == node.Id || !byId.TryGetValue(node.ParentNodeId.Value, out var parent) ||
                !IsValidParent(node.NodeType, parent.NodeType))
                return TopologyStateStatus.InvalidParent;

            var seen = new HashSet<Guid> { node.Id };
            var cursor = parent;
            while (cursor.ParentNodeId.HasValue)
            {
                if (!seen.Add(cursor.Id) || !byId.TryGetValue(cursor.ParentNodeId.Value, out cursor!))
                    return TopologyStateStatus.InvalidParent;
            }
        }

        var serverReferences = state.Nodes
            .Where(node => node.NodeType.Equals("server", StringComparison.OrdinalIgnoreCase))
            .Select(node => node.ReferenceId).ToArray();
        var deploymentReferences = state.Nodes
            .Where(node => node.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase))
            .Select(node => node.ReferenceId).ToArray();
        if (serverReferences.Any(reference => !reference.HasValue || reference == Guid.Empty) ||
            deploymentReferences.Any(reference => !reference.HasValue || reference == Guid.Empty) ||
            serverReferences.Where(reference => reference.HasValue).Distinct().Count() != serverReferences.Length ||
            deploymentReferences.Where(reference => reference.HasValue).Distinct().Count() != deploymentReferences.Length ||
            state.Nodes.Any(node =>
                (node.NodeType.Equals("frame", StringComparison.OrdinalIgnoreCase) ||
                 node.NodeType.Equals("group", StringComparison.OrdinalIgnoreCase)) && node.ReferenceId.HasValue))
            return TopologyStateStatus.InvalidReference;

        var serverIds = await _context.Servers.IgnoreQueryFilters()
            .Where(server => server.WorkspaceId == workspaceId &&
                             (server.OwnerUserId == ownerUserId || server.OwnerUserId == null) &&
                             serverReferences.Contains(server.Id))
            .Select(server => server.Id).ToListAsync();
        var mappings = await _context.PortMappings.IgnoreQueryFilters()
            .Where(mapping => mapping.WorkspaceId == workspaceId &&
                              (mapping.OwnerUserId == ownerUserId || mapping.OwnerUserId == null) &&
                              deploymentReferences.Contains(mapping.Id))
            .Select(mapping => new { mapping.Id, mapping.ServerId, mapping.AppId })
            .ToDictionaryAsync(mapping => mapping.Id);
        if (serverIds.Count != serverReferences.Length || mappings.Count != deploymentReferences.Length)
            return TopologyStateStatus.InvalidReference;

        foreach (var applicationNode in state.Nodes.Where(node =>
                     node.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) &&
                     node.ParentNodeId.HasValue))
        {
            var parent = byId[applicationNode.ParentNodeId!.Value];
            if (parent.NodeType.Equals("server", StringComparison.OrdinalIgnoreCase) &&
                (!parent.ReferenceId.HasValue ||
                 mappings[applicationNode.ReferenceId!.Value].ServerId != parent.ReferenceId.Value))
                return TopologyStateStatus.InvalidParent;
        }

        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);
        var dependencyReferences = new List<Guid>();
        foreach (var edge in state.Edges)
        {
            if (edge.SourceNodeId == Guid.Empty || edge.TargetNodeId == Guid.Empty ||
                edge.SourceNodeId == edge.TargetNodeId ||
                !byId.ContainsKey(edge.SourceNodeId) || !byId.ContainsKey(edge.TargetNodeId))
                return TopologyStateStatus.InvalidEdge;
            var key = $"{edge.SourceNodeId:N}|{edge.TargetNodeId:N}|{edge.SourceHandle}|{edge.TargetHandle}";
            if (!edgeKeys.Add(key))
                return TopologyStateStatus.InvalidEdge;
            if (edge.ReferenceId.HasValue)
                dependencyReferences.Add(edge.ReferenceId.Value);
        }

        if (dependencyReferences.Count > 0)
        {
            if (dependencyReferences.Distinct().Count() != dependencyReferences.Count)
                return TopologyStateStatus.InvalidReference;
            var found = await _context.AppDependencies.IgnoreQueryFilters()
                .Where(dependency => dependency.WorkspaceId == workspaceId &&
                                     (dependency.OwnerUserId == ownerUserId || dependency.OwnerUserId == null) &&
                                     dependencyReferences.Contains(dependency.Id))
                .Select(dependency => new
                {
                    dependency.Id,
                    dependency.SourceAppId,
                    dependency.DestAppId,
                    dependency.DestPortId
                }).ToDictionaryAsync(dependency => dependency.Id);
            if (found.Count != dependencyReferences.Count)
                return TopologyStateStatus.InvalidReference;
            foreach (var edge in state.Edges.Where(edge => edge.ReferenceId.HasValue))
            {
                var source = byId[edge.SourceNodeId];
                var target = byId[edge.TargetNodeId];
                if (!source.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) ||
                    !target.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) ||
                    !source.ReferenceId.HasValue || !target.ReferenceId.HasValue ||
                    !mappings.TryGetValue(source.ReferenceId.Value, out var sourceMapping) ||
                    !mappings.TryGetValue(target.ReferenceId.Value, out var targetMapping))
                    return TopologyStateStatus.InvalidReference;
                var dependency = found[edge.ReferenceId!.Value];
                if (dependency.SourceAppId != sourceMapping.AppId ||
                    dependency.DestAppId != targetMapping.AppId ||
                    dependency.DestPortId != targetMapping.Id)
                    return TopologyStateStatus.InvalidReference;
            }
        }

        return TopologyStateStatus.Success;
    }

    private static bool IsValidParent(string nodeType, string parentType)
    {
        if (nodeType.Equals("frame", StringComparison.OrdinalIgnoreCase))
            return false;
        if (nodeType.Equals("group", StringComparison.OrdinalIgnoreCase) ||
            nodeType.Equals("server", StringComparison.OrdinalIgnoreCase))
            return parentType.Equals("frame", StringComparison.OrdinalIgnoreCase) ||
                   parentType.Equals("group", StringComparison.OrdinalIgnoreCase);
        return parentType.Equals("frame", StringComparison.OrdinalIgnoreCase) ||
               parentType.Equals("group", StringComparison.OrdinalIgnoreCase) ||
               parentType.Equals("server", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<TopologyTreeDto>> GetTopologyTreeAsync(Guid? datacenterId = null, int skip = 0, int take = 100, List<string>? labels = null, string? ownerUserId = null)
    {
        var scope = await ResolveReadScopeAsync(ownerUserId);
        if (scope is null) return [];
        var transitionalWorkspaceIds = await TransitionalWorkspaceIdsAsync(scope.OwnerUserId);
        var allowedServers = scope.ReadableServerIds;
        var allowedApps = scope.ReadableApplicationIds;
        var query = _context.Datacenters.IgnoreQueryFilters()
            .Include(d => d.Servers)
                .ThenInclude(s => s.PortMappings)
                    .ThenInclude(pm => pm.Application)
            .Include(d => d.Servers)
                .ThenInclude(s => s.Labels)
            .AsSplitQuery()
            .AsNoTracking()
            .Where(d => (d.OwnerUserId == scope.OwnerUserId || d.OwnerUserId == null && transitionalWorkspaceIds.Contains(d.WorkspaceId)) &&
                        d.Servers.Any(s => allowedServers.Contains(s.Id)));

        if (datacenterId.HasValue && datacenterId.Value != Guid.Empty)
        {
            query = query.Where(d => d.Id == datacenterId.Value);
        }

        if (labels != null && labels.Any())
        {
            query = query.Where(d => d.Servers.Any(s => s.Labels.Any(l =>
                labels.Contains(l.Key) || labels.Contains(l.Value) || labels.Contains(l.Key + ":" + l.Value))));
        }

        var datacenters = await query
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return datacenters.Select(d => new TopologyTreeDto
        {
            Id = d.Id,
            Name = d.Name,
            Location = d.Location,
            Servers = d.Servers
                .Where(s => allowedServers.Contains(s.Id) && (labels == null || !labels.Any() || s.Labels.Any(l =>
                    labels.Contains(l.Key) || labels.Contains(l.Value) || labels.Contains(l.Key + ":" + l.Value))))
                .Select(s => new ServerNodeDto
                {
                    Id = s.Id,
                    ServerId = s.Id,
                    Hostname = s.Hostname,
                    IpAddress = s.IpAddress,
                    Labels = s.Labels.Select(l => new LabelDto
                    {
                        Key = l.Key,
                        Value = l.Value
                    }).ToList(),
                    Applications = s.PortMappings.Where(pm => allowedApps.Contains(pm.AppId)).Select(pm => new ApplicationNodeDto
                    {
                        Id = pm.Id,
                        AppId = pm.AppId,
                        ServerId = pm.ServerId,
                        Name = pm.Application!.AppName,
                        PortMappingId = pm.Id,
                        Port = pm.PortNumber,
                        Protocol = pm.Protocol
                    }).ToList()
                }).ToList()
        });
    }

    public async Task<DependencyMapDto> GetDependencyMapAsync(string? environment = null, Guid? datacenterId = null, List<string>? labels = null, string? ownerUserId = null)
    {
        var scope = await ResolveReadScopeAsync(ownerUserId);
        if (scope is null) return new();
        var transitionalWorkspaceIds = await TransitionalWorkspaceIdsAsync(scope.OwnerUserId);
        var allowedServers = scope.ReadableServerIds;
        var allowedApps = scope.ReadableApplicationIds;
        var serverQuery = _context.Servers.IgnoreQueryFilters()
            .Include(s => s.PortMappings)
                .ThenInclude(pm => pm.Application)
            .Include(s => s.Labels)
            .AsSplitQuery()
            .AsNoTracking()
            .Where(s => (s.OwnerUserId == scope.OwnerUserId || s.OwnerUserId == null && transitionalWorkspaceIds.Contains(s.WorkspaceId)) &&
                        allowedServers.Contains(s.Id));

        if (!string.IsNullOrWhiteSpace(environment))
        {
            serverQuery = serverQuery.Where(s => s.Environment == environment);
        }

        if (datacenterId.HasValue && datacenterId.Value != Guid.Empty)
        {
            serverQuery = serverQuery.Where(s => s.DatacenterId == datacenterId.Value);
        }

        if (labels != null && labels.Any())
        {
            serverQuery = serverQuery.Where(s => s.Labels.Any(l =>
                labels.Contains(l.Key) || labels.Contains(l.Value) || labels.Contains(l.Key + ":" + l.Value)));
        }

        var servers = await serverQuery.ToListAsync();

        var connections = await _context.AppDependencies.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(ad => (ad.OwnerUserId == scope.OwnerUserId || ad.OwnerUserId == null && transitionalWorkspaceIds.Contains(ad.WorkspaceId)) &&
                         (allowedApps.Contains(ad.SourceAppId) || allowedApps.Contains(ad.DestAppId)))
            .Select(ad => new ConnectionDto
            {
                Id = ad.Id,
                SourceAppId = ad.SourceAppId,
                TargetAppId = ad.DestAppId,
                DestinationPortMappingId = ad.DestPortId,
                DestinationServerId = ad.DestinationPort!.ServerId,
                ConnectionType = ad.ConnectionType
            })
            .ToListAsync();

        var restrictedNodes = new Dictionary<Guid, RestrictedDependencyNodeDto>();
        if (scope.EffectivePermission != LabelEffectivePermission.Owner)
        {
            foreach (var connection in connections)
            {
                var sourceVisible = allowedApps.Contains(connection.SourceAppId);
                var targetVisible = allowedApps.Contains(connection.TargetAppId) && allowedServers.Contains(connection.DestinationServerId);
                if (sourceVisible && targetVisible) continue;
                var salt = $"{_currentUser.UserId}:{scope.OwnerUserId}";
                if (!sourceVisible) { var opaque = OpaqueId(salt, connection.SourceAppId); restrictedNodes.TryAdd(opaque, new(opaque)); connection.SourceAppId = opaque; }
                if (!targetVisible) { var opaque = OpaqueId(salt, connection.TargetAppId); restrictedNodes.TryAdd(opaque, new(opaque)); connection.TargetAppId = opaque; connection.DestinationServerId = OpaqueId(salt, connection.DestinationServerId); connection.DestinationPortMappingId = OpaqueId(salt, connection.DestinationPortMappingId); }
                connection.Id = OpaqueId(salt, connection.Id);
                connection.ConnectionType = "Restricted";
                connection.IsRestricted = true;
            }
        }

        return new DependencyMapDto
        {
            Servers = servers.Select(s => new ServerNodeDto
            {
                Id = s.Id,
                ServerId = s.Id,
                Hostname = s.Hostname,
                IpAddress = s.IpAddress,
                Labels = s.Labels.Select(l => new LabelDto
                {
                    Key = l.Key,
                    Value = l.Value
                }).ToList(),
                Applications = s.PortMappings.Where(pm => allowedApps.Contains(pm.AppId)).Select(pm => new ApplicationNodeDto
                {
                    Id = pm.Id,
                    AppId = pm.AppId,
                    ServerId = pm.ServerId,
                    Name = pm.Application!.AppName,
                    PortMappingId = pm.Id,
                    Port = pm.PortNumber,
                    Protocol = pm.Protocol
                }).ToList()
            }).ToList(),
            Connections = connections,
            RestrictedNodes = restrictedNodes.Values.ToList()
        };
    }

    public async Task<IEnumerable<ApplicationStatusDto>> GetApplicationStatusAsync(string? ownerUserId = null)
    {
        var scope = await ResolveReadScopeAsync(ownerUserId);
        if (scope is null) return [];
        var transitionalWorkspaceIds = await TransitionalWorkspaceIdsAsync(scope.OwnerUserId);
        var allowedApps = scope.ReadableApplicationIds;
        var ownedDependencies = _context.AppDependencies.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.OwnerUserId == scope.OwnerUserId ||
                           item.OwnerUserId == null && transitionalWorkspaceIds.Contains(item.WorkspaceId));
        var mappedAppIds = await ownedDependencies.Select(ad => ad.SourceAppId)
            .Union(ownedDependencies.Select(ad => ad.DestAppId))
            .Distinct()
            .ToListAsync();

        return await _context.Applications.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => (a.OwnerUserId == scope.OwnerUserId || a.OwnerUserId == null && transitionalWorkspaceIds.Contains(a.WorkspaceId)) &&
                        allowedApps.Contains(a.Id))
            .Select(a => new ApplicationStatusDto
            {
                Id = a.Id,
                AppName = a.AppName,
                IsMapped = mappedAppIds.Contains(a.Id)
            })
            .ToListAsync();

    }

    private Task<OwnerGraphAccessDto?> ResolveReadScopeAsync(string? ownerUserId)
    {
        var actorUserId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actorUserId)) return Task.FromResult<OwnerGraphAccessDto?>(null);
        var selectedOwner = string.IsNullOrWhiteSpace(ownerUserId) ? actorUserId : ownerUserId.Trim();
        return _graphAccess.ResolveAsync(selectedOwner);
    }

    private Task<List<Guid>> TransitionalWorkspaceIdsAsync(string ownerUserId) =>
        _context.Workspaces.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId)
            .Select(item => item.Id).ToListAsync();
}
