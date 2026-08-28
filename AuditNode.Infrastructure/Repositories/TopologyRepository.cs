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
    private readonly IScopedResourcePolicy _policy;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantProvider _tenant;
    private readonly IWorkspaceAccessService _access;

    public TopologyRepository(
        AuditDbContext context,
        IScopedResourcePolicy policy,
        ICurrentUserService currentUser,
        ITenantProvider tenant,
        IWorkspaceAccessService access)
    {
        _context = context;
        _policy = policy;
        _currentUser = currentUser;
        _tenant = tenant;
        _access = access;
    }

    public async Task<TopologyStateDto> GetTopologyStateAsync()
    {
        if (!_tenant.WorkspaceId.HasValue || string.IsNullOrWhiteSpace(_currentUser.UserId)) return new();
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead)
            : null;
        var topologyVersion = await _context.Workspaces.AsNoTracking()
            .Where(workspace => workspace.Id == _tenant.WorkspaceId.Value)
            .Select(workspace => workspace.TopologyVersion)
            .SingleOrDefaultAsync();
        var allowedServers = await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "server");
        var allowedApps = await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "application");
        var frameRoots = await _policy.GetGrantedFrameIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!);
        var allowedMappings = allowedApps is null ? null : (await _context.PortMappings.Where(x => allowedApps.Contains(x.AppId) && (allowedServers == null || allowedServers.Contains(x.ServerId))).Select(x => x.Id).ToListAsync()).ToHashSet();
        var rawNodes = await _context.TopologyNodes.AsNoTracking()
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
        var visibleIds = allowedServers is null && allowedApps is null
            ? rawNodes.Select(node => node.Id).ToHashSet()
            : rawNodes.Where(node => node.NodeType.Equals("server", StringComparison.OrdinalIgnoreCase)
                ? node.ReferenceId.HasValue && (allowedServers == null || allowedServers.Contains(node.ReferenceId.Value))
                : node.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase)
                    ? node.ReferenceId.HasValue && (allowedMappings == null || allowedMappings.Contains(node.ReferenceId.Value))
                    : false).Select(x => x.Id).ToHashSet();
        foreach (var id in visibleIds.ToArray())
        {
            if (frameRoots is not { Count: > 0 }) continue;
            var cursor = rawById[id].ParentNodeId;
            while (cursor.HasValue && visibleIds.Add(cursor.Value) && rawById.TryGetValue(cursor.Value, out var parent))
            {
                if (frameRoots.Contains(cursor.Value)) break;
                cursor = parent.ParentNodeId;
            }
        }
        var nodes = rawNodes.Where(x => visibleIds.Contains(x.Id)).ToList();
        foreach (var node in nodes.Where(x => x.ParentNodeId.HasValue && !visibleIds.Contains(x.ParentNodeId.Value))) node.ParentNodeId = null;
        var rawEdges = await _context.TopologyEdges.AsNoTracking()
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
        foreach (var edge in rawEdges.Where(x => visibleIds.Contains(x.SourceNodeId) || visibleIds.Contains(x.TargetNodeId)))
        {
            var sourceVisible = visibleIds.Contains(edge.SourceNodeId);
            var targetVisible = visibleIds.Contains(edge.TargetNodeId);
            if (!sourceVisible)
            {
                var opaque = OpaqueId(_currentUser.UserId!, edge.SourceNodeId);
                nodes.Add(Restricted(opaque));
                edge.SourceNodeId = opaque;
                edge.Id = OpaqueId(_currentUser.UserId!, edge.Id);
                edge.ReferenceId = null;
            }
            if (!targetVisible)
            {
                var opaque = OpaqueId(_currentUser.UserId!, edge.TargetNodeId);
                nodes.Add(Restricted(opaque));
                edge.TargetNodeId = opaque;
                edge.Id = OpaqueId(_currentUser.UserId!, edge.Id);
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
            if (!_context.CurrentWorkspaceId.HasValue || _context.CurrentWorkspaceId == Guid.Empty ||
                string.IsNullOrWhiteSpace(_currentUser.UserId))
                return TopologyStateStatus.Forbidden;
            var workspaceId = _context.CurrentWorkspaceId.Value;
            var workspace = _context.Database.IsRelational()
                ? await _context.Workspaces.FromSqlInterpolated($"SELECT * FROM workspaces WHERE id = {workspaceId} FOR UPDATE").SingleOrDefaultAsync()
                : await _context.Workspaces.SingleOrDefaultAsync(item => item.Id == workspaceId);
            if (workspace is null) return TopologyStateStatus.Forbidden;
            if (workspace.OwnerUserId != _currentUser.UserId)
            {
                if (_context.Database.IsRelational())
                {
                    _ = await _context.WorkspaceMembers.FromSqlInterpolated(
                            $"SELECT * FROM workspace_members WHERE workspace_id = {workspaceId} AND user_id = {_currentUser.UserId!} FOR UPDATE")
                        .SingleOrDefaultAsync();
                }
                var access = await _access.ResolveAsync(workspaceId, _currentUser.UserId!);
                if (access?.EffectiveRole != WorkspaceRoles.Admin)
                    return TopologyStateStatus.Forbidden;
            }
            if (workspace.TopologyVersion != state.Version.Value)
                return TopologyStateStatus.Conflict;

            var validation = await ValidateStateAsync(state);
            if (validation != TopologyStateStatus.Success)
                return validation;

            var existingNodes = await _context.TopologyNodes.ToDictionaryAsync(n => n.Id);
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

            var existingEdges = await _context.TopologyEdges.ToDictionaryAsync(edge => edge.Id);
            var persistedEdges = new Dictionary<Guid, TopologyEdge>();
            foreach (var edgeDto in state.Edges)
            {
                if (!existingEdges.TryGetValue(edgeDto.Id, out var edge))
                {
                    edge = new TopologyEdge { Id = edgeDto.Id };
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

            var dependencyValidation = await ValidateAndApplyDependenciesAsync(state, persistedEdges);
            if (dependencyValidation != TopologyStateStatus.Success)
                return dependencyValidation;

            workspace.TopologyVersion++;

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
        IReadOnlyDictionary<Guid, TopologyEdge> persistedEdges)
    {
        var payload = state.Dependencies;
        if (payload is null || payload.Any(item =>
                item.SourceAppId == Guid.Empty || item.DestAppId == Guid.Empty ||
                item.DestinationPortMappingId == Guid.Empty || item.SourceAppId == item.DestAppId))
            return TopologyStateStatus.InvalidDependency;
        var keys = payload.Select(DependencyKey).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            return TopologyStateStatus.InvalidDependency;

        var workspaceId = _context.CurrentWorkspaceId!.Value;
        var appIds = payload.SelectMany(item => new[] { item.SourceAppId, item.DestAppId }).Distinct().ToArray();
        var validAppCount = await _context.Applications.IgnoreQueryFilters()
            .CountAsync(item => item.WorkspaceId == workspaceId && appIds.Contains(item.Id));
        if (validAppCount != appIds.Length) return TopologyStateStatus.InvalidDependency;
        var destinationPortIds = payload.Select(item => item.DestinationPortMappingId).Distinct().ToArray();
        var destinationPorts = await _context.PortMappings.IgnoreQueryFilters()
            .Where(item => item.WorkspaceId == workspaceId && destinationPortIds.Contains(item.Id))
            .Select(item => new { item.Id, item.AppId })
            .ToDictionaryAsync(item => item.Id, item => item.AppId);
        if (destinationPorts.Count != destinationPortIds.Length ||
            payload.Any(item => destinationPorts[item.DestinationPortMappingId] != item.DestAppId))
            return TopologyStateStatus.InvalidDependency;

        var existing = await _context.AppDependencies.IgnoreQueryFilters()
            .Where(item => item.WorkspaceId == workspaceId).ToListAsync();
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
            .Where(item => item.WorkspaceId == workspaceId && deploymentIds.Contains(item.Id))
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

    private async Task<TopologyStateStatus> ValidateStateAsync(SaveTopologyStateDto? state)
    {
        if (state?.Nodes is null || state.Edges is null ||
            !_context.CurrentWorkspaceId.HasValue || _context.CurrentWorkspaceId == Guid.Empty)
            return TopologyStateStatus.InvalidRequest;

        if (state.Nodes.Any(node => node.Id == Guid.Empty) ||
            state.Edges.Any(edge => edge.Id == Guid.Empty) ||
            state.Nodes.Select(node => node.Id).Distinct().Count() != state.Nodes.Count ||
            state.Edges.Select(edge => edge.Id).Distinct().Count() != state.Edges.Count ||
            state.Nodes.Select(node => node.Id).Intersect(state.Edges.Select(edge => edge.Id)).Any())
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

        var serverIds = await _context.Servers
            .Where(server => serverReferences.Contains(server.Id))
            .Select(server => server.Id).ToListAsync();
        var mappings = await _context.PortMappings
            .Where(mapping => deploymentReferences.Contains(mapping.Id))
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
            var found = await _context.AppDependencies
                .Where(dependency => dependencyReferences.Contains(dependency.Id))
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

    public async Task<IEnumerable<TopologyTreeDto>> GetTopologyTreeAsync(Guid? datacenterId = null, int skip = 0, int take = 100, List<string>? labels = null)
    {
        var (allowedServers, allowedApps) = await ScopeIdsAsync();
        var query = _context.Datacenters
            .Include(d => d.Servers)
                .ThenInclude(s => s.PortMappings)
                    .ThenInclude(pm => pm.Application)
            .Include(d => d.Servers)
                .ThenInclude(s => s.Labels)
            .AsSplitQuery()
            .AsNoTracking();
        if (allowedServers is not null) query = query.Where(d => d.Servers.Any(s => allowedServers.Contains(s.Id)));

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
                .Where(s => (allowedServers == null || allowedServers.Contains(s.Id)) && (labels == null || !labels.Any() || s.Labels.Any(l =>
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
                    Applications = s.PortMappings.Where(pm => allowedApps == null || allowedApps.Contains(pm.AppId)).Select(pm => new ApplicationNodeDto
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

    public async Task<DependencyMapDto> GetDependencyMapAsync(string? environment = null, Guid? datacenterId = null, List<string>? labels = null)
    {
        var (allowedServers, allowedApps) = await ScopeIdsAsync();
        var serverQuery = _context.Servers
            .Include(s => s.PortMappings)
                .ThenInclude(pm => pm.Application)
            .Include(s => s.Labels)
            .AsSplitQuery()
            .AsNoTracking();
        if (allowedServers is not null) serverQuery = serverQuery.Where(s => allowedServers.Contains(s.Id));

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

        var connections = await _context.AppDependencies
            .AsNoTracking()
            .Where(ad => allowedApps == null || allowedApps.Contains(ad.SourceAppId) || allowedApps.Contains(ad.DestAppId))
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
        if (allowedApps is not null)
        {
            foreach (var connection in connections)
            {
                var sourceVisible = allowedApps.Contains(connection.SourceAppId);
                var targetVisible = allowedApps.Contains(connection.TargetAppId) && (allowedServers == null || allowedServers.Contains(connection.DestinationServerId));
                if (sourceVisible && targetVisible) continue;
                if (!sourceVisible) { var opaque = OpaqueId(_currentUser.UserId!, connection.SourceAppId); restrictedNodes.TryAdd(opaque, new(opaque)); connection.SourceAppId = opaque; }
                if (!targetVisible) { var opaque = OpaqueId(_currentUser.UserId!, connection.TargetAppId); restrictedNodes.TryAdd(opaque, new(opaque)); connection.TargetAppId = opaque; connection.DestinationServerId = OpaqueId(_currentUser.UserId!, connection.DestinationServerId); connection.DestinationPortMappingId = OpaqueId(_currentUser.UserId!, connection.DestinationPortMappingId); }
                connection.Id = OpaqueId(_currentUser.UserId!, connection.Id);
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
                Applications = s.PortMappings.Where(pm => allowedApps == null || allowedApps.Contains(pm.AppId)).Select(pm => new ApplicationNodeDto
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

    public async Task<IEnumerable<ApplicationStatusDto>> GetApplicationStatusAsync()
    {
        var (_, allowedApps) = await ScopeIdsAsync();
        var mappedAppIds = await _context.AppDependencies
            .AsNoTracking()
            .Select(ad => ad.SourceAppId)
            .Union(_context.AppDependencies.AsNoTracking().Select(ad => ad.DestAppId))
            .Distinct()
            .ToListAsync();

        return await _context.Applications
            .AsNoTracking()
            .Where(a => allowedApps == null || allowedApps.Contains(a.Id))
            .Select(a => new ApplicationStatusDto
            {
                Id = a.Id,
                AppName = a.AppName,
                IsMapped = mappedAppIds.Contains(a.Id)
            })
            .ToListAsync();

    }

    private async Task<(IReadOnlySet<Guid>? Servers, IReadOnlySet<Guid>? Applications)> ScopeIdsAsync()
    {
        if (!_tenant.WorkspaceId.HasValue || string.IsNullOrWhiteSpace(_currentUser.UserId)) return (new HashSet<Guid>(), new HashSet<Guid>());
        return (await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "server"),
            await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "application"));
    }
}
