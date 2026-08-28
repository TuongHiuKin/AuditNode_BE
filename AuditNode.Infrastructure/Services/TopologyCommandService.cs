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
    IWorkspaceAccessService accessService,
    IScopedResourcePolicy resourcePolicy,
    ICurrentUserService currentUser,
    ITenantProvider tenant,
    ILogger<TopologyCommandService> logger) : ITopologyCommandService
{
    private const int MaxOperations = 100;

    public async Task<TopologyCommandResult> ExecuteAsync(TopologyCommandBatchDto batch, CancellationToken cancellationToken = default)
    {
        if (!tenant.WorkspaceId.HasValue || tenant.WorkspaceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(currentUser.UserId) || batch.Version is null || batch.Version < 0 ||
            batch.Operations is null || batch.Operations.Count is < 1 or > MaxOperations ||
            batch.Operations.Any(operation => operation is null))
            return Invalid("A version and between 1 and 100 operations are required.");

        var workspaceId = tenant.WorkspaceId.Value;
        var actorId = currentUser.UserId!;
        IDbContextTransaction? transaction = null;
        try
        {
            if (context.Database.IsRelational())
                transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            var workspace = await LockWorkspaceAsync(workspaceId, cancellationToken);
            if (workspace is null)
                return await FailAsync(TopologyCommandStatus.Forbidden, 0, "The workspace is unavailable.", transaction, cancellationToken);

            if (workspace.OwnerUserId != actorId)
                await LockMembershipAsync(workspaceId, actorId, cancellationToken);

            var access = await accessService.ResolveAsync(workspaceId, actorId, cancellationToken);
            if (access is null || access.EffectiveRole == WorkspaceRoles.Viewer || !access.Capabilities.CanEditGraph)
                return await FailAsync(TopologyCommandStatus.Forbidden, workspace.TopologyVersion, "Graph editing is forbidden.", transaction, cancellationToken);
            if (workspace.TopologyVersion != batch.Version.Value)
                return await FailAsync(TopologyCommandStatus.Conflict, workspace.TopologyVersion, "The topology changed. Refresh and retry.", transaction, cancellationToken);

            await LockScopeInputsAsync(workspaceId, access, cancellationToken);

            var nodes = await context.TopologyNodes.IgnoreQueryFilters()
                .Where(item => item.WorkspaceId == workspaceId)
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            var edges = await context.TopologyEdges.IgnoreQueryFilters()
                .Where(item => item.WorkspaceId == workspaceId)
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            var allowed = await ResolveAllowedNodeIdsAsync(workspaceId, actorId, access, nodes, cancellationToken);
            var grantedFrameRoots = access.Scope.Frames.Select(item => item.Id).ToHashSet();
            var isAuditor = access.EffectiveRole == WorkspaceRoles.Auditor;
            var isScopedAuditor = isAuditor && access.Scope.Mode != WorkspaceScopeModes.All;
            if (HasConflictingTargets(batch.Operations))
                return await FailAsync(TopologyCommandStatus.InvalidRequest, workspace.TopologyVersion, "A resource may be targeted only once per batch.", transaction, cancellationToken);

            foreach (var operation in batch.Operations)
            {
                var error = await ApplyAsync(operation!, access.Scope.Mode, isAuditor, isScopedAuditor, allowed, grantedFrameRoots, nodes, edges, cancellationToken);
                if (error is null) continue;

                logger.LogWarning(
                    "Topology command rejected. ActorUserId={ActorUserId} WorkspaceId={WorkspaceId} CommandType={CommandType} Reason={Reason}",
                    actorId, workspaceId, operation!.Type, error.Value.Error);
                return await FailAsync(error.Value.Status, workspace.TopologyVersion, error.Value.Error, transaction, cancellationToken);
            }

            if (!ValidateGraph(nodes.Values, edges.Values))
                return await FailAsync(TopologyCommandStatus.InvalidRequest, workspace.TopologyVersion, "The resulting topology is invalid.", transaction, cancellationToken);

            workspace.TopologyVersion++;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return await FailAsync(TopologyCommandStatus.Conflict, workspace.TopologyVersion, "The topology changed. Refresh and retry.", transaction, cancellationToken);
            }

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Topology command batch applied. ActorUserId={ActorUserId} WorkspaceId={WorkspaceId} OperationCount={OperationCount} PreviousVersion={PreviousVersion} NewVersion={NewVersion}",
                actorId, workspaceId, batch.Operations.Count, batch.Version.Value, workspace.TopologyVersion);
            return new(TopologyCommandStatus.Success, workspace.TopologyVersion);
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<Workspace?> LockWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        context.Database.IsRelational()
            ? await context.Workspaces.FromSqlInterpolated($"SELECT * FROM workspaces WHERE id = {workspaceId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            : await context.Workspaces.SingleOrDefaultAsync(item => item.Id == workspaceId, cancellationToken);

    private async Task LockMembershipAsync(Guid workspaceId, string userId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational()) return;
        _ = await context.WorkspaceMembers.FromSqlInterpolated(
                $"SELECT * FROM workspace_members WHERE workspace_id = {workspaceId} AND user_id = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task LockScopeInputsAsync(Guid workspaceId, WorkspaceAccessDto access, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational()) return;

        _ = await context.PortMappings.FromSqlInterpolated(
                $"SELECT * FROM port_mappings WHERE workspace_id = {workspaceId} FOR SHARE").IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
        if (access.Scope.Mode != WorkspaceScopeModes.Labels) return;

        _ = await context.ServerLabels.FromSqlInterpolated(
                $"SELECT * FROM server_labels WHERE workspace_id = {workspaceId} FOR SHARE").IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
        _ = await context.ApplicationLabels.FromSqlInterpolated(
                $"SELECT * FROM application_labels WHERE workspace_id = {workspaceId} FOR SHARE").IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<Guid>> ResolveAllowedNodeIdsAsync(
        Guid workspaceId,
        string actorId,
        WorkspaceAccessDto access,
        IReadOnlyDictionary<Guid, TopologyNode> nodes,
        CancellationToken cancellationToken)
    {
        if (access.Scope.Mode == WorkspaceScopeModes.All)
            return nodes.Keys.ToHashSet();

        if (access.Scope.Mode == WorkspaceScopeModes.Frames)
        {
            var roots = access.Scope.Frames.Select(item => item.Id).ToHashSet();
            return nodes.Values.Where(node => IsDescendant(node.Id, roots, nodes)).Select(node => node.Id).ToHashSet();
        }

        var serverIds = await resourcePolicy.GetReadableIdsAsync(workspaceId, actorId, "server", cancellationToken) ?? new HashSet<Guid>();
        var applicationIds = await resourcePolicy.GetReadableIdsAsync(workspaceId, actorId, "application", cancellationToken) ?? new HashSet<Guid>();
        var deploymentIds = await context.PortMappings.IgnoreQueryFilters()
            .Where(mapping => mapping.WorkspaceId == workspaceId && applicationIds.Contains(mapping.AppId) && serverIds.Contains(mapping.ServerId))
            .Select(mapping => mapping.Id)
            .ToListAsync(cancellationToken);
        return nodes.Values.Where(node =>
                node.NodeType.Equals("server", StringComparison.OrdinalIgnoreCase)
                    ? node.ReferenceId.HasValue && serverIds.Contains(node.ReferenceId.Value)
                    : node.NodeType.Equals("application", StringComparison.OrdinalIgnoreCase) &&
                      node.ReferenceId.HasValue && deploymentIds.Contains(node.ReferenceId.Value))
            .Select(node => node.Id).ToHashSet();
    }

    private async Task<(TopologyCommandStatus Status, string Error)?> ApplyAsync(
        TopologyCommandDto operation,
        string scopeMode,
        bool isAuditor,
        bool isScopedAuditor,
        HashSet<Guid> allowed,
        HashSet<Guid> grantedFrameRoots,
        IDictionary<Guid, TopologyNode> nodes,
        IDictionary<Guid, TopologyEdge> edges,
        CancellationToken cancellationToken)
    {
        var type = operation.Type?.Trim().ToLowerInvariant();
        return type switch
        {
            "movenode" or "updatenode" or "updatenodegeometry" => ApplyMove(operation, scopeMode, isAuditor, isScopedAuditor, allowed, grantedFrameRoots, nodes),
            "deletenode" => await ApplyDeleteNodeAsync(operation, isAuditor, allowed, nodes, edges, cancellationToken),
            "createedge" => await ApplyCreateEdgeAsync(operation, allowed, nodes, edges, cancellationToken),
            "updateedge" => await ApplyUpdateEdgeAsync(operation, allowed, nodes, edges, cancellationToken),
            "deleteedge" => await ApplyDeleteEdgeAsync(operation, allowed, nodes, edges, cancellationToken),
            _ => InvalidTuple("Unsupported topology command type.")
        };
    }

    private static (TopologyCommandStatus Status, string Error)? ApplyMove(
        TopologyCommandDto operation,
        string scopeMode,
        bool isAuditor,
        bool isScopedAuditor,
        HashSet<Guid> allowed,
        HashSet<Guid> grantedFrameRoots,
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
        if (isAuditor && (node.NodeType.Equals("frame", StringComparison.OrdinalIgnoreCase) ||
                          node.NodeType.Equals("group", StringComparison.OrdinalIgnoreCase)))
            return ForbiddenTuple("Auditors cannot modify frames or groups.");

        var geometryOnly = isAuditor && scopeMode == WorkspaceScopeModes.Labels;
        if (!geometryOnly && operation.ParentId.HasValue && (!nodes.ContainsKey(operation.ParentId.Value) ||
            isScopedAuditor && scopeMode == WorkspaceScopeModes.Frames && !allowed.Contains(operation.ParentId.Value)))
            return ForbiddenTuple();
        if (isScopedAuditor && scopeMode == WorkspaceScopeModes.Frames &&
            (!operation.ParentId.HasValue || !SharesGrantedRoot(
                node.Id,
                operation.ParentId.Value,
                grantedFrameRoots,
                nodes as IReadOnlyDictionary<Guid, TopologyNode> ?? nodes.ToDictionary(item => item.Key, item => item.Value))))
            return ForbiddenTuple("A node cannot be moved outside its granted frame subtree.");

        node.X = operation.X.Value;
        node.Y = operation.Y.Value;
        if (!geometryOnly) node.ParentNodeId = operation.ParentId;
        if (operation.Width.HasValue) node.Width = operation.Width;
        if (operation.Height.HasValue) node.Height = operation.Height;
        return null;
    }

    private async Task<(TopologyCommandStatus Status, string Error)?> ApplyDeleteNodeAsync(
        TopologyCommandDto operation,
        bool isAuditor,
        HashSet<Guid> allowed,
        IDictionary<Guid, TopologyNode> nodes,
        IDictionary<Guid, TopologyEdge> edges,
        CancellationToken cancellationToken)
    {
        if (!operation.NodeId.HasValue || !nodes.TryGetValue(operation.NodeId.Value, out var node) || !allowed.Contains(node.Id))
            return ForbiddenTuple();
        if (isAuditor && (node.NodeType.Equals("frame", StringComparison.OrdinalIgnoreCase) ||
                          node.NodeType.Equals("group", StringComparison.OrdinalIgnoreCase)))
            return ForbiddenTuple("Auditors cannot delete frames or groups.");

        var nodeSnapshot = nodes as IReadOnlyDictionary<Guid, TopologyNode> ?? nodes.ToDictionary(item => item.Key, item => item.Value);
        var subtree = nodes.Values.Where(candidate => IsDescendant(candidate.Id, new HashSet<Guid> { node.Id }, nodeSnapshot)).Select(candidate => candidate.Id).ToHashSet();
        if (subtree.Any(id => !allowed.Contains(id)))
            return ForbiddenTuple();
        var connected = edges.Values.Where(edge => subtree.Contains(edge.SourceNodeId) || subtree.Contains(edge.TargetNodeId)).ToList();
        if (isAuditor && (subtree.Count != 1 || connected.Count != 0))
            return ForbiddenTuple("Auditors can delete only workload leaf nodes without connections.");
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
            var workspaceId = tenant.WorkspaceId!.Value;
            var dependencies = await context.AppDependencies.IgnoreQueryFilters()
                .Where(item => item.WorkspaceId == workspaceId && references.Contains(item.Id)).ToListAsync(cancellationToken);
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
        CancellationToken cancellationToken)
    {
        if (!operation.EdgeId.HasValue || operation.EdgeId == Guid.Empty)
            return InvalidTuple("A unique edge id is required.");
        var endpoints = ValidateEndpoints(operation.SourceNodeId, operation.TargetNodeId, allowed, nodes);
        if (endpoints.Error is not null) return endpoints.Error;
        if (nodes.ContainsKey(operation.EdgeId.Value))
            return InvalidTuple("Node and edge ids must be distinct.");
        if (edges.TryGetValue(operation.EdgeId.Value, out var existingEdge))
            return allowed.Contains(existingEdge.SourceNodeId) && allowed.Contains(existingEdge.TargetNodeId)
                ? InvalidTuple("A unique edge id is required.")
                : ForbiddenTuple();
        var workspaceId = tenant.WorkspaceId!.Value;
        if (await context.AppDependencies.IgnoreQueryFilters().AnyAsync(
                item => item.WorkspaceId == workspaceId && item.Id == operation.EdgeId.Value,
                cancellationToken))
            return InvalidTuple("A unique edge id is required.");
        var dependency = await BuildDependencyAsync(operation.EdgeId.Value, endpoints.Source!, endpoints.Target!, null, cancellationToken);
        if (dependency.Error is not null) return dependency.Error;

        var edge = NewEdge(operation.EdgeId.Value, operation, dependency.Dependency!.Id);
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
                item => item.WorkspaceId == tenant.WorkspaceId!.Value && item.Id == edge.ReferenceId.Value, cancellationToken);
        var dependency = await BuildDependencyAsync(edge.ReferenceId ?? edge.Id, endpoints.Source!, endpoints.Target!, existingDependency, cancellationToken);
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
                item => item.WorkspaceId == tenant.WorkspaceId!.Value && item.Id == edge.ReferenceId.Value, cancellationToken);
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
        var workspaceId = tenant.WorkspaceId!.Value;
        var mappings = await context.PortMappings.IgnoreQueryFilters()
            .Where(item => item.WorkspaceId == workspaceId && mappingIds.Contains(item.Id))
            .Select(item => new { item.Id, item.AppId })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var dependency = await context.AppDependencies.IgnoreQueryFilters().SingleOrDefaultAsync(
            item => item.WorkspaceId == workspaceId && item.Id == edge.ReferenceId.Value,
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
        CancellationToken cancellationToken)
    {
        var mappingIds = new[] { source.ReferenceId!.Value, target.ReferenceId!.Value };
        var workspaceId = tenant.WorkspaceId!.Value;
        var mappings = await context.PortMappings.IgnoreQueryFilters().Where(item => item.WorkspaceId == workspaceId && mappingIds.Contains(item.Id))
            .Select(item => new { item.Id, item.AppId }).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (mappings.Count != 2) return (null, InvalidTuple("A deployment reference is invalid."));
        var sourceAppId = mappings[source.ReferenceId.Value].AppId;
        var targetAppId = mappings[target.ReferenceId.Value].AppId;
        if (sourceAppId == targetAppId) return (null, InvalidTuple("An application cannot depend on itself."));
        var duplicate = await context.AppDependencies.IgnoreQueryFilters().AnyAsync(item =>
            item.WorkspaceId == workspaceId && item.Id != dependencyId && item.SourceAppId == sourceAppId && item.DestAppId == targetAppId && item.DestPortId == target.ReferenceId.Value,
            cancellationToken);
        duplicate = duplicate || context.ChangeTracker.Entries<AppDependency>().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified &&
            entry.Entity.Id != dependencyId &&
            entry.Entity.SourceAppId == sourceAppId &&
            entry.Entity.DestAppId == targetAppId &&
            entry.Entity.DestPortId == target.ReferenceId.Value);
        if (duplicate) return (null, InvalidTuple("Duplicate dependencies are not allowed."));

        var dependency = existing ?? new AppDependency { Id = dependencyId, CreatedAt = DateTime.UtcNow };
        dependency.SourceAppId = sourceAppId;
        dependency.DestAppId = targetAppId;
        dependency.DestPortId = target.ReferenceId.Value;
        dependency.ConnectionType = "Manual";
        return (dependency, null);
    }

    private static TopologyEdge NewEdge(Guid id, TopologyCommandDto operation, Guid dependencyId) => new()
    {
        Id = id,
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

    private static bool SharesGrantedRoot(
        Guid nodeId,
        Guid parentId,
        IReadOnlySet<Guid> roots,
        IReadOnlyDictionary<Guid, TopologyNode> nodes) =>
        roots.Any(root => IsDescendant(nodeId, new HashSet<Guid> { root }, nodes) &&
                          IsDescendant(parentId, new HashSet<Guid> { root }, nodes));

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
