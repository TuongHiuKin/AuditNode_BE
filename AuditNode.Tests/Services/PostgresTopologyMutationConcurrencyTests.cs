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
    public async Task Concurrent_command_batches_are_serialized_by_owner_version()
    {
        var workspaceId = Guid.NewGuid();
        var owner = OwnerFor(workspaceId);
        var nodeId = Guid.NewGuid();
        await SeedAsync(workspaceId, new TopologyNode { Id = nodeId, NodeType = "group", Label = "group" });
        await using var first = Context(workspaceId);
        await using var second = Context(workspaceId);

        var results = await Task.WhenAll(
            CommandService(first, workspaceId, owner).ExecuteAsync(Move(0, nodeId, null, 10)),
            CommandService(second, workspaceId, owner).ExecuteAsync(Move(0, nodeId, null, 20)));

        results.Select(result => result.Status).Should().BeEquivalentTo(
            [TopologyCommandStatus.Success, TopologyCommandStatus.Conflict]);
        await using var verification = Context(workspaceId);
        (await verification.OwnerCatalogStates.SingleAsync(item => item.OwnerUserId == owner)).TopologyVersion.Should().Be(1);
        (await verification.TopologyNodes.SingleAsync(item => item.Id == nodeId)).X.Should().BeOneOf(10, 20);
    }

    [PostgresIntegrationFact]
    public async Task Full_save_and_command_share_one_revision_protocol()
    {
        var workspaceId = Guid.NewGuid();
        var owner = OwnerFor(workspaceId);
        var graph = await SeedDependencyGraphAsync(workspaceId);
        await using var fullSaveContext = Context(workspaceId);
        await using var commandContext = Context(workspaceId);
        var repository = Repository(fullSaveContext, workspaceId, owner);
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
        var commandTask = CommandService(commandContext, workspaceId, owner).ExecuteAsync(new TopologyCommandBatchDto(0,
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
        (await verification.OwnerCatalogStates.SingleAsync(item => item.OwnerUserId == owner)).TopologyVersion.Should().Be(1);
        var edgeCount = await verification.TopologyEdges.CountAsync(item => item.Id == edgeId);
        var dependencyCount = await verification.AppDependencies.CountAsync(item => item.Id == edgeId);
        edgeCount.Should().Be(dependencyCount);
    }

    [PostgresIntegrationFact]
    public async Task Concurrent_revoke_and_command_are_linearized_and_post_revoke_command_fails_closed()
    {
        var workspaceId = Guid.NewGuid();
        var owner = OwnerFor(workspaceId);
        var frameId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var grant = await SeedAsync(
            workspaceId,
            new TopologyNode { Id = frameId, NodeType = "frame", Label = "frame" },
            new TopologyNode { Id = nodeId, NodeType = "server", Label = "server", ParentNodeId = frameId },
            scopedAuditorFrameId: frameId);
        await using var commandContext = Context(workspaceId);
        await using var revokeContext = Context(workspaceId);
        await using var revokeTransaction = await revokeContext.Database.BeginTransactionAsync();
        var lockedGrant = await revokeContext.LabelGrants.FromSqlInterpolated(
                $"SELECT * FROM label_grants WHERE id = {grant.GrantId!.Value} FOR UPDATE")
            .IgnoreQueryFilters().SingleAsync();
        lockedGrant.RevokedAt = DateTime.UtcNow;
        lockedGrant.UpdatedAt = DateTime.UtcNow;
        lockedGrant.Version++;
        await revokeContext.SaveChangesAsync();

        var commandTask = CommandService(commandContext, workspaceId, Auditor)
            .ExecuteAsync(Move(0, nodeId, frameId, 50));
        var completedBeforeCommit = await Task.WhenAny(commandTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
        completedBeforeCommit.Should().NotBe(commandTask, "the command must wait for the grant-row revoke lock");

        await revokeTransaction.CommitAsync();
        var commandResult = await commandTask;

        commandResult.Status.Should().Be(TopologyCommandStatus.Forbidden);
        await using var verification = Context(workspaceId);
        (await verification.LabelGrants.IgnoreQueryFilters()
            .AnyAsync(item => item.Id == grant.GrantId && item.RevokedAt == null)).Should().BeFalse();
        var currentVersion = await verification.OwnerCatalogStates.Where(item => item.OwnerUserId == owner)
            .Select(item => item.TopologyVersion).SingleAsync();
        currentVersion.Should().Be(0);
        await using var postRevokeContext = Context(workspaceId);
        var postRevoke = await CommandService(postRevokeContext, workspaceId, Auditor)
            .ExecuteAsync(Move(currentVersion, nodeId, frameId, 60));
        postRevoke.Status.Should().Be(TopologyCommandStatus.Forbidden);
    }

    [PostgresIntegrationFact]
    public async Task Editor_cannot_use_owner_full_save_even_with_an_active_grant()
    {
        var workspaceId = Guid.NewGuid();
        var owner = OwnerFor(workspaceId);
        var nodeId = Guid.NewGuid();
        await SeedAsync(workspaceId, new TopologyNode { Id = nodeId, NodeType = "group", Label = "group" }, scopedAuditorFrameId: nodeId);
        await using var saveContext = Context(workspaceId);
        var state = new SaveTopologyStateDto
        {
            Version = 0,
            Nodes = [new TopologyNodeDto { Id = nodeId, NodeType = "group", Label = "admin-save", X = 5, Y = 5 }],
            Edges = [],
            Dependencies = []
        };
        var result = await Repository(saveContext, workspaceId, Auditor).SaveTopologyStateAsync(state);
        result.Should().Be(TopologyStateStatus.Forbidden);
    }

    private static TopologyCommandBatchDto Move(long version, Guid nodeId, Guid? parentId, double x) =>
        new(version, [new TopologyCommandDto { Type = "moveNode", NodeId = nodeId, ParentId = parentId, X = x, Y = x }]);

    private static async Task<(Guid? LabelId, Guid? GrantId)> SeedAsync(
        Guid workspaceId,
        TopologyNode first,
        TopologyNode? second = null,
        Guid? scopedAuditorFrameId = null)
    {
        await using var context = Context(workspaceId);
        var owner = OwnerFor(workspaceId);
        context.OwnerCatalogStates.Add(new OwnerCatalogState { OwnerUserId = owner });
        first.OwnerUserId = owner;
        context.TopologyNodes.Add(first);
        AddServerReferenceIfNeeded(context, workspaceId, owner, first);
        if (second is not null)
        {
            second.OwnerUserId = owner;
            context.TopologyNodes.Add(second);
            AddServerReferenceIfNeeded(context, workspaceId, owner, second);
        }
        if (scopedAuditorFrameId.HasValue)
        {
            var label = new Label
            {
                Id = Guid.NewGuid(), OwnerUserId = owner,
                Key = "Owner", Value = owner, Kind = LabelKinds.Owner, IsProtected = true
            };
            var grant = new LabelGrant
            {
                Id = Guid.NewGuid(), OwnerUserId = owner, LabelId = label.Id, GranteeUserId = Auditor,
                Permission = LabelGrantPermissions.Editor, Version = 1, CreatedByUserId = owner
            };
            context.AddRange(label, grant);
            await context.SaveChangesAsync();
            return (label.Id, grant.Id);
        }
        await context.SaveChangesAsync();
        return (null, null);
    }

    private static void AddServerReferenceIfNeeded(AuditDbContext context, Guid workspaceId, string owner, TopologyNode node)
    {
        if (!node.NodeType.Equals("server", StringComparison.OrdinalIgnoreCase) || node.ReferenceId.HasValue) return;
        var datacenter = new Datacenter
        {
            Id = Guid.NewGuid(), OwnerUserId = owner,
            Name = $"dc-{node.Id:N}", Location = "test"
        };
        var server = new Server
        {
            Id = Guid.NewGuid(), OwnerUserId = owner, DatacenterId = datacenter.Id,
            Hostname = $"host-{node.Id:N}", IpAddress = $"10.{node.Id.ToByteArray()[0]}.{node.Id.ToByteArray()[1]}.1",
            OsType = "Linux", Environment = "test", Status = "up"
        };
        node.ReferenceId = server.Id;
        context.AddRange(datacenter, server);
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
        var owner = OwnerFor(workspaceId);
        datacenter.OwnerUserId = owner;
        sourceServer.OwnerUserId = owner;
        targetServer.OwnerUserId = owner;
        sourceApp.OwnerUserId = owner;
        targetApp.OwnerUserId = owner;
        sourceMapping.OwnerUserId = owner;
        targetMapping.OwnerUserId = owner;
        sourceNode.OwnerUserId = owner;
        targetNode.OwnerUserId = owner;
        context.OwnerCatalogStates.Add(new OwnerCatalogState { OwnerUserId = owner });
        context.AddRange(datacenter, sourceServer, targetServer, sourceApp, targetApp, sourceMapping, targetMapping, sourceNode, targetNode);
        await context.SaveChangesAsync();
        return (sourceNode.Id, targetNode.Id, sourceMapping.Id, targetMapping.Id);
    }

    private static TopologyCommandService CommandService(AuditDbContext context, Guid workspaceId, string userId)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(item => item.UserId).Returns(userId);
        return new TopologyCommandService(
            context, new OwnerGraphAccessService(context, user.Object, TimeProvider.System), user.Object,
            NullLogger<TopologyCommandService>.Instance);
    }

    private static TopologyRepository Repository(AuditDbContext context, Guid workspaceId, string userId)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(item => item.UserId).Returns(userId);
        return new TopologyRepository(context, user.Object,
            new OwnerGraphAccessService(context, user.Object, TimeProvider.System));
    }

    private static LabelGrantService LabelGrantService(AuditDbContext context, string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(item => item.UserId).Returns(userId);
        var identities = new Mock<IIdentityAdminService>();
        return new LabelGrantService(context, currentUser.Object, identities.Object, TimeProvider.System,
            NullLogger<LabelGrantService>.Instance);
    }

    private static AuditDbContext Context(Guid workspaceId)
    {
        return new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(ConnectionString()).Options);
    }

    private static string ConnectionString() => Environment.GetEnvironmentVariable("AUDITNODE_TEST_POSTGRES")!;

    private static string OwnerFor(Guid workspaceId) => $"{Owner}-{workspaceId:N}";
}
