using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class PostgresTopologyMutationConcurrencyTests
{
    private const string Owner = "topology-owner";
    private const string Auditor = "topology-auditor";

    [PostgresIntegrationFact]
    public async Task Concurrent_command_batches_are_serialized_by_workspace_version()
    {
        var workspaceId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        await SeedAsync(workspaceId, new TopologyNode { Id = nodeId, NodeType = "group", Label = "group" });
        await using var first = Context(workspaceId);
        await using var second = Context(workspaceId);

        var results = await Task.WhenAll(
            CommandService(first, workspaceId, Owner).ExecuteAsync(Move(0, nodeId, null, 10)),
            CommandService(second, workspaceId, Owner).ExecuteAsync(Move(0, nodeId, null, 20)));

        results.Select(result => result.Status).Should().BeEquivalentTo(
            [TopologyCommandStatus.Success, TopologyCommandStatus.Conflict]);
        await using var verification = Context(workspaceId);
        (await verification.Workspaces.SingleAsync(item => item.Id == workspaceId)).TopologyVersion.Should().Be(1);
        (await verification.TopologyNodes.SingleAsync(item => item.Id == nodeId)).X.Should().BeOneOf(10, 20);
    }

    [PostgresIntegrationFact]
    public async Task Full_save_and_command_share_one_revision_protocol()
    {
        var workspaceId = Guid.NewGuid();
        var graph = await SeedDependencyGraphAsync(workspaceId);
        await using var fullSaveContext = Context(workspaceId);
        await using var commandContext = Context(workspaceId);
        var repository = Repository(fullSaveContext, workspaceId, Owner);
        var fullState = new SaveTopologyStateDto
        {
            Version = 0,
            Nodes =
            [
                new TopologyNodeDto { Id = graph.SourceNodeId, NodeType = "application", Label = "source", ReferenceId = graph.SourceMappingId },
                new TopologyNodeDto { Id = graph.TargetNodeId, NodeType = "application", Label = "target", ReferenceId = graph.TargetMappingId }
            ],
            Edges = [],
            Dependencies = []
        };
        var edgeId = Guid.NewGuid();

        var fullSaveTask = repository.SaveTopologyStateAsync(fullState);
        var commandTask = CommandService(commandContext, workspaceId, Owner).ExecuteAsync(new TopologyCommandBatchDto(0,
        [
            new TopologyCommandDto
            {
                Type = "createEdge", EdgeId = edgeId,
                SourceNodeId = graph.SourceNodeId, TargetNodeId = graph.TargetNodeId
            }
        ]));
        await Task.WhenAll(fullSaveTask, commandTask);

        var successCount = (fullSaveTask.Result == TopologyStateStatus.Success ? 1 : 0) +
                           (commandTask.Result.Status == TopologyCommandStatus.Success ? 1 : 0);
        var conflictCount = (fullSaveTask.Result == TopologyStateStatus.Conflict ? 1 : 0) +
                            (commandTask.Result.Status == TopologyCommandStatus.Conflict ? 1 : 0);
        successCount.Should().Be(1);
        conflictCount.Should().Be(1);
        await using var verification = Context(workspaceId);
        (await verification.Workspaces.SingleAsync(item => item.Id == workspaceId)).TopologyVersion.Should().Be(1);
        var edgeCount = await verification.TopologyEdges.CountAsync(item => item.Id == edgeId);
        var dependencyCount = await verification.AppDependencies.CountAsync(item => item.Id == edgeId);
        edgeCount.Should().Be(dependencyCount);
    }

    [PostgresIntegrationFact]
    public async Task Concurrent_revoke_and_command_are_linearized_and_post_revoke_command_fails_closed()
    {
        var workspaceId = Guid.NewGuid();
        var frameId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        await SeedAsync(
            workspaceId,
            new TopologyNode { Id = frameId, NodeType = "frame", Label = "frame" },
            new TopologyNode { Id = nodeId, NodeType = "server", Label = "server", ParentNodeId = frameId },
            scopedAuditorFrameId: frameId);
        await using var commandContext = Context(workspaceId);
        await using var revokeContext = Context(workspaceId);
        var commandTask = CommandService(commandContext, workspaceId, Auditor).ExecuteAsync(Move(0, nodeId, frameId, 50));
        var revokeTask = SharingService(revokeContext).RevokeAsync(workspaceId, Owner, Auditor, 1);

        await Task.WhenAll(commandTask, revokeTask);

        revokeTask.Result.Success.Should().BeTrue();
        commandTask.Result.Status.Should().BeOneOf(TopologyCommandStatus.Success, TopologyCommandStatus.Forbidden);
        await using var verification = Context(workspaceId);
        (await verification.WorkspaceMembers.IgnoreQueryFilters()
            .AnyAsync(item => item.WorkspaceId == workspaceId && item.UserId == Auditor)).Should().BeFalse();
        var currentVersion = await verification.Workspaces.Where(item => item.Id == workspaceId)
            .Select(item => item.TopologyVersion).SingleAsync();
        await using var postRevokeContext = Context(workspaceId);
        var postRevoke = await CommandService(postRevokeContext, workspaceId, Auditor)
            .ExecuteAsync(Move(currentVersion, nodeId, frameId, 60));
        postRevoke.Status.Should().Be(TopologyCommandStatus.Forbidden);
    }

    [PostgresIntegrationFact]
    public async Task Concurrent_admin_revoke_and_full_save_are_linearized_and_post_revoke_save_is_forbidden()
    {
        const string admin = "topology-admin";
        var workspaceId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        await SeedAsync(workspaceId, new TopologyNode { Id = nodeId, NodeType = "group", Label = "group" });
        await using (var memberContext = Context(workspaceId))
        {
            memberContext.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspaceId, UserId = admin, Role = WorkspaceRoles.Admin,
                ScopeMode = WorkspaceScopeModes.All, Version = 1, InvitedByUserId = Owner
            });
            await memberContext.SaveChangesAsync();
        }
        await using var saveContext = Context(workspaceId);
        await using var revokeContext = Context(workspaceId);
        var state = new SaveTopologyStateDto
        {
            Version = 0,
            Nodes = [new TopologyNodeDto { Id = nodeId, NodeType = "group", Label = "admin-save", X = 5, Y = 5 }],
            Edges = [],
            Dependencies = []
        };
        var saveTask = Repository(saveContext, workspaceId, admin).SaveTopologyStateAsync(state);
        var revokeTask = SharingService(revokeContext).RevokeAsync(workspaceId, Owner, admin, 1);

        await Task.WhenAll(saveTask, revokeTask);

        revokeTask.Result.Success.Should().BeTrue();
        saveTask.Result.Should().BeOneOf(TopologyStateStatus.Success, TopologyStateStatus.Forbidden);
        await using var verification = Context(workspaceId);
        var version = await verification.Workspaces.Where(item => item.Id == workspaceId)
            .Select(item => item.TopologyVersion).SingleAsync();
        await using var postRevokeContext = Context(workspaceId);
        var postRevoke = await Repository(postRevokeContext, workspaceId, admin).SaveTopologyStateAsync(new SaveTopologyStateDto
        {
            Version = version,
            Nodes = [new TopologyNodeDto { Id = nodeId, NodeType = "group", Label = "forbidden", X = 9, Y = 9 }],
            Edges = [],
            Dependencies = []
        });
        postRevoke.Should().Be(TopologyStateStatus.Forbidden);
    }

    private static TopologyCommandBatchDto Move(long version, Guid nodeId, Guid? parentId, double x) =>
        new(version, [new TopologyCommandDto { Type = "moveNode", NodeId = nodeId, ParentId = parentId, X = x, Y = x }]);

    private static async Task SeedAsync(
        Guid workspaceId,
        TopologyNode first,
        TopologyNode? second = null,
        Guid? scopedAuditorFrameId = null)
    {
        await using var context = Context(workspaceId);
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Topology concurrency", OwnerUserId = Owner });
        first.WorkspaceId = workspaceId;
        context.TopologyNodes.Add(first);
        if (second is not null)
        {
            second.WorkspaceId = workspaceId;
            context.TopologyNodes.Add(second);
        }
        if (scopedAuditorFrameId.HasValue)
        {
            context.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspaceId,
                UserId = Auditor,
                Role = WorkspaceRoles.Auditor,
                ScopeMode = WorkspaceScopeModes.Frames,
                Version = 1,
                InvitedByUserId = Owner,
                Scopes =
                [
                    new WorkspaceMemberScope
                    {
                        Id = Guid.NewGuid(),
                        WorkspaceId = workspaceId,
                        UserId = Auditor,
                        ScopeType = WorkspaceScopeTypes.Frame,
                        TargetId = scopedAuditorFrameId.Value,
                        CreatedByUserId = Owner
                    }
                ]
            });
        }
        await context.SaveChangesAsync();
    }

    private static async Task<(Guid SourceNodeId, Guid TargetNodeId, Guid SourceMappingId, Guid TargetMappingId)>
        SeedDependencyGraphAsync(Guid workspaceId)
    {
        await using var context = Context(workspaceId);
        var datacenter = new Datacenter { Id = Guid.NewGuid(), Name = $"dc-{workspaceId:N}", Location = "test" };
        var sourceServer = new Server
        {
            Id = Guid.NewGuid(), DatacenterId = datacenter.Id, Hostname = $"source-{workspaceId:N}",
            IpAddress = $"10.{workspaceId.ToByteArray()[0]}.0.1", OsType = "Linux", Environment = "Test", Status = "Up"
        };
        var targetServer = new Server
        {
            Id = Guid.NewGuid(), DatacenterId = datacenter.Id, Hostname = $"target-{workspaceId:N}",
            IpAddress = $"10.{workspaceId.ToByteArray()[0]}.0.2", OsType = "Linux", Environment = "Test", Status = "Up"
        };
        var sourceApp = new AuditNode.Domain.Entities.Application
        {
            Id = Guid.NewGuid(), AppCode = $"SRC-{workspaceId:N}", AppName = "source", OwnerTeam = "test", Risk = "low"
        };
        var targetApp = new AuditNode.Domain.Entities.Application
        {
            Id = Guid.NewGuid(), AppCode = $"DST-{workspaceId:N}", AppName = "target", OwnerTeam = "test", Risk = "low"
        };
        var sourceMapping = new PortMapping
        {
            Id = Guid.NewGuid(), ServerId = sourceServer.Id, AppId = sourceApp.Id, PortNumber = 8101, Protocol = "TCP"
        };
        var targetMapping = new PortMapping
        {
            Id = Guid.NewGuid(), ServerId = targetServer.Id, AppId = targetApp.Id, PortNumber = 8102, Protocol = "TCP"
        };
        var sourceNode = new TopologyNode
        {
            Id = Guid.NewGuid(), NodeType = "application", Label = "source", ReferenceId = sourceMapping.Id
        };
        var targetNode = new TopologyNode
        {
            Id = Guid.NewGuid(), NodeType = "application", Label = "target", ReferenceId = targetMapping.Id
        };
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Dependency race", OwnerUserId = Owner });
        context.AddRange(datacenter, sourceServer, targetServer, sourceApp, targetApp, sourceMapping, targetMapping, sourceNode, targetNode);
        await context.SaveChangesAsync();
        return (sourceNode.Id, targetNode.Id, sourceMapping.Id, targetMapping.Id);
    }

    private static TopologyCommandService CommandService(AuditDbContext context, Guid workspaceId, string userId)
    {
        var access = new WorkspaceAccessService(context);
        var tenant = Tenant(workspaceId);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(item => item.UserId).Returns(userId);
        return new TopologyCommandService(
            context,
            access,
            new ScopedResourcePolicy(context, access),
            user.Object,
            tenant.Object,
            NullLogger<TopologyCommandService>.Instance);
    }

    private static TopologyRepository Repository(AuditDbContext context, Guid workspaceId, string userId)
    {
        var access = new WorkspaceAccessService(context);
        var tenant = Tenant(workspaceId);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(item => item.UserId).Returns(userId);
        return new TopologyRepository(context, new ScopedResourcePolicy(context, access), user.Object, tenant.Object, access);
    }

    private static WorkspaceSharingService SharingService(AuditDbContext context)
    {
        var identities = new Mock<IIdentityAdminService>();
        return new WorkspaceSharingService(context, NullLogger<WorkspaceSharingService>.Instance, identities.Object);
    }

    private static AuditDbContext Context(Guid workspaceId)
    {
        var tenant = Tenant(workspaceId);
        return new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(ConnectionString()).Options,
            tenant.Object);
    }

    private static Mock<ITenantProvider> Tenant(Guid workspaceId)
    {
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(item => item.WorkspaceId).Returns(workspaceId);
        return tenant;
    }

    private static string ConnectionString() => Environment.GetEnvironmentVariable("AUDITNODE_TEST_POSTGRES")!;
}
