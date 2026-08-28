using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Infrastructure.Services;
using AuditNode.Application.Interfaces;
using AuditNode.Application.DTOs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Repositories;

public class TopologyRepositoryTests
{
    private static TopologyRepository Repository(AuditDbContext context)
    {
        var policy = new Mock<IScopedResourcePolicy>();
        policy.Setup(x => x.GetReadableIdsAsync(It.IsAny<Guid>(), "test-user", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlySet<Guid>?)null);
        policy.Setup(x => x.GetGrantedFrameIdsAsync(It.IsAny<Guid>(), "test-user", It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlySet<Guid>?)null);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("test-user");
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(x => x.WorkspaceId).Returns(context.CurrentWorkspaceId);
        return new TopologyRepository(context, policy.Object, user.Object, tenant.Object, new WorkspaceAccessService(context));
    }
    private AuditDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var mockTenantProvider = new Mock<ITenantProvider>();
        var workspaceId = Guid.NewGuid();
        mockTenantProvider.Setup(x => x.WorkspaceId).Returns(workspaceId);
        var context = new AuditDbContext(options, mockTenantProvider.Object);
        context.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Test", OwnerUserId = "test-user" });
        context.SaveChanges();
        return context;
    }

    [Fact]
    public async Task GetApplicationStatusAsync_ShouldReturnStatus_CorrectlyIdentifyingMappedApps()
    {
        // Arrange
        using var context = GetDbContext();
        var app1 = new AppEntity { Id = Guid.NewGuid(), AppName = "App 1", AppCode = "A1", OwnerTeam = "O1", Risk = "LOW" };
        var app2 = new AppEntity { Id = Guid.NewGuid(), AppName = "App 2", AppCode = "A2", OwnerTeam = "O2", Risk = "LOW" };
        var app3 = new AppEntity { Id = Guid.NewGuid(), AppName = "App 3", AppCode = "A3", OwnerTeam = "O3", Risk = "LOW" };
        
        context.Applications.AddRange(app1, app2, app3);

        // App 1 is a source, App 2 is a destination. App 3 is not in any dependency.
        context.AppDependencies.Add(new AppDependency 
        { 
            Id = Guid.NewGuid(), 
            SourceAppId = app1.Id, 
            DestAppId = app2.Id,
            DestPortId = Guid.NewGuid(),
            ConnectionType = "TCP"
        });

        await context.SaveChangesAsync();

        var repository = Repository(context);

        // Act
        var result = await repository.GetApplicationStatusAsync();

        // Assert
        result.Should().HaveCount(3);
        result.First(a => a.Id == app1.Id).IsMapped.Should().BeTrue();
        result.First(a => a.Id == app2.Id).IsMapped.Should().BeTrue();
        result.First(a => a.Id == app3.Id).IsMapped.Should().BeFalse();
    }

    [Fact]
    public async Task SaveTopologyStateAsync_ShouldUpsertAndSyncNodes()
    {
        // Arrange
        using var context = GetDbContext();
        var existingNodeId = Guid.NewGuid();
        context.TopologyNodes.Add(new TopologyNode 
        { 
            Id = existingNodeId, 
            NodeType = "group",
            Label = "Old Label", 
            X = 0, 
            Y = 0 
        });
        await context.SaveChangesAsync();

        var repository = Repository(context);
        var newNodeId = Guid.NewGuid();
        var state = new SaveTopologyStateDto
        {
            Version = 0,
            Nodes = new List<TopologyNodeDto>
            {
                new TopologyNodeDto 
                { 
                    Id = existingNodeId, 
                    NodeType = "group",
                    Label = "New Label", 
                    X = 10, 
                    Y = 10,
                    Width = 100,
                    Height = 100
                },
                new TopologyNodeDto 
                { 
                    Id = newNodeId, 
                    NodeType = "group", 
                    Label = "Group 1", 
                    X = 50, 
                    Y = 50,
                    Width = 200,
                    Height = 200
                }
            },
            Edges = [],
            Dependencies = []
        };

        // Act
        await repository.SaveTopologyStateAsync(state);

        // Assert
        var nodes = await context.TopologyNodes.ToListAsync();
        nodes.Should().HaveCount(2);
        
        var updatedNode = nodes.First(n => n.Id == existingNodeId);
        updatedNode.Label.Should().Be("New Label");
        updatedNode.X.Should().Be(10);
        updatedNode.Width.Should().Be(100);

        var newNode = nodes.First(n => n.Id == newNodeId);
        newNode.NodeType.Should().Be("group");
        newNode.Label.Should().Be("Group 1");
    }

    [Fact]
    public async Task GetDependencyMapAsync_WithLabelsFilter_ReturnsOnlyMatchingServersAndIncludesLabels()
    {
        // Arrange
        using var context = GetDbContext();
        var srv1 = new Server { Id = Guid.NewGuid(), Hostname = "SRV-PROD", IpAddress = "10.0.0.1", Status = "UP", OsType = "Linux", Environment = "Prod" };
        var srv2 = new Server { Id = Guid.NewGuid(), Hostname = "SRV-DEV", IpAddress = "10.0.0.2", Status = "UP", OsType = "Linux", Environment = "Dev" };
        
        var lbl1 = new Label { Id = Guid.NewGuid(), Key = "env", Value = "prod" };
        var lbl2 = new Label { Id = Guid.NewGuid(), Key = "tier", Value = "db" };

        srv1.Labels.Add(lbl1);
        srv2.Labels.Add(lbl2);

        context.Servers.AddRange(srv1, srv2);
        context.Labels.AddRange(lbl1, lbl2);
        await context.SaveChangesAsync();

        var repository = Repository(context);

        // Act
        var result = await repository.GetDependencyMapAsync(labels: new List<string> { "env:prod" });

        // Assert
        result.Should().NotBeNull();
        result.Servers.Should().HaveCount(1);
        var matchingServer = result.Servers.First();
        matchingServer.Hostname.Should().Be("SRV-PROD");
        matchingServer.Labels.Should().HaveCount(1);
        matchingServer.Labels.First().Key.Should().Be("env");
        matchingServer.Labels.First().Value.Should().Be("prod");
    }

    [Fact]
    public async Task GetTopologyTreeAsync_WithLabelsFilter_ReturnsOnlyMatchingServersAndIncludesLabels()
    {
        // Arrange
        using var context = GetDbContext();
        var dc = new Datacenter { Id = Guid.NewGuid(), Name = "DC-Main", Location = "Loc 1" };
        var srv1 = new Server { Id = Guid.NewGuid(), DatacenterId = dc.Id, Hostname = "SRV-PROD", IpAddress = "10.0.0.1", Status = "UP", OsType = "Linux", Environment = "Prod" };
        var srv2 = new Server { Id = Guid.NewGuid(), DatacenterId = dc.Id, Hostname = "SRV-DEV", IpAddress = "10.0.0.2", Status = "UP", OsType = "Linux", Environment = "Dev" };

        var lbl1 = new Label { Id = Guid.NewGuid(), Key = "env", Value = "prod" };
        var lbl2 = new Label { Id = Guid.NewGuid(), Key = "tier", Value = "db" };

        srv1.Labels.Add(lbl1);
        srv2.Labels.Add(lbl2);

        context.Datacenters.Add(dc);
        context.Servers.AddRange(srv1, srv2);
        context.Labels.AddRange(lbl1, lbl2);
        await context.SaveChangesAsync();

        var repository = Repository(context);

        // Act
        var result = await repository.GetTopologyTreeAsync(datacenterId: null, skip: 0, take: 100, labels: new List<string> { "env:prod" });

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        var dcNode = result.First();
        dcNode.Servers.Should().HaveCount(1);
        var matchingServer = dcNode.Servers.First();
        matchingServer.Hostname.Should().Be("SRV-PROD");
        matchingServer.Labels.Should().HaveCount(1);
        matchingServer.Labels.First().Key.Should().Be("env");
        matchingServer.Labels.First().Value.Should().Be("prod");
    }

    [Fact]
    public async Task One_application_on_two_servers_has_unique_deployment_node_ids()
    {
        using var context = GetDbContext();
        var app = new AppEntity { Id = Guid.NewGuid(), AppCode = "APP", AppName = "App" };
        var first = new Server { Id = Guid.NewGuid(), Hostname = "one", IpAddress = "10.0.0.1" };
        var second = new Server { Id = Guid.NewGuid(), Hostname = "two", IpAddress = "10.0.0.2" };
        var firstMapping = new PortMapping { Id = Guid.NewGuid(), AppId = app.Id, ServerId = first.Id, PortNumber = 80 };
        var secondMapping = new PortMapping { Id = Guid.NewGuid(), AppId = app.Id, ServerId = second.Id, PortNumber = 81 };
        context.AddRange(app, first, second, firstMapping, secondMapping);
        await context.SaveChangesAsync();

        var map = await Repository(context).GetDependencyMapAsync();
        var nodes = map.Servers.SelectMany(server => server.Applications).ToArray();

        nodes.Select(node => node.Id).Should().BeEquivalentTo([firstMapping.Id, secondMapping.Id]);
        nodes.Should().OnlyContain(node => node.AppId == app.Id);
        nodes.Select(node => node.ServerId).Should().BeEquivalentTo([first.Id, second.Id]);
    }

    [Fact]
    public async Task Dependency_connection_retains_destination_port_mapping()
    {
        using var context = GetDbContext();
        var dependency = new AppDependency
        {
            Id = Guid.NewGuid(), SourceAppId = Guid.NewGuid(), DestAppId = Guid.NewGuid(),
            DestPortId = Guid.NewGuid(), ConnectionType = "TCP"
        };
        context.PortMappings.Add(new PortMapping
        {
            Id = dependency.DestPortId, AppId = dependency.DestAppId,
            ServerId = Guid.NewGuid(), PortNumber = 443
        });
        context.AppDependencies.Add(dependency);
        await context.SaveChangesAsync();

        var map = await Repository(context).GetDependencyMapAsync();

        map.Connections.Should().ContainSingle().Which.DestinationPortMappingId.Should().Be(dependency.DestPortId);
    }

    [Fact]
    public async Task Scoped_dependency_map_preserves_boundary_connection_without_leaking_hidden_identifiers()
    {
        using var context = GetDbContext();
        var visibleServer = new Server { Id = Guid.NewGuid(), Hostname = "visible", IpAddress = "10.0.0.1" };
        var hiddenServer = new Server { Id = Guid.NewGuid(), Hostname = "secret-host", IpAddress = "10.0.0.99" };
        var visibleApp = new AppEntity { Id = Guid.NewGuid(), AppCode = "VISIBLE", AppName = "Visible" };
        var hiddenApp = new AppEntity { Id = Guid.NewGuid(), AppCode = "SECRET", AppName = "Secret Application" };
        var hiddenMapping = new PortMapping { Id = Guid.NewGuid(), AppId = hiddenApp.Id, ServerId = hiddenServer.Id, PortNumber = 9443 };
        var dependency = new AppDependency { Id = Guid.NewGuid(), SourceAppId = visibleApp.Id, DestAppId = hiddenApp.Id, DestPortId = hiddenMapping.Id, ConnectionType = "SensitiveProtocol" };
        context.AddRange(visibleServer, hiddenServer, visibleApp, hiddenApp, hiddenMapping, dependency);
        await context.SaveChangesAsync();
        var policy = new Mock<IScopedResourcePolicy>();
        policy.Setup(x => x.GetReadableIdsAsync(It.IsAny<Guid>(), "viewer", "server", It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<Guid> { visibleServer.Id });
        policy.Setup(x => x.GetReadableIdsAsync(It.IsAny<Guid>(), "viewer", "application", It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<Guid> { visibleApp.Id });
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("viewer");
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(x => x.WorkspaceId).Returns(context.CurrentWorkspaceId);

        var map = await new TopologyRepository(context, policy.Object, user.Object, tenant.Object, new WorkspaceAccessService(context)).GetDependencyMapAsync();

        var connection = map.Connections.Should().ContainSingle().Which;
        connection.SourceAppId.Should().Be(visibleApp.Id);
        connection.TargetAppId.Should().NotBe(hiddenApp.Id);
        connection.DestinationServerId.Should().NotBe(hiddenServer.Id);
        connection.DestinationPortMappingId.Should().NotBe(hiddenMapping.Id);
        connection.Id.Should().NotBe(dependency.Id);
        connection.ConnectionType.Should().Be("Restricted");
        connection.IsRestricted.Should().BeTrue();
        map.RestrictedNodes.Should().ContainSingle(x => x.Id == connection.TargetAppId && x.IsRestricted && x.DisplayName == "External Resource (Restricted)");
        map.Servers.Should().NotContain(x => x.Id == hiddenServer.Id || x.Hostname == hiddenServer.Hostname);
    }

    [Fact]
    public async Task Canonical_state_roundtrips_nodes_frames_and_edges()
    {
        using var context = GetDbContext();
        var frameId = Guid.NewGuid();
        var firstNodeId = Guid.NewGuid();
        var secondNodeId = Guid.NewGuid();
        var state = new SaveTopologyStateDto
        {
            Version = 0,
            Nodes =
            [
                new TopologyNodeDto { Id = frameId, NodeType = "frame", Label = "Frame", X = 1, Y = 2, Width = 500, Height = 300 },
                new TopologyNodeDto { Id = firstNodeId, NodeType = "group", Label = "One", ParentNodeId = frameId, X = 3, Y = 4 },
                new TopologyNodeDto { Id = secondNodeId, NodeType = "group", Label = "Two", ParentNodeId = frameId, X = 5, Y = 6 }
            ],
            Edges =
            [
                new TopologyEdgeDto { Id = Guid.NewGuid(), SourceNodeId = firstNodeId, TargetNodeId = secondNodeId, EdgeType = "smoothstep" }
            ],
            Dependencies = []
        };
        var repository = Repository(context);

        var status = await repository.SaveTopologyStateAsync(state);
        var reloaded = await repository.GetTopologyStateAsync();

        status.Should().Be(TopologyStateStatus.Success);
        reloaded.Version.Should().Be(1);
        reloaded.Nodes.Should().BeEquivalentTo(state.Nodes);
        reloaded.Edges.Should().BeEquivalentTo(state.Edges);
    }

    [Fact]
    public async Task Canonical_state_reconciles_dependencies_in_the_same_revision()
    {
        using var context = GetDbContext();
        var sourceApp = new AppEntity { Id = Guid.NewGuid(), AppCode = "SRC", AppName = "Source" };
        var targetApp = new AppEntity { Id = Guid.NewGuid(), AppCode = "DST", AppName = "Target" };
        var sourceMapping = new PortMapping { Id = Guid.NewGuid(), AppId = sourceApp.Id, ServerId = Guid.NewGuid(), PortNumber = 7001 };
        var targetMapping = new PortMapping { Id = Guid.NewGuid(), AppId = targetApp.Id, ServerId = Guid.NewGuid(), PortNumber = 7002 };
        var dependency = new AppDependency
        {
            Id = Guid.NewGuid(), SourceAppId = sourceApp.Id, DestAppId = targetApp.Id,
            DestPortId = targetMapping.Id, ConnectionType = "Manual"
        };
        context.AddRange(sourceApp, targetApp, sourceMapping, targetMapping, dependency);
        await context.SaveChangesAsync();
        var sourceNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();

        var status = await Repository(context).SaveTopologyStateAsync(new SaveTopologyStateDto
        {
            Version = 0,
            Nodes =
            [
                new TopologyNodeDto { Id = sourceNodeId, NodeType = "application", ReferenceId = sourceMapping.Id },
                new TopologyNodeDto { Id = targetNodeId, NodeType = "application", ReferenceId = targetMapping.Id }
            ],
            Edges =
            [
                new TopologyEdgeDto
                {
                    Id = Guid.NewGuid(), SourceNodeId = sourceNodeId, TargetNodeId = targetNodeId,
                    ReferenceId = dependency.Id
                }
            ],
            Dependencies =
            [
                new DependencyItemDto
                {
                    SourceAppId = sourceApp.Id, DestAppId = targetApp.Id,
                    DestinationPortMappingId = targetMapping.Id
                }
            ]
        });

        status.Should().Be(TopologyStateStatus.Success);
        context.AppDependencies.Should().ContainSingle().Which.Id.Should().Be(dependency.Id);
        (await context.Workspaces.SingleAsync()).TopologyVersion.Should().Be(1);

        var savedState = await Repository(context).GetTopologyStateAsync();
        var sharedReference = await Repository(context).SaveTopologyStateAsync(new SaveTopologyStateDto
        {
            Version = 1,
            Nodes = savedState.Nodes,
            Edges =
            [
                savedState.Edges.Single(),
                new TopologyEdgeDto
                {
                    Id = Guid.NewGuid(), SourceNodeId = sourceNodeId, TargetNodeId = targetNodeId,
                    SourceHandle = "second", ReferenceId = dependency.Id
                }
            ],
            Dependencies =
            [
                new DependencyItemDto
                {
                    SourceAppId = sourceApp.Id, DestAppId = targetApp.Id,
                    DestinationPortMappingId = targetMapping.Id
                }
            ]
        });
        sharedReference.Should().Be(TopologyStateStatus.InvalidReference);
        context.TopologyEdges.Should().ContainSingle();
        (await context.Workspaces.SingleAsync()).TopologyVersion.Should().Be(1);
    }

    [Fact]
    public async Task Canonical_state_links_a_new_application_edge_to_its_created_dependency()
    {
        using var context = GetDbContext();
        var sourceApp = new AppEntity { Id = Guid.NewGuid(), AppCode = "NEW-SRC", AppName = "Source" };
        var targetApp = new AppEntity { Id = Guid.NewGuid(), AppCode = "NEW-DST", AppName = "Target" };
        var sourceMapping = new PortMapping { Id = Guid.NewGuid(), AppId = sourceApp.Id, ServerId = Guid.NewGuid(), PortNumber = 7101 };
        var targetMapping = new PortMapping { Id = Guid.NewGuid(), AppId = targetApp.Id, ServerId = Guid.NewGuid(), PortNumber = 7102 };
        context.AddRange(sourceApp, targetApp, sourceMapping, targetMapping);
        await context.SaveChangesAsync();
        var sourceNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var edgeId = Guid.NewGuid();

        var status = await Repository(context).SaveTopologyStateAsync(new SaveTopologyStateDto
        {
            Version = 0,
            Nodes =
            [
                new TopologyNodeDto { Id = sourceNodeId, NodeType = "application", ReferenceId = sourceMapping.Id },
                new TopologyNodeDto { Id = targetNodeId, NodeType = "application", ReferenceId = targetMapping.Id }
            ],
            Edges = [new TopologyEdgeDto { Id = edgeId, SourceNodeId = sourceNodeId, TargetNodeId = targetNodeId }],
            Dependencies =
            [
                new DependencyItemDto
                {
                    SourceAppId = sourceApp.Id,
                    DestAppId = targetApp.Id,
                    DestinationPortMappingId = targetMapping.Id
                }
            ]
        });

        status.Should().Be(TopologyStateStatus.Success);
        var dependency = await context.AppDependencies.SingleAsync();
        (await context.TopologyEdges.SingleAsync(item => item.Id == edgeId)).ReferenceId.Should().Be(dependency.Id);
        (await Repository(context).GetTopologyStateAsync()).Edges.Single(item => item.Id == edgeId)
            .ReferenceId.Should().Be(dependency.Id);
    }

    [Fact]
    public async Task Omitted_dependencies_are_rejected_without_mutating_legacy_data()
    {
        using var context = GetDbContext();
        var dependency = new AppDependency
        {
            Id = Guid.NewGuid(), SourceAppId = Guid.NewGuid(), DestAppId = Guid.NewGuid(),
            DestPortId = Guid.NewGuid(), ConnectionType = "Legacy"
        };
        context.AppDependencies.Add(dependency);
        await context.SaveChangesAsync();

        var status = await Repository(context).SaveTopologyStateAsync(new SaveTopologyStateDto
        {
            Version = 0,
            Nodes = [],
            Edges = [],
            Dependencies = null
        });

        status.Should().Be(TopologyStateStatus.InvalidRequest);
        context.AppDependencies.Should().ContainSingle(item => item.Id == dependency.Id);
        (await context.Workspaces.SingleAsync()).TopologyVersion.Should().Be(0);
    }

    [Fact]
    public async Task Null_collection_items_are_rejected_without_incrementing_the_revision()
    {
        using var context = GetDbContext();
        var repository = Repository(context);
        var states = new[]
        {
            new SaveTopologyStateDto { Version = 0, Nodes = [null!], Edges = [], Dependencies = [] },
            new SaveTopologyStateDto { Version = 0, Nodes = [], Edges = [null!], Dependencies = [] },
            new SaveTopologyStateDto { Version = 0, Nodes = [], Edges = [], Dependencies = [null!] }
        };

        foreach (var state in states)
            (await repository.SaveTopologyStateAsync(state)).Should().Be(TopologyStateStatus.InvalidRequest);

        (await context.Workspaces.SingleAsync()).TopologyVersion.Should().Be(0);
    }

    [Fact]
    public async Task State_rejects_duplicate_ids_without_persisting_partial_data()
    {
        using var context = GetDbContext();
        var duplicate = Guid.NewGuid();
        var repository = Repository(context);

        var status = await repository.SaveTopologyStateAsync(new SaveTopologyStateDto
        {
            Version = 0,
            Nodes =
            [
                new TopologyNodeDto { Id = duplicate, NodeType = "group" },
                new TopologyNodeDto { Id = duplicate, NodeType = "group" }
            ],
            Edges = [],
            Dependencies = []
        });

        status.Should().Be(TopologyStateStatus.DuplicateId);
        context.TopologyNodes.Should().BeEmpty();
    }

    [Fact]
    public async Task State_rejects_invalid_parent_type()
    {
        using var context = GetDbContext();
        var parent = Guid.NewGuid();
        var status = await Repository(context).SaveTopologyStateAsync(new SaveTopologyStateDto
        {
            Version = 0,
            Nodes =
            [
                new TopologyNodeDto { Id = parent, NodeType = "application", ReferenceId = Guid.NewGuid() },
                new TopologyNodeDto { Id = Guid.NewGuid(), NodeType = "group", ParentNodeId = parent }
            ],
            Edges = [],
            Dependencies = []
        });

        status.Should().Be(TopologyStateStatus.InvalidParent);
    }

    [Fact]
    public async Task Scoped_state_replaces_external_endpoint_with_restricted_opaque_node()
    {
        using var context = GetDbContext();
        var visibleResource = Guid.NewGuid();
        var hiddenResource = Guid.NewGuid();
        var visibleNode = Guid.NewGuid();
        var hiddenNode = Guid.NewGuid();
        context.TopologyNodes.AddRange(
            new TopologyNode { Id = visibleNode, NodeType = "server", Label = "Visible host", ReferenceId = visibleResource },
            new TopologyNode { Id = hiddenNode, NodeType = "server", Label = "Secret host", ReferenceId = hiddenResource });
        context.TopologyEdges.Add(new TopologyEdge { Id = Guid.NewGuid(), SourceNodeId = visibleNode, TargetNodeId = hiddenNode, EdgeType = "smoothstep" });
        await context.SaveChangesAsync();
        var policy = new Mock<IScopedResourcePolicy>();
        policy.Setup(x => x.GetReadableIdsAsync(It.IsAny<Guid>(), "viewer", "server", It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<Guid> { visibleResource });
        policy.Setup(x => x.GetReadableIdsAsync(It.IsAny<Guid>(), "viewer", "application", It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<Guid>());
        policy.Setup(x => x.GetGrantedFrameIdsAsync(It.IsAny<Guid>(), "viewer", It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<Guid>());
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("viewer");
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(x => x.WorkspaceId).Returns(context.CurrentWorkspaceId);
        var repository = new TopologyRepository(context, policy.Object, user.Object, tenant.Object, new WorkspaceAccessService(context));

        var state = await repository.GetTopologyStateAsync();

        state.Nodes.Should().ContainSingle(x => x.Id == visibleNode && !x.IsRestricted);
        var restricted = state.Nodes.Should().ContainSingle(x => x.IsRestricted).Which;
        restricted.Id.Should().NotBe(hiddenNode);
        restricted.Label.Should().Be("External Resource (Restricted)");
        restricted.ReferenceId.Should().BeNull();
        var boundary = state.Edges.Should().ContainSingle(x => x.SourceNodeId == visibleNode && x.TargetNodeId == restricted.Id && x.ReferenceId == null).Which;
        boundary.SourceHandle.Should().BeEmpty();
        boundary.TargetHandle.Should().BeEmpty();
        boundary.EdgeType.Should().Be("restricted");
        boundary.Label.Should().BeEmpty();
    }
}
