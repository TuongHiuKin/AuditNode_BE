using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class TopologyCommandServiceTests
{
    [Fact]
    public async Task FrameAuditor_CanMoveWorkloadInsideGrantedRoot()
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var workload = fixture.AddNode("server", frame.Id);
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "moveNode", NodeId = workload.Id, ParentId = frame.Id, X = 25, Y = 40 }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.Success);
        result.Version.Should().Be(1);
        fixture.Context.ChangeTracker.Clear();
        var saved = await fixture.Context.TopologyNodes.SingleAsync(item => item.Id == workload.Id);
        saved.X.Should().Be(25);
        saved.Y.Should().Be(40);
    }

    [Fact]
    public async Task FrameAuditor_CannotMoveWorkloadOutsideGrantedRoot()
    {
        await using var fixture = Fixture.ForFrames();
        var granted = fixture.AddNode("frame");
        var hidden = fixture.AddNode("frame");
        var workload = fixture.AddNode("server", granted.Id);
        fixture.GrantFrames(granted.Id);
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "moveNode", NodeId = workload.Id, ParentId = hidden.Id, X = 25, Y = 40 }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.Success);
        fixture.Context.ChangeTracker.Clear();
        (await fixture.Context.TopologyNodes.SingleAsync(item => item.Id == workload.Id)).ParentNodeId.Should().Be(granted.Id);
    }

    [Fact]
    public async Task RestrictedEndpoint_RejectsEntireBatchWithoutPersistingEarlierOperation()
    {
        await using var fixture = Fixture.ForFrames();
        var granted = fixture.AddNode("frame");
        var workload = fixture.AddNode("server", granted.Id);
        var hidden = fixture.AddNode("application");
        fixture.GrantFrames(granted.Id);
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "moveNode", NodeId = workload.Id, ParentId = granted.Id, X = 99, Y = 99 },
            new() { Type = "createEdge", EdgeId = Guid.NewGuid(), SourceNodeId = workload.Id, TargetNodeId = hidden.Id }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.InvalidRequest);
        fixture.Context.ChangeTracker.Clear();
        (await fixture.Context.TopologyNodes.SingleAsync(item => item.Id == workload.Id)).X.Should().Be(0);
        fixture.Context.TopologyEdges.Should().BeEmpty();
        (await fixture.Context.OwnerCatalogStates.SingleAsync()).TopologyVersion.Should().Be(0);
    }

    [Fact]
    public async Task StaleVersion_ReturnsConflictWithoutMutation()
    {
        await using var fixture = Fixture.ForFrames(topologyVersion: 4);
        var frame = fixture.AddNode("frame");
        var workload = fixture.AddNode("server", frame.Id);
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(3,
        [
            new() { Type = "moveNode", NodeId = workload.Id, ParentId = frame.Id, X = 10, Y = 10 }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.Conflict);
        result.Version.Should().Be(4);
    }

    [Fact]
    public async Task LabelAuditor_CannotDeleteNodeWithHiddenDescendant()
    {
        await using var fixture = Fixture.ForLabels();
        var visibleServerId = Guid.NewGuid();
        var visible = fixture.AddNode("server", referenceId: visibleServerId);
        _ = fixture.AddNode("application", visible.Id, Guid.NewGuid());
        fixture.AllowResources(new HashSet<Guid> { visibleServerId }, new HashSet<Guid>());
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "deleteNode", NodeId = visible.Id }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.Forbidden);
        fixture.Context.ChangeTracker.Clear();
        (await fixture.Context.TopologyNodes.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task LabelAuditor_ChangesGeometryButParentInputIsIgnored()
    {
        await using var fixture = Fixture.ForLabels();
        var originalFrame = fixture.AddNode("frame");
        var otherFrame = fixture.AddNode("frame");
        var serverId = Guid.NewGuid();
        var workload = fixture.AddNode("server", originalFrame.Id, serverId);
        fixture.AllowResources(new HashSet<Guid> { serverId }, new HashSet<Guid>());
        await fixture.SaveAsync();

        var geometry = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "moveNode", NodeId = workload.Id, ParentId = originalFrame.Id, X = 12, Y = 24 }
        ]));
        var reparent = await fixture.Service.ExecuteAsync(new(1,
        [
            new() { Type = "moveNode", NodeId = workload.Id, ParentId = otherFrame.Id, X = 30, Y = 40 }
        ]));

        geometry.Status.Should().Be(TopologyCommandStatus.Success);
        reparent.Status.Should().Be(TopologyCommandStatus.Success);
        fixture.Context.ChangeTracker.Clear();
        var saved = await fixture.Context.TopologyNodes.SingleAsync(item => item.Id == workload.Id);
        saved.ParentNodeId.Should().Be(originalFrame.Id);
        saved.X.Should().Be(30);
        saved.Y.Should().Be(40);
    }

    [Theory]
    [InlineData("frame")]
    [InlineData("group")]
    public async Task ScopedAuditor_CannotModifyGraphContainers(string nodeType)
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var target = nodeType == "frame" ? frame : fixture.AddNode("group", frame.Id);
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "moveNode", NodeId = target.Id, ParentId = target.ParentNodeId, X = 10, Y = 10 }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.Forbidden);
    }

    [Theory]
    [InlineData("frame")]
    [InlineData("group")]
    public async Task AllScopeAuditor_CannotModifyOrDeleteGraphContainers(string nodeType)
    {
        await using var fixture = Fixture.ForAll();
        var frame = fixture.AddNode("frame");
        var target = nodeType == "frame" ? frame : fixture.AddNode("group", frame.Id);
        await fixture.SaveAsync();

        var move = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "moveNode", NodeId = target.Id, ParentId = target.ParentNodeId, X = 10, Y = 10 }
        ]));
        var delete = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "deleteNode", NodeId = target.Id }
        ]));

        move.Status.Should().Be(TopologyCommandStatus.Forbidden);
        delete.Status.Should().Be(TopologyCommandStatus.Forbidden);
    }

    [Fact]
    public async Task ScopedAuditor_CannotDeleteWorkloadWithIncidentEdge()
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var source = fixture.AddNode("server", frame.Id);
        var target = fixture.AddNode("server", frame.Id);
        fixture.Context.TopologyEdges.Add(new TopologyEdge
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner",
            SourceNodeId = source.Id, TargetNodeId = target.Id, EdgeType = "default"
        });
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "deleteNode", NodeId = source.Id }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.Forbidden);
        fixture.Context.ChangeTracker.Clear();
        (await fixture.Context.TopologyNodes.AnyAsync(item => item.Id == source.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task BatchOverOperationLimit_IsRejectedWithoutMutation()
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var workload = fixture.AddNode("server", frame.Id);
        await fixture.SaveAsync();
        var operations = Enumerable.Range(0, 101).Select(index => new TopologyCommandDto
        {
            Type = "moveNode", NodeId = workload.Id, ParentId = frame.Id, X = index, Y = index
        }).ToList();

        var result = await fixture.Service.ExecuteAsync(new(0, operations));

        result.Status.Should().Be(TopologyCommandStatus.InvalidRequest);
        (await fixture.Context.OwnerCatalogStates.SingleAsync()).TopologyVersion.Should().Be(0);
    }

    [Fact]
    public async Task ConflictingEdgeLifecycle_IsRejectedWithoutOrphanDependency()
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var sourceMapping = Guid.NewGuid();
        var targetMapping = Guid.NewGuid();
        fixture.Context.PortMappings.AddRange(
            new PortMapping { Id = sourceMapping, OwnerUserId = "owner", AppId = Guid.NewGuid(), ServerId = Guid.NewGuid(), PortNumber = 2001 },
            new PortMapping { Id = targetMapping, OwnerUserId = "owner", AppId = Guid.NewGuid(), ServerId = Guid.NewGuid(), PortNumber = 2002 });
        var source = fixture.AddNode("application", frame.Id, sourceMapping);
        var target = fixture.AddNode("application", frame.Id, targetMapping);
        await fixture.SaveAsync();
        var edgeId = Guid.NewGuid();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "createEdge", EdgeId = edgeId, SourceNodeId = source.Id, TargetNodeId = target.Id },
            new() { Type = "deleteEdge", EdgeId = edgeId }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.InvalidRequest);
        fixture.Context.TopologyEdges.Should().BeEmpty();
        fixture.Context.AppDependencies.Should().BeEmpty();
    }

    [Fact]
    public async Task NullOperationAndMissingVersion_AreRejectedAsInvalidRequests()
    {
        await using var fixture = Fixture.ForFrames();

        var nullOperation = await fixture.Service.ExecuteAsync(new TopologyCommandBatchDto(0, [null]));
        var missingVersion = await fixture.Service.ExecuteAsync(new TopologyCommandBatchDto(null,
            [new TopologyCommandDto { Type = "moveNode" }]));

        nullOperation.Status.Should().Be(TopologyCommandStatus.InvalidRequest);
        missingVersion.Status.Should().Be(TopologyCommandStatus.InvalidRequest);
    }

    [Fact]
    public async Task EdgeIdCannotCollideWithNodeId()
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var source = fixture.AddNode("application", frame.Id, Guid.NewGuid());
        var target = fixture.AddNode("application", frame.Id, Guid.NewGuid());
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "createEdge", EdgeId = source.Id, SourceNodeId = source.Id, TargetNodeId = target.Id }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.InvalidRequest);
        fixture.Context.TopologyEdges.Should().BeEmpty();
    }

    [Fact]
    public async Task PoisonedDependencyReference_CannotBeUpdated()
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var sourceApp = Guid.NewGuid();
        var targetApp = Guid.NewGuid();
        var sourceMapping = Guid.NewGuid();
        var targetMapping = Guid.NewGuid();
        fixture.Context.PortMappings.AddRange(
            new PortMapping { Id = sourceMapping, OwnerUserId = "owner", AppId = sourceApp, ServerId = Guid.NewGuid(), PortNumber = 3001 },
            new PortMapping { Id = targetMapping, OwnerUserId = "owner", AppId = targetApp, ServerId = Guid.NewGuid(), PortNumber = 3002 });
        var source = fixture.AddNode("application", frame.Id, sourceMapping);
        var target = fixture.AddNode("application", frame.Id, targetMapping);
        var dependencyId = Guid.NewGuid();
        fixture.Context.AppDependencies.Add(new AppDependency
        {
            Id = dependencyId, OwnerUserId = "owner", SourceAppId = targetApp, DestAppId = sourceApp,
            DestPortId = sourceMapping, ConnectionType = "Poisoned"
        });
        fixture.Context.TopologyEdges.Add(new TopologyEdge
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner", SourceNodeId = source.Id, TargetNodeId = target.Id,
            EdgeType = "default", ReferenceId = dependencyId
        });
        await fixture.SaveAsync();
        var edgeId = await fixture.Context.TopologyEdges.Select(edge => edge.Id).SingleAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "updateEdge", EdgeId = edgeId, SourceNodeId = source.Id, TargetNodeId = target.Id }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.InvalidRequest);
        (await fixture.Context.AppDependencies.SingleAsync()).SourceAppId.Should().Be(targetApp);
    }

    [Fact]
    public async Task SharedDependencyReference_CannotBeDeletedThroughOneEdge()
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var sourceApp = Guid.NewGuid();
        var targetApp = Guid.NewGuid();
        var sourceMapping = Guid.NewGuid();
        var targetMapping = Guid.NewGuid();
        fixture.Context.PortMappings.AddRange(
            new PortMapping { Id = sourceMapping, OwnerUserId = "owner", AppId = sourceApp, ServerId = Guid.NewGuid(), PortNumber = 4001 },
            new PortMapping { Id = targetMapping, OwnerUserId = "owner", AppId = targetApp, ServerId = Guid.NewGuid(), PortNumber = 4002 });
        var source = fixture.AddNode("application", frame.Id, sourceMapping);
        var target = fixture.AddNode("application", frame.Id, targetMapping);
        var dependencyId = Guid.NewGuid();
        var firstEdgeId = Guid.NewGuid();
        fixture.Context.AppDependencies.Add(new AppDependency
        {
            Id = dependencyId, OwnerUserId = "owner", SourceAppId = sourceApp, DestAppId = targetApp,
            DestPortId = targetMapping, ConnectionType = "Manual"
        });
        fixture.Context.TopologyEdges.AddRange(
            new TopologyEdge
            {
                Id = firstEdgeId, OwnerUserId = "owner", SourceNodeId = source.Id, TargetNodeId = target.Id,
                SourceHandle = "one", EdgeType = "default", ReferenceId = dependencyId
            },
            new TopologyEdge
            {
                Id = Guid.NewGuid(), OwnerUserId = "owner", SourceNodeId = source.Id, TargetNodeId = target.Id,
                SourceHandle = "two", EdgeType = "default", ReferenceId = dependencyId
            });
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "deleteEdge", EdgeId = firstEdgeId }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.InvalidRequest);
        fixture.Context.TopologyEdges.Should().HaveCount(2);
        fixture.Context.AppDependencies.Should().ContainSingle(item => item.Id == dependencyId);
    }

    [Fact]
    public async Task FrameAuditor_CreatesTopologyEdgeAndDependencyAtomically()
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var sourceMapping = Guid.NewGuid();
        var targetMapping = Guid.NewGuid();
        var sourceApp = Guid.NewGuid();
        var targetApp = Guid.NewGuid();
        fixture.Context.PortMappings.AddRange(
            new PortMapping { Id = sourceMapping, OwnerUserId = "owner", AppId = sourceApp, ServerId = Guid.NewGuid(), PortNumber = 1001, Protocol = "TCP" },
            new PortMapping { Id = targetMapping, OwnerUserId = "owner", AppId = targetApp, ServerId = Guid.NewGuid(), PortNumber = 1002, Protocol = "TCP" });
        var source = fixture.AddNode("application", frame.Id, sourceMapping);
        var target = fixture.AddNode("application", frame.Id, targetMapping);
        await fixture.SaveAsync();
        var edgeId = Guid.NewGuid();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "createEdge", EdgeId = edgeId, SourceNodeId = source.Id, TargetNodeId = target.Id, EdgeType = "floatingSmooth", Label = "TCP" }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.Success);
        var edge = await fixture.Context.TopologyEdges.SingleAsync(item => item.Id == edgeId);
        edge.ReferenceId.Should().Be(edgeId);
        var dependency = await fixture.Context.AppDependencies.SingleAsync(item => item.Id == edgeId);
        dependency.SourceAppId.Should().Be(sourceApp);
        dependency.DestAppId.Should().Be(targetApp);
        dependency.DestPortId.Should().Be(targetMapping);
    }

    [Fact]
    public async Task CreateEdge_RejectsAnIdAlreadyOwnedByADependency()
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var sourceMapping = Guid.NewGuid();
        var targetMapping = Guid.NewGuid();
        fixture.Context.PortMappings.AddRange(
            new PortMapping { Id = sourceMapping, OwnerUserId = "owner", AppId = Guid.NewGuid(), ServerId = Guid.NewGuid(), PortNumber = 1101 },
            new PortMapping { Id = targetMapping, OwnerUserId = "owner", AppId = Guid.NewGuid(), ServerId = Guid.NewGuid(), PortNumber = 1102 });
        var source = fixture.AddNode("application", frame.Id, sourceMapping);
        var target = fixture.AddNode("application", frame.Id, targetMapping);
        var edgeId = Guid.NewGuid();
        fixture.Context.AppDependencies.Add(new AppDependency
        {
            Id = edgeId,
            OwnerUserId = "owner",
            SourceAppId = Guid.NewGuid(),
            DestAppId = Guid.NewGuid(),
            DestPortId = Guid.NewGuid(),
            ConnectionType = "Existing"
        });
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "createEdge", EdgeId = edgeId, SourceNodeId = source.Id, TargetNodeId = target.Id }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.InvalidRequest);
        fixture.Context.TopologyEdges.Should().BeEmpty();
        fixture.Context.AppDependencies.Should().ContainSingle(item => item.Id == edgeId);
    }

    [Fact]
    public async Task CreateEdge_rejects_endpoints_from_different_owners()
    {
        await using var fixture = Fixture.ForAll();
        var source = fixture.AddNode("application");
        var target = fixture.AddNode("application");
        target.OwnerUserId = "other-owner";
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new() { Type = "createEdge", EdgeId = Guid.NewGuid(), SourceNodeId = source.Id, TargetNodeId = target.Id }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.Forbidden);
        fixture.Context.TopologyEdges.Should().BeEmpty();
        fixture.Context.AppDependencies.Should().BeEmpty();
    }

    [Fact]
    public async Task Business_label_editor_cannot_create_an_edge_when_only_one_endpoint_is_granted()
    {
        await using var fixture = Fixture.ForAll();
        var visible = fixture.AddNode("application");
        var hidden = fixture.AddNode("application");
        fixture.GrantBusinessDeployments(visible.ReferenceId!.Value);
        await fixture.SaveAsync();

        var result = await fixture.Service.ExecuteAsync(new(0,
        [
            new()
            {
                Type = "createEdge", EdgeId = Guid.NewGuid(),
                SourceNodeId = visible.Id, TargetNodeId = hidden.Id
            }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.Forbidden);
        fixture.Context.TopologyEdges.Should().BeEmpty();
        fixture.Context.AppDependencies.Should().BeEmpty();
        (await fixture.Context.OwnerCatalogStates.SingleAsync()).TopologyVersion.Should().Be(0);
    }

    [Fact]
    public async Task Create_edge_rejects_topology_node_whose_owner_differs_from_its_deployment()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(item => item.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var context = new AuditDbContext(options);
        var sourceApp = Guid.NewGuid();
        var targetApp = Guid.NewGuid();
        var sourceServer = Guid.NewGuid();
        var targetServer = Guid.NewGuid();
        var sourceMapping = Guid.NewGuid();
        var targetMapping = Guid.NewGuid();
        var sourceNode = new TopologyNode
        {
            Id = Guid.NewGuid(), OwnerUserId = "other-owner",
            NodeType = "application", ReferenceId = sourceMapping
        };
        var targetNode = new TopologyNode
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner",
            NodeType = "application", ReferenceId = targetMapping
        };
        var ownerLabel = new Label
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner",
            Key = "Owner", Value = "owner", Kind = LabelKinds.Owner, IsProtected = true
        };
        context.AddRange(
            new AuditNode.Domain.Entities.Application { Id = sourceApp, OwnerUserId = "owner", AppCode = "SRC", AppName = "Source" },
            new AuditNode.Domain.Entities.Application { Id = targetApp, OwnerUserId = "owner", AppCode = "DST", AppName = "Target" },
            new Server { Id = sourceServer, OwnerUserId = "owner", DatacenterId = Guid.NewGuid(), Hostname = "source", IpAddress = "10.0.0.1" },
            new Server { Id = targetServer, OwnerUserId = "owner", DatacenterId = Guid.NewGuid(), Hostname = "target", IpAddress = "10.0.0.2" },
            new PortMapping { Id = sourceMapping, OwnerUserId = "owner", AppId = sourceApp, ServerId = sourceServer, PortNumber = 8001 },
            new PortMapping { Id = targetMapping, OwnerUserId = "owner", AppId = targetApp, ServerId = targetServer, PortNumber = 8002 },
            sourceNode, targetNode, ownerLabel,
            new LabelGrant
            {
                Id = Guid.NewGuid(), OwnerUserId = "owner", LabelId = ownerLabel.Id, GranteeUserId = "auditor",
                Permission = LabelGrantPermissions.Editor, Version = 1, CreatedByUserId = "owner"
            },
            new OwnerCatalogState { OwnerUserId = "owner" });
        await context.SaveChangesAsync();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(item => item.UserId).Returns("auditor");
        var service = new TopologyCommandService(
            context, new OwnerGraphAccessService(context, user.Object, TimeProvider.System), user.Object,
            NullLogger<TopologyCommandService>.Instance);

        var result = await service.ExecuteAsync(new(0,
        [
            new()
            {
                Type = "createEdge", EdgeId = Guid.NewGuid(),
                SourceNodeId = sourceNode.Id, TargetNodeId = targetNode.Id
            }
        ]));

        result.Status.Should().Be(TopologyCommandStatus.Forbidden);
        context.TopologyEdges.Should().BeEmpty();
        context.AppDependencies.Should().BeEmpty();
    }

    [Fact]
    public async Task Revoked_editor_grant_fails_closed_on_the_next_command()
    {
        await using var fixture = Fixture.ForAll();
        var workload = fixture.AddNode("server");
        await fixture.SaveAsync();

        var first = await fixture.Service.ExecuteAsync(new(0,
            [new() { Type = "moveNode", NodeId = workload.Id, X = 1, Y = 1 }]));
        var grant = await fixture.Context.LabelGrants.SingleAsync();
        grant.RevokedAt = DateTime.UtcNow;
        await fixture.Context.SaveChangesAsync();
        var second = await fixture.Service.ExecuteAsync(new(1,
            [new() { Type = "moveNode", NodeId = workload.Id, X = 2, Y = 2 }]));

        first.Status.Should().Be(TopologyCommandStatus.Success);
        second.Status.Should().Be(TopologyCommandStatus.Forbidden);
        (await fixture.Context.OwnerCatalogStates.SingleAsync()).TopologyVersion.Should().Be(1);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string Actor = "auditor";
        private Fixture(long topologyVersion)
        {
            var user = new Mock<ICurrentUserService>();
            user.SetupGet(item => item.UserId).Returns(Actor);
            var options = new DbContextOptionsBuilder<AuditDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(item => item.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            Context = new AuditDbContext(options);
            var ownerLabel = new Label
            {
                Id = Guid.NewGuid(), OwnerUserId = "owner",
                Key = "Owner", Value = "owner", Kind = LabelKinds.Owner, IsProtected = true
            };
            Context.Labels.Add(ownerLabel);
            Context.LabelGrants.Add(new LabelGrant
            {
                Id = Guid.NewGuid(), OwnerUserId = "owner", LabelId = ownerLabel.Id,
                GranteeUserId = Actor, Permission = LabelGrantPermissions.Editor,
                Version = 1, CreatedByUserId = "owner"
            });
            Context.OwnerCatalogStates.Add(new OwnerCatalogState
            {
                OwnerUserId = "owner", TopologyVersion = topologyVersion
            });
            Service = new TopologyCommandService(
                Context, new OwnerGraphAccessService(Context, user.Object, TimeProvider.System), user.Object,
                NullLogger<TopologyCommandService>.Instance);
        }

        public AuditDbContext Context { get; }
        public TopologyCommandService Service { get; }

        public static Fixture ForFrames(long topologyVersion = 0) => new(topologyVersion);
        public static Fixture ForLabels(long topologyVersion = 0) => new(topologyVersion);
        public static Fixture ForAll(long topologyVersion = 0) => new(topologyVersion);

        public TopologyNode AddNode(string type, Guid? parentId = null, Guid? referenceId = null)
        {
            if (type.Equals("server", StringComparison.OrdinalIgnoreCase) && !referenceId.HasValue)
            {
                referenceId = Guid.NewGuid();
                Context.Servers.Add(new Server
                {
                    Id = referenceId.Value, OwnerUserId = "owner",
                    DatacenterId = Guid.NewGuid(), Hostname = "server", IpAddress = $"10.0.0.{Context.Servers.Local.Count + 1}",
                    OsType = "Linux", Environment = "test", Status = "up"
                });
            }
            if (type.Equals("application", StringComparison.OrdinalIgnoreCase) && !referenceId.HasValue)
            {
                var appId = Guid.NewGuid();
                var serverId = Guid.NewGuid();
                referenceId = Guid.NewGuid();
                Context.Applications.Add(new AuditNode.Domain.Entities.Application
                {
                    Id = appId, OwnerUserId = "owner", AppCode = $"APP-{appId:N}", AppName = "app"
                });
                Context.Servers.Add(new Server
                {
                    Id = serverId, OwnerUserId = "owner", DatacenterId = Guid.NewGuid(),
                    Hostname = "app-host", IpAddress = $"10.1.0.{Context.Servers.Local.Count + 1}", OsType = "Linux", Environment = "test", Status = "up"
                });
                Context.PortMappings.Add(new PortMapping
                {
                    Id = referenceId.Value, OwnerUserId = "owner",
                    AppId = appId, ServerId = serverId, PortNumber = 8000 + Context.PortMappings.Local.Count, Protocol = "TCP"
                });
            }
            var node = new TopologyNode
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "owner",
                NodeType = type,
                Label = type,
                ParentNodeId = parentId,
                ReferenceId = referenceId,
                X = 0,
                Y = 0
            };
            Context.TopologyNodes.Add(node);
            return node;
        }

        public void GrantFrames(params Guid[] frameIds)
        {
            _ = frameIds;
        }

        public void AllowResources(IReadOnlySet<Guid> servers, IReadOnlySet<Guid> applications)
        {
            foreach (var id in servers.Where(id => !Context.Servers.Local.Any(item => item.Id == id)))
                Context.Servers.Add(new Server
                {
                    Id = id, OwnerUserId = "owner", DatacenterId = Guid.NewGuid(),
                    Hostname = "allowed", IpAddress = $"10.2.0.{Context.Servers.Local.Count + 1}", OsType = "Linux", Environment = "test", Status = "up"
                });
            foreach (var id in applications.Where(id => !Context.Applications.Local.Any(item => item.Id == id)))
                Context.Applications.Add(new AuditNode.Domain.Entities.Application
                {
                    Id = id, OwnerUserId = "owner", AppCode = $"APP-{id:N}", AppName = "allowed"
                });
        }

        public void GrantBusinessDeployments(params Guid[] deploymentIds)
        {
            Context.LabelGrants.RemoveRange(Context.LabelGrants.Local);
            Context.Labels.RemoveRange(Context.Labels.Local.Where(item => item.Kind == LabelKinds.Owner));
            var label = new Label
            {
                Id = Guid.NewGuid(), OwnerUserId = "owner",
                Key = "scope", Value = "graph-editor", Kind = LabelKinds.Business
            };
            Context.Labels.Add(label);
            Context.LabelGrants.Add(new LabelGrant
            {
                Id = Guid.NewGuid(), OwnerUserId = "owner", LabelId = label.Id, Label = label,
                GranteeUserId = Actor, Permission = LabelGrantPermissions.Editor,
                Version = 1, CreatedByUserId = "owner"
            });
            var mappings = Context.PortMappings.Local.Where(item => deploymentIds.Contains(item.Id)).ToList();
            foreach (var mapping in mappings)
            {
                Context.ApplicationLabels.Add(new ApplicationLabel
                {
                    OwnerUserId = "owner",
                    ApplicationId = mapping.AppId, LabelId = label.Id, Label = label
                });
                Context.ServerLabels.Add(new ServerLabel
                {
                    OwnerUserId = "owner",
                    ServerId = mapping.ServerId, LabelId = label.Id, Label = label
                });
            }
        }

        public Task SaveAsync()
        {
            var mappings = Context.ChangeTracker.Entries<PortMapping>()
                .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity).ToList();
            foreach (var mapping in mappings)
            {
                if (!Context.Applications.Local.Any(item => item.Id == mapping.AppId))
                    Context.Applications.Add(new AuditNode.Domain.Entities.Application
                    {
                        Id = mapping.AppId, OwnerUserId = "owner",
                        AppCode = $"APP-{mapping.AppId:N}", AppName = "mapped"
                    });
                if (!Context.Servers.Local.Any(item => item.Id == mapping.ServerId))
                    Context.Servers.Add(new Server
                    {
                        Id = mapping.ServerId, OwnerUserId = "owner", DatacenterId = Guid.NewGuid(),
                        Hostname = "mapped-host", IpAddress = $"10.3.0.{Context.Servers.Local.Count + 1}",
                        OsType = "Linux", Environment = "test", Status = "up"
                    });
            }
            foreach (var entry in Context.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
            {
                switch (entry.Entity)
                {
                    case TopologyEdge edge: edge.OwnerUserId = "owner"; break;
                    case AppDependency dependency: dependency.OwnerUserId = "owner"; break;
                    case PortMapping mapping: mapping.OwnerUserId = "owner"; break;
                    case AuditNode.Domain.Entities.Application app: app.OwnerUserId = "owner"; break;
                    case Server server: server.OwnerUserId = "owner"; break;
                }
            }
            return Context.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
