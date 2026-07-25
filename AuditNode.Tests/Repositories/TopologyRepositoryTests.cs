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
        mockTenantProvider.Setup(x => x.WorkspaceId).Returns(Guid.Empty);
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
            NodeType = "server", 
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
                    NodeType = "server", 
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
}
