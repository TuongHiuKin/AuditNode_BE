using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
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
    private AuditDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.WorkspaceId).Returns(Guid.NewGuid());
        return new AuditDbContext(options, mockTenantProvider.Object);
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

        var repository = new TopologyRepository(context);

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

        var repository = new TopologyRepository(context);
        var newNodeId = Guid.NewGuid();
        var state = new SaveTopologyStateDto
        {
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
            }
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

        var repository = new TopologyRepository(context);

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

        var repository = new TopologyRepository(context);

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

        var map = await new TopologyRepository(context).GetDependencyMapAsync();
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

        var map = await new TopologyRepository(context).GetDependencyMapAsync();

        map.Connections.Should().ContainSingle().Which.DestinationPortMappingId.Should().Be(dependency.DestPortId);
    }

    [Fact]
    public async Task Canonical_state_roundtrips_nodes_frames_and_edges()
    {
        using var context = GetDbContext();
        var frameId = Guid.NewGuid();
        var firstNodeId = Guid.NewGuid();
        var secondNodeId = Guid.NewGuid();
        var state = new TopologyStateDto
        {
            Nodes =
            [
                new TopologyNodeDto { Id = frameId, NodeType = "frame", Label = "Frame", X = 1, Y = 2, Width = 500, Height = 300 },
                new TopologyNodeDto { Id = firstNodeId, NodeType = "group", Label = "One", ParentNodeId = frameId, X = 3, Y = 4 },
                new TopologyNodeDto { Id = secondNodeId, NodeType = "group", Label = "Two", ParentNodeId = frameId, X = 5, Y = 6 }
            ],
            Edges =
            [
                new TopologyEdgeDto { Id = Guid.NewGuid(), SourceNodeId = firstNodeId, TargetNodeId = secondNodeId, EdgeType = "smoothstep" }
            ]
        };
        var repository = new TopologyRepository(context);

        var status = await repository.SaveTopologyStateAsync(state);
        var reloaded = await repository.GetTopologyStateAsync();

        status.Should().Be(TopologyStateStatus.Success);
        reloaded.Should().BeEquivalentTo(state);
    }

    [Fact]
    public async Task State_rejects_duplicate_ids_without_persisting_partial_data()
    {
        using var context = GetDbContext();
        var duplicate = Guid.NewGuid();
        var repository = new TopologyRepository(context);

        var status = await repository.SaveTopologyStateAsync(new TopologyStateDto
        {
            Nodes =
            [
                new TopologyNodeDto { Id = duplicate, NodeType = "group" },
                new TopologyNodeDto { Id = duplicate, NodeType = "group" }
            ]
        });

        status.Should().Be(TopologyStateStatus.DuplicateId);
        context.TopologyNodes.Should().BeEmpty();
    }

    [Fact]
    public async Task State_rejects_invalid_parent_type()
    {
        using var context = GetDbContext();
        var parent = Guid.NewGuid();
        var status = await new TopologyRepository(context).SaveTopologyStateAsync(new TopologyStateDto
        {
            Nodes =
            [
                new TopologyNodeDto { Id = parent, NodeType = "application", ReferenceId = Guid.NewGuid() },
                new TopologyNodeDto { Id = Guid.NewGuid(), NodeType = "group", ParentNodeId = parent }
            ]
        });

        status.Should().Be(TopologyStateStatus.InvalidParent);
    }
}
