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

        result.Status.Should().Be(TopologyCommandStatus.Forbidden);
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

        result.Status.Should().Be(TopologyCommandStatus.Forbidden);
        fixture.Context.ChangeTracker.Clear();
        (await fixture.Context.TopologyNodes.SingleAsync(item => item.Id == workload.Id)).X.Should().Be(0);
        fixture.Context.TopologyEdges.Should().BeEmpty();
        (await fixture.Context.Workspaces.SingleAsync()).TopologyVersion.Should().Be(0);
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
            Id = Guid.NewGuid(), SourceNodeId = source.Id, TargetNodeId = target.Id, EdgeType = "default"
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
        (await fixture.Context.Workspaces.SingleAsync()).TopologyVersion.Should().Be(0);
    }

    [Fact]
    public async Task ConflictingEdgeLifecycle_IsRejectedWithoutOrphanDependency()
    {
        await using var fixture = Fixture.ForFrames();
        var frame = fixture.AddNode("frame");
        var sourceMapping = Guid.NewGuid();
        var targetMapping = Guid.NewGuid();
        fixture.Context.PortMappings.AddRange(
            new PortMapping { Id = sourceMapping, AppId = Guid.NewGuid(), ServerId = Guid.NewGuid(), PortNumber = 2001 },
            new PortMapping { Id = targetMapping, AppId = Guid.NewGuid(), ServerId = Guid.NewGuid(), PortNumber = 2002 });
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
            new PortMapping { Id = sourceMapping, AppId = sourceApp, ServerId = Guid.NewGuid(), PortNumber = 3001 },
            new PortMapping { Id = targetMapping, AppId = targetApp, ServerId = Guid.NewGuid(), PortNumber = 3002 });
        var source = fixture.AddNode("application", frame.Id, sourceMapping);
        var target = fixture.AddNode("application", frame.Id, targetMapping);
        var dependencyId = Guid.NewGuid();
        fixture.Context.AppDependencies.Add(new AppDependency
        {
            Id = dependencyId, SourceAppId = targetApp, DestAppId = sourceApp,
            DestPortId = sourceMapping, ConnectionType = "Poisoned"
        });
        fixture.Context.TopologyEdges.Add(new TopologyEdge
        {
            Id = Guid.NewGuid(), SourceNodeId = source.Id, TargetNodeId = target.Id,
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
            new PortMapping { Id = sourceMapping, AppId = sourceApp, ServerId = Guid.NewGuid(), PortNumber = 4001 },
            new PortMapping { Id = targetMapping, AppId = targetApp, ServerId = Guid.NewGuid(), PortNumber = 4002 });
        var source = fixture.AddNode("application", frame.Id, sourceMapping);
        var target = fixture.AddNode("application", frame.Id, targetMapping);
        var dependencyId = Guid.NewGuid();
        var firstEdgeId = Guid.NewGuid();
        fixture.Context.AppDependencies.Add(new AppDependency
        {
            Id = dependencyId, SourceAppId = sourceApp, DestAppId = targetApp,
            DestPortId = targetMapping, ConnectionType = "Manual"
        });
        fixture.Context.TopologyEdges.AddRange(
            new TopologyEdge
            {
                Id = firstEdgeId, SourceNodeId = source.Id, TargetNodeId = target.Id,
                SourceHandle = "one", EdgeType = "default", ReferenceId = dependencyId
            },
            new TopologyEdge
            {
                Id = Guid.NewGuid(), SourceNodeId = source.Id, TargetNodeId = target.Id,
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
            new PortMapping { Id = sourceMapping, AppId = sourceApp, ServerId = Guid.NewGuid(), PortNumber = 1001, Protocol = "TCP" },
            new PortMapping { Id = targetMapping, AppId = targetApp, ServerId = Guid.NewGuid(), PortNumber = 1002, Protocol = "TCP" });
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
            new PortMapping { Id = sourceMapping, AppId = Guid.NewGuid(), ServerId = Guid.NewGuid(), PortNumber = 1101 },
            new PortMapping { Id = targetMapping, AppId = Guid.NewGuid(), ServerId = Guid.NewGuid(), PortNumber = 1102 });
        var source = fixture.AddNode("application", frame.Id, sourceMapping);
        var target = fixture.AddNode("application", frame.Id, targetMapping);
        var edgeId = Guid.NewGuid();
        fixture.Context.AppDependencies.Add(new AppDependency
        {
            Id = edgeId,
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

    private sealed class Fixture : IAsyncDisposable
    {
        private const string Actor = "auditor";
        private readonly Mock<IWorkspaceAccessService> _access = new();
        private readonly Mock<IScopedResourcePolicy> _policy = new();
        private readonly Guid _workspaceId = Guid.NewGuid();
        private string _scopeMode;
        private IReadOnlyList<Guid> _frameIds = [];

        private Fixture(string scopeMode, long topologyVersion)
        {
            _scopeMode = scopeMode;
            var tenant = new Mock<ITenantProvider>();
            tenant.SetupGet(item => item.WorkspaceId).Returns(_workspaceId);
            var user = new Mock<ICurrentUserService>();
            user.SetupGet(item => item.UserId).Returns(Actor);
            var options = new DbContextOptionsBuilder<AuditDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(item => item.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            Context = new AuditDbContext(options, tenant.Object);
            Context.Workspaces.Add(new Workspace
            {
                Id = _workspaceId,
                Name = "Scoped",
                OwnerUserId = "owner",
                TopologyVersion = topologyVersion
            });
            ConfigureAccess();
            Service = new TopologyCommandService(
                Context, _access.Object, _policy.Object, user.Object, tenant.Object,
                NullLogger<TopologyCommandService>.Instance);
        }

        public AuditDbContext Context { get; }
        public TopologyCommandService Service { get; }

        public static Fixture ForFrames(long topologyVersion = 0) => new(WorkspaceScopeModes.Frames, topologyVersion);
        public static Fixture ForLabels(long topologyVersion = 0) => new(WorkspaceScopeModes.Labels, topologyVersion);
        public static Fixture ForAll(long topologyVersion = 0) => new(WorkspaceScopeModes.All, topologyVersion);

        public TopologyNode AddNode(string type, Guid? parentId = null, Guid? referenceId = null)
        {
            var node = new TopologyNode
            {
                Id = Guid.NewGuid(),
                WorkspaceId = _workspaceId,
                NodeType = type,
                Label = type,
                ParentNodeId = parentId,
                ReferenceId = referenceId,
                X = 0,
                Y = 0
            };
            Context.TopologyNodes.Add(node);
            if (type == "frame" && _frameIds.Count == 0) GrantFrames(node.Id);
            return node;
        }

        public void GrantFrames(params Guid[] frameIds)
        {
            _frameIds = frameIds;
            ConfigureAccess();
        }

        public void AllowResources(IReadOnlySet<Guid> servers, IReadOnlySet<Guid> applications)
        {
            _policy.Setup(item => item.GetReadableIdsAsync(_workspaceId, Actor, "server", It.IsAny<CancellationToken>())).ReturnsAsync(servers);
            _policy.Setup(item => item.GetReadableIdsAsync(_workspaceId, Actor, "application", It.IsAny<CancellationToken>())).ReturnsAsync(applications);
        }

        public Task SaveAsync() => Context.SaveChangesAsync();

        private void ConfigureAccess()
        {
            var scope = new WorkspaceScopeDto(
                _scopeMode,
                [],
                _frameIds.Select(id => new WorkspaceScopeTargetDto(id, "frame")).ToList());
            var access = new WorkspaceAccessDto(
                _workspaceId,
                "shared",
                WorkspaceRoles.Auditor,
                scope,
                new WorkspaceCapabilitiesDto(false, true, true, false, false, false));
            _access.Setup(item => item.ResolveAsync(_workspaceId, Actor, It.IsAny<CancellationToken>())).ReturnsAsync(access);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
