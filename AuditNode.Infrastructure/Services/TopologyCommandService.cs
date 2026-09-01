using System.Data;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AuditNode.Infrastructure.Services;

public sealed class TopologyCommandService(
    AuditDbContext context,
    IOwnerGraphAccessService graphAccess,
    ICurrentUserService currentUser,
    ILogger<TopologyCommandService> logger) : ITopologyCommandService
{
    private const int MaxOperations = 100;

    public async Task<TopologyCommandResult> ExecuteAsync(TopologyCommandBatchDto batch, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId) || batch.Version is null || batch.Version < 0 ||
            batch.Operations is null || batch.Operations.Count is < 1 or > MaxOperations ||
            batch.Operations.Any(operation => operation is null))
            return Invalid("A version and between 1 and 100 operations are required.");

        var actorId = currentUser.UserId!;
        IDbContextTransaction? transaction = null;
        try
        {
            if (context.Database.IsRelational())
                transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            var ownerUserId = await ResolveBatchOwnerAsync(batch.Operations, cancellationToken);
            if (ownerUserId is null)
                return await FailAsync(TopologyCommandStatus.Forbidden, 0, "The graph owner could not be resolved.", transaction, cancellationToken);
            var ownerState = await LockOwnerStateAsync(ownerUserId, cancellationToken);
            var access = await graphAccess.ResolveAsync(ownerUserId, lockForWrite: true, cancellationToken);
            if (access is null || access.EffectivePermission == LabelEffectivePermission.Viewer)
                return await FailAsync(TopologyCommandStatus.Forbidden, ownerState.TopologyVersion, "Graph editing is forbidden.", transaction, cancellationToken);
            if (ownerState.TopologyVersion != batch.Version.Value)
                return await FailAsync(TopologyCommandStatus.Conflict, ownerState.TopologyVersion, "The topology changed. Refresh and retry.", transaction, cancellationToken);

            var nodes = await context.TopologyNodes.IgnoreQueryFilters()
                .Where(item => item.OwnerUserId == ownerUserId)
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            var edges = await context.TopologyEdges.IgnoreQueryFilters()
                .Where(item => item.OwnerUserId == ownerUserId)
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            var allowed = await ResolveEditableNodeIdsAsync(access, nodes, cancellationToken);
            var isEditor = access.EffectivePermission == LabelEffectivePermission.Editor;
            if (HasConflictingTargets(batch.Operations))
                return await FailAsync(TopologyCommandStatus.InvalidRequest, ownerState.TopologyVersion, "A resource may be targeted only once per batch.", transaction, cancellationToken);

            foreach (var operation in batch.Operations)
            {
                var error = await ApplyAsync(operation!, isEditor, allowed, nodes, edges, ownerUserId, cancellationToken);
                if (error is null) continue;

                logger.LogWarning(
                    "Topology command rejected. ActorUserId={ActorUserId} OwnerUserId={OwnerUserId} CommandType={CommandType} Reason={Reason}",
                    actorId, ownerUserId, operation!.Type, error.Value.Error);
                return await FailAsync(error.Value.Status, ownerState.TopologyVersion, error.Value.Error, transaction, cancellationToken);
            }

            if (!ValidateGraph(nodes.Values, edges.Values))
                return await FailAsync(TopologyCommandStatus.InvalidRequest, ownerState.TopologyVersion, "The resulting topology is invalid.", transaction, cancellationToken);

            ownerState.TopologyVersion++;
            ownerState.UpdatedAt = DateTime.UtcNow;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return await FailAsync(TopologyCommandStatus.Conflict, ownerState.TopologyVersion, "The topology changed. Refresh and retry.", transaction, cancellationToken);
            }

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Topology command batch applied. ActorUserId={ActorUserId} OwnerUserId={OwnerUserId} OperationCount={OperationCount} PreviousVersion={PreviousVersion} NewVersion={NewVersion}",
                actorId, ownerUserId, batch.Operations.Count, batch.Version.Value, ownerState.TopologyVersion);
            return new(TopologyCommandStatus.Success, ownerState.TopologyVersion);
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<string?> ResolveBatchOwnerAsync(
        IReadOnlyList<TopologyCommandDto?> operations,
        CancellationToken cancellationToken)
    {
        var nodeIds = operations.Where(item => item is not null)
            .SelectMany(item => new[] { item!.NodeId, item.SourceNodeId, item.TargetNodeId })
            .Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToArray();
        var edgeIds = operations.Where(item => item is not null &&
                                               !string.Equals(item!.Type, "createEdge", StringComparison.OrdinalIgnoreCase))
            .Select(item => item!.EdgeId).Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToArray();
        var owners = await context.TopologyNodes.IgnoreQueryFilters().AsNoTracking()
            .Where(item => nodeIds.Contains(item.Id))
            .Select(item => item.OwnerUserId).ToListAsync(cancellationToken);
        owners.AddRange(await context.TopologyEdges.IgnoreQueryFilters().AsNoTracking()
            .Where(item => edgeIds.Contains(item.Id))
            .Select(item => item.OwnerUserId).ToListAsync(cancellationToken));
        if (owners.Count == 0 || owners.Any(string.IsNullOrWhiteSpace)) return null;
        var distinct = owners.Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    private async Task<OwnerCatalogState> LockOwnerStateAsync(string ownerUserId, CancellationToken cancellationToken)
    {
        if (context.Database.IsRelational())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO owner_catalog_states (owner_user_id, topology_version, updated_at) VALUES ({ownerUserId}, 0, CURRENT_TIMESTAMP) ON CONFLICT (owner_user_id) DO NOTHING",
                cancellationToken);
            return await context.OwnerCatalogStates.FromSqlInterpolated(
                    $"SELECT * FROM owner_catalog_states WHERE owner_user_id = {ownerUserId} FOR UPDATE")
                .SingleAsync(cancellationToken);
        }

        var state = await context.OwnerCatalogStates.SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId, cancellationToken);
        if (state is not null) return state;
        state = new OwnerCatalogState { OwnerUserId = ownerUserId };
        context.OwnerCatalogStates.Add(state);
        return state;
    }

    private async Task<HashSet<Guid>> ResolveEditableNodeIdsAsync(
        OwnerGraphAccessDto access,
        IReadOnlyDictionary<Guid, TopologyNode> nodes,
        CancellationToken cancellationToken)
    {
        if (access.EffectivePermission == LabelEffectivePermission.Owner)
            return nodes.Keys.ToHashSet();
        var deploymentIds = await context.PortMappings.IgnoreQueryFilters()
            .Where(mapping => mapping.OwnerUserId == access.OwnerUserId &&
                              access.EditableApplicationIds.Contains(mapping.AppId) &&
                              access.EditableServerIds.Contains(mapping.ServerId))
            .Select(mapping => mapping.Id)
            .ToListAsync(cancellationToken);
        return nodes.Values.Where(node =>
                node.NodeType.Equals("server", StringComparison.OrdinalIgnoreCase)
                    ? node.ReferenceId.HasValue && access.EditableServerIds.Contains(node.ReferenceId.Value)
                    : node.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) &&
                      node.ReferenceId.HasValue && deploymentIds.Contains(node.ReferenceId.Value))
            .Select(node => node.Id).ToHashSet();
    }

    private async Task<(TopologyCommandStatus Status, string Error)?> ApplyAsync(
        TopologyCommandDto operation,
        bool isEditor,
        HashSet<Guid> allowed,
        IDictionary<Guid, TopologyNode> nodes,
        IDictionary<Guid, TopologyEdge> edges,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        var type = operation.Type?.Trim().ToLowerInvariant();
        return type switch
        {
            "movenode" or "updatenode" or "updatenodegeometry" => ApplyMove(operation, isEditor, allowed, nodes),
            "deletenode" => await ApplyDeleteNodeAsync(operation, isEditor, allowed, nodes, edges, ownerUserId, cancellationToken),
            "createedge" => await ApplyCreateEdgeAsync(operation, allowed, nodes, edges, ownerUserId, cancellationToken),
            "updateedge" => await ApplyUpdateEdgeAsync(operation, allowed, nodes, edges, ownerUserId, cancellationToken),
            "deleteedge" => await ApplyDeleteEdgeAsync(operation, allowed, nodes, edges, ownerUserId, cancellationToken),
            _ => InvalidTuple("Unsupported topology command type.")
        };
    }

    private static (TopologyCommandStatus Status, string Error)? ApplyMove(
        TopologyCommandDto operation,
        bool isEditor,
        HashSet<Guid> allowed,
        IDictionary<Guid, TopologyNode> nodes)
    {
        if (!operation.NodeId.HasValue || !operation.X.HasValue || !operation.Y.HasValue ||
            !double.IsFinite(operation.X.Value) || !double.IsFinite(operation.Y.Value) ||
            operation.Width is <= 0 || operation.Height is <= 0 ||
            operation.Width.HasValue && !double.IsFinite(operation.Width.Value) ||
            operation.Height.HasValue && !double.IsFinite(operation.Height.Value))
            return InvalidTuple("A valid node id, position and optional positive size are required.");
        if (!nodes.TryGetValue(operation.NodeId.Value, out var node) || !allowed.Contains(node.Id))
            return ForbiddenTuple();
        if (isEditor && (node.NodeType.Equals("frame", StringComparison.OrdinalIgnoreCase) ||
                          node.NodeType.Equals("group", StringComparison.OrdinalIgnoreCase)))
            return ForbiddenTuple("Editors cannot modify frames or groups.");

        var geometryOnly = isEditor;
        if (!geometryOnly && operation.ParentId.HasValue && !nodes.ContainsKey(operation.ParentId.Value))
            return ForbiddenTuple();

        node.X = operation.X.Value;
        node.Y = operation.Y.Value;
        if (!geometryOnly) node.ParentNodeId = operation.ParentId;
        if (operation.Width.HasValue) node.Width = operation.Width;
        if (operation.Height.HasValue) node.Height = operation.Height;
        return null;
    }

    private async Task<(TopologyCommandStatus Status, string Error)?> ApplyDeleteNodeAsync(
        TopologyCommandDto operation,
        bool isEditor,
        HashSet<Guid> allowed,
        IDictionary<Guid, TopologyNode> nodes,
        IDictionary<Guid, TopologyEdge> edges,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        if (!operation.NodeId.HasValue || !nodes.TryGetValue(operation.NodeId.Value, out var node) || !allowed.Contains(node.Id))
            return ForbiddenTuple();
        if (isEditor && (node.NodeType.Equals("frame", StringComparison.OrdinalIgnoreCase) ||
                          node.NodeType.Equals("group", StringComparison.OrdinalIgnoreCase)))
            return ForbiddenTuple("Editors cannot delete frames or groups.");

        var nodeSnapshot = nodes as IReadOnlyDictionary<Guid, TopologyNode> ?? nodes.ToDictionary(item => item.Key, item => item.Value);
        var subtree = nodes.Values.Where(candidate => IsDescendant(candidate.Id, new HashSet<Guid> { node.Id }, nodeSnapshot)).Select(candidate => candidate.Id).ToHashSet();
        if (subtree.Any(id => !allowed.Contains(id)))
            return ForbiddenTuple();
        var connected = edges.Values.Where(edge => subtree.Contains(edge.SourceNodeId) || subtree.Contains(edge.TargetNodeId)).ToList();
        if (isEditor && (subtree.Count != 1 || connected.Count != 0))
            return ForbiddenTuple("Editors can delete only workload leaf nodes without connections.");
        if (connected.Any(edge => !allowed.Contains(edge.SourceNodeId) || !allowed.Contains(edge.TargetNodeId)))
            return ForbiddenTuple();
        foreach (var edge in connected)
        {
            var referenceValidation = await ValidateOwnedDependencyReferenceAsync(edge, nodes, edges, cancellationToken);
            if (referenceValidation is not null) return referenceValidation;
        }

        var references = connected.Where(edge => edge.ReferenceId.HasValue).Select(edge => edge.ReferenceId!.Value).ToList();
        if (references.Count > 0)
        {
            var dependencies = await context.AppDependencies.IgnoreQueryFilters()
                .Where(item => item.OwnerUserId == ownerUserId && references.Contains(item.Id)).ToListAsync(cancellationToken);
            context.AppDependencies.RemoveRange(dependencies);
        }
        context.TopologyEdges.RemoveRange(connected);
        context.TopologyNodes.RemoveRange(subtree.Select(id => nodes[id]));
        foreach (var edge in connected) edges.Remove(edge.Id);
        foreach (var id in subtree) nodes.Remove(id);
        return null;
    }

    private async Task<(TopologyCommandStatus Status, string Error)?> ApplyCreateEdgeAsync(
        TopologyCommandDto operation,
        HashSet<Guid> allowed,
        IDictionary<Guid, TopologyNode> nodes,
        IDictionary<Guid, TopologyEdge> edges,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        if (!operation.EdgeId.HasValue || operation.EdgeId == Guid.Empty)
            return InvalidTuple("A unique edge id is required.");
        if (nodes.ContainsKey(operation.EdgeId.Value) ||
            await context.TopologyNodes.IgnoreQueryFilters().AnyAsync(item => item.Id == operation.EdgeId.Value, cancellationToken))
            return InvalidTuple("Node and edge ids must be distinct.");
        var endpoints = ValidateEndpoints(operation.SourceNodeId, operation.TargetNodeId, allowed, nodes);
        if (endpoints.Error is not null) return endpoints.Error;
        if (edges.TryGetValue(operation.EdgeId.Value, out var existingEdge))
            return allowed.Contains(existingEdge.SourceNodeId) && allowed.Contains(existingEdge.TargetNodeId)
                ? InvalidTuple("A unique edge id is required.")
                : ForbiddenTuple();
        if (await context.TopologyEdges.IgnoreQueryFilters().AnyAsync(item => item.Id == operation.EdgeId.Value, cancellationToken) ||
            await context.AppDependencies.IgnoreQueryFilters().AnyAsync(item => item.Id == operation.EdgeId.Value, cancellationToken))
            return InvalidTuple("A unique edge id is required.");
        var dependency = await BuildDependencyAsync(operation.EdgeId.Value, endpoints.Source!, endpoints.Target!, null, ownerUserId, cancellationToken);
        if (dependency.Error is not null) return dependency.Error;

        var edge = NewEdge(operation.EdgeId.Value, operation, dependency.Dependency!.Id, endpoints.Source!, ownerUserId);
        context.AppDependencies.Add(dependency.Dependency);
        context.TopologyEdges.Add(edge);
        edges.Add(edge.Id, edge);
        return null;
    }

    private async Task<(TopologyCommandStatus Status, string Error)?> ApplyUpdateEdgeAsync(
        TopologyCommandDto operation,
        HashSet<Guid> allowed,
        IDictionary<Guid, TopologyNode> nodes,
        IDictionary<Guid, TopologyEdge> edges,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        if (!operation.EdgeId.HasValue || !edges.TryGetValue(operation.EdgeId.Value, out var edge) ||
            !allowed.Contains(edge.SourceNodeId) || !allowed.Contains(edge.TargetNodeId))
            return ForbiddenTuple();
        var referenceValidation = await ValidateOwnedDependencyReferenceAsync(edge, nodes, edges, cancellationToken);
        if (referenceValidation is not null) return referenceValidation;
        var endpoints = ValidateEndpoints(operation.SourceNodeId, operation.TargetNodeId, allowed, nodes);
        if (endpoints.Error is not null) return endpoints.Error;

        AppDependency? existingDependency = null;
        if (edge.ReferenceId.HasValue)
            existingDependency = await context.AppDependencies.IgnoreQueryFilters().SingleOrDefaultAsync(
                item => item.OwnerUserId == ownerUserId && item.Id == edge.ReferenceId.Value, cancellationToken);
        var dependency = await BuildDependencyAsync(edge.ReferenceId ?? edge.Id, endpoints.Source!, endpoints.Target!, existingDependency, ownerUserId, cancellationToken);
        if (dependency.Error is not null) return dependency.Error;
        if (existingDependency is null) context.AppDependencies.Add(dependency.Dependency!);

        edge.SourceNodeId = operation.SourceNodeId!.Value;
        edge.TargetNodeId = operation.TargetNodeId!.Value;
        edge.SourceHandle = SafeText(operation.SourceHandle);
        edge.TargetHandle = SafeText(operation.TargetHandle);
        edge.EdgeType = SafeText(operation.EdgeType, "default");
        edge.Label = SafeText(operation.Label);
        edge.ReferenceId = dependency.Dependency!.Id;
        return null;
    }

    private async Task<(TopologyCommandStatus Status, string Error)?> ApplyDeleteEdgeAsync(
        TopologyCommandDto operation,
        HashSet<Guid> allowed,
        IDictionary<Guid, TopologyNode> nodes,
        IDictionary<Guid, TopologyEdge> edges,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        if (!operation.EdgeId.HasValue || !edges.TryGetValue(operation.EdgeId.Value, out var edge) ||
            !allowed.Contains(edge.SourceNodeId) || !allowed.Contains(edge.TargetNodeId))
            return ForbiddenTuple();
        var referenceValidation = await ValidateOwnedDependencyReferenceAsync(edge, nodes, edges, cancellationToken);
        if (referenceValidation is not null) return referenceValidation;
        if (edge.ReferenceId.HasValue)
        {
            var dependency = await context.AppDependencies.IgnoreQueryFilters().SingleOrDefaultAsync(
                item => item.OwnerUserId == ownerUserId && item.Id == edge.ReferenceId.Value, cancellationToken);
            if (dependency is not null) context.AppDependencies.Remove(dependency);
        }
        context.TopologyEdges.Remove(edge);
        edges.Remove(edge.Id);
        return null;
    }

    private async Task<(TopologyCommandStatus Status, string Error)?> ValidateOwnedDependencyReferenceAsync(
        TopologyEdge edge,
        IDictionary<Guid, TopologyNode> nodes,
        IDictionary<Guid, TopologyEdge> edges,
        CancellationToken cancellationToken)
    {
        if (!edge.ReferenceId.HasValue) return null;
        if (edges.Values.Any(other => other.Id != edge.Id && other.ReferenceId == edge.ReferenceId))
            return InvalidTuple("An edge dependency reference must be unique.");
        if (!nodes.TryGetValue(edge.SourceNodeId, out var source) ||
            !nodes.TryGetValue(edge.TargetNodeId, out var target) ||
            !source.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) ||
            !target.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) ||
            !source.ReferenceId.HasValue || !target.ReferenceId.HasValue)
            return InvalidTuple("The edge dependency reference is invalid.");

        var mappingIds = new[] { source.ReferenceId.Value, target.ReferenceId.Value };
        var mappings = await context.PortMappings.IgnoreQueryFilters()
            .Where(item => item.OwnerUserId == edge.OwnerUserId && mappingIds.Contains(item.Id))
            .Select(item => new { item.Id, item.AppId })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var dependency = await context.AppDependencies.IgnoreQueryFilters().SingleOrDefaultAsync(
            item => item.OwnerUserId == edge.OwnerUserId && item.Id == edge.ReferenceId.Value,
            cancellationToken);
        if (mappings.Count != 2 || dependency is null ||
            dependency.SourceAppId != mappings[source.ReferenceId.Value].AppId ||
            dependency.DestAppId != mappings[target.ReferenceId.Value].AppId ||
            dependency.DestPortId != target.ReferenceId.Value)
            return InvalidTuple("The edge dependency reference is invalid.");
        return null;
    }

    private static (TopologyNode? Source, TopologyNode? Target, (TopologyCommandStatus Status, string Error)? Error) ValidateEndpoints(
        Guid? sourceId,
        Guid? targetId,
        HashSet<Guid> allowed,
        IDictionary<Guid, TopologyNode> nodes)
    {
        if (!sourceId.HasValue || !targetId.HasValue || sourceId == targetId ||
            !nodes.TryGetValue(sourceId.Value, out var source) || !nodes.TryGetValue(targetId.Value, out var target) ||
            !allowed.Contains(source.Id) || !allowed.Contains(target.Id))
            return (null, null, ForbiddenTuple("Both edge endpoints must be inside the granted scope."));
        if (!source.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) ||
            !target.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) ||
            !source.ReferenceId.HasValue || !target.ReferenceId.HasValue)
            return (null, null, InvalidTuple("Dependency edges require two deployment nodes."));
        return (source, target, null);
    }

    private async Task<(AppDependency? Dependency, (TopologyCommandStatus Status, string Error)? Error)> BuildDependencyAsync(
        Guid dependencyId,
        TopologyNode source,
        TopologyNode target,
        AppDependency? existing,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        var mappingIds = new[] { source.ReferenceId!.Value, target.ReferenceId!.Value };
        var mappings = await context.PortMappings.IgnoreQueryFilters().Where(item => item.OwnerUserId == ownerUserId && mappingIds.Contains(item.Id))
            .Select(item => new { item.Id, item.AppId }).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (mappings.Count != 2)
            return (null, ForbiddenTuple("Cross-owner or cross-catalog dependency edges are forbidden."));
        var sourceAppId = mappings[source.ReferenceId.Value].AppId;
        var targetAppId = mappings[target.ReferenceId.Value].AppId;
        if (sourceAppId == targetAppId) return (null, InvalidTuple("An application cannot depend on itself."));
        var duplicate = await context.AppDependencies.IgnoreQueryFilters().AnyAsync(item =>
            item.OwnerUserId == ownerUserId && item.Id != dependencyId && item.SourceAppId == sourceAppId && item.DestAppId == targetAppId && item.DestPortId == target.ReferenceId.Value,
            cancellationToken);
        duplicate = duplicate || context.ChangeTracker.Entries<AppDependency>().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified &&
            entry.Entity.Id != dependencyId &&
            entry.Entity.SourceAppId == sourceAppId &&
            entry.Entity.DestAppId == targetAppId &&
            entry.Entity.DestPortId == target.ReferenceId.Value);
        if (duplicate) return (null, InvalidTuple("Duplicate dependencies are not allowed."));

        var dependency = existing ?? new AppDependency { Id = dependencyId, OwnerUserId = ownerUserId, CreatedAt = DateTime.UtcNow };
        dependency.SourceAppId = sourceAppId;
        dependency.DestAppId = targetAppId;
        dependency.DestPortId = target.ReferenceId.Value;
        dependency.ConnectionType = "Manual";
        return (dependency, null);
    }

    private static TopologyEdge NewEdge(Guid id, TopologyCommandDto operation, Guid dependencyId, TopologyNode source, string ownerUserId) => new()
    {
        Id = id,
        OwnerUserId = ownerUserId,
        SourceNodeId = operation.SourceNodeId!.Value,
        TargetNodeId = operation.TargetNodeId!.Value,
        SourceHandle = SafeText(operation.SourceHandle),
        TargetHandle = SafeText(operation.TargetHandle),
        EdgeType = SafeText(operation.EdgeType, "default"),
        Label = SafeText(operation.Label),
        ReferenceId = dependencyId
    };

    private static bool ValidateGraph(IEnumerable<TopologyNode> nodes, IEnumerable<TopologyEdge> edges)
    {
        var byId = nodes.ToDictionary(item => item.Id);
        foreach (var node in byId.Values)
        {
            if (!double.IsFinite(node.X) || !double.IsFinite(node.Y)) return false;
            if (!node.ParentNodeId.HasValue) continue;
            if (!byId.TryGetValue(node.ParentNodeId.Value, out var parent) || !ValidParent(node.NodeType, parent.NodeType)) return false;
            var visited = new HashSet<Guid> { node.Id };
            var cursor = parent;
            while (cursor.ParentNodeId.HasValue)
            {
                if (!visited.Add(cursor.Id) || !byId.TryGetValue(cursor.ParentNodeId.Value, out cursor!)) return false;
            }
        }
        return edges.All(edge => edge.SourceNodeId != edge.TargetNodeId && byId.ContainsKey(edge.SourceNodeId) && byId.ContainsKey(edge.TargetNodeId));
    }

    private static bool HasConflictingTargets(IReadOnlyList<TopologyCommandDto?> operations)
    {
        var nodeTargets = new HashSet<Guid>();
        var edgeTargets = new HashSet<Guid>();
        foreach (var operation in operations)
        {
            if (operation is null) return true;
            var type = operation.Type?.Trim().ToLowerInvariant();
            if (type is "movenode" or "updatenode" or "updatenodegeometry" or "deletenode")
            {
                if (operation.NodeId.HasValue && !nodeTargets.Add(operation.NodeId.Value)) return true;
            }
            else if (type is "createedge" or "updateedge" or "deleteedge")
            {
                if (operation.EdgeId.HasValue && !edgeTargets.Add(operation.EdgeId.Value)) return true;
            }
        }
        return false;
    }

    private static bool ValidParent(string nodeType, string parentType)
    {
        if (nodeType.Equals("frame", StringComparison.OrdinalIgnoreCase)) return false;
        if (nodeType.Equals("group", StringComparison.OrdinalIgnoreCase) || nodeType.Equals("server", StringComparison.OrdinalIgnoreCase))
            return parentType.Equals("frame", StringComparison.OrdinalIgnoreCase) || parentType.Equals("group", StringComparison.OrdinalIgnoreCase);
        return parentType.Equals("frame", StringComparison.OrdinalIgnoreCase) || parentType.Equals("group", StringComparison.OrdinalIgnoreCase) || parentType.Equals("server", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDescendant(Guid id, IReadOnlySet<Guid> roots, IReadOnlyDictionary<Guid, TopologyNode> nodes)
    {
        var visited = new HashSet<Guid>();
        var cursor = id;
        while (visited.Add(cursor) && nodes.TryGetValue(cursor, out var node))
        {
            if (roots.Contains(cursor)) return true;
            if (!node.ParentNodeId.HasValue) return false;
            cursor = node.ParentNodeId.Value;
        }
        return false;
    }

    private async Task<TopologyCommandResult> FailAsync(
        TopologyCommandStatus status,
        long version,
        string error,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
            await transaction.RollbackAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return new(status, version, error);
    }

    private static TopologyCommandResult Invalid(string error) => new(TopologyCommandStatus.InvalidRequest, 0, error);
    private static (TopologyCommandStatus Status, string Error) InvalidTuple(string error) => (TopologyCommandStatus.InvalidRequest, error);
    private static (TopologyCommandStatus Status, string Error) ForbiddenTuple(string error = "A topology resource is outside the granted scope.") => (TopologyCommandStatus.Forbidden, error);
    private static string SafeText(string? value, string fallback = "")
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized[..Math.Min(normalized.Length, 200)];
    }
}
