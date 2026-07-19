using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
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
            .Options;        return new AuditDbContext(options);
    }

    private static TopologyRepository CreateRepository(AuditDbContext context, string userId = "owner-1")
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(userId);
        return new TopologyRepository(context, currentUser.Object);
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

        var repository = CreateRepository(context);

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

        var repository = CreateRepository(context);
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
    public async Task GetDependencyMapAsync_ShouldFilterByLabelId_AndCurrentOwner()
    {
        using var context = GetDbContext();
        var selectedLabel = new Label
        {
            Id = Guid.NewGuid(),
            Key = "team",
            Value = "platform",
            ColorHex = "#3366ff",
            OwnerId = "owner-1"
        };
        var otherLabel = new Label
        {
            Id = Guid.NewGuid(),
            Key = "team",
            Value = "payments",
            OwnerId = "owner-1"
        };
        var selectedServer = new Server
        {
            Id = Guid.NewGuid(),
            Hostname = "platform-01",
            OwnerId = "owner-1",
            Environment = "Production",
            Labels = new List<Label> { selectedLabel }
        };
        var otherLabelServer = new Server
        {
            Id = Guid.NewGuid(),
            Hostname = "payments-01",
            OwnerId = "owner-1",
            Labels = new List<Label> { otherLabel }
        };
        var otherOwnerServer = new Server
        {
            Id = Guid.NewGuid(),
            Hostname = "platform-foreign",
            OwnerId = "owner-2",
            Labels = new List<Label>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Key = "team",
                    Value = "platform",
                    OwnerId = "owner-2"
                }
            }
        };

        context.Servers.AddRange(selectedServer, otherLabelServer, otherOwnerServer);
        await context.SaveChangesAsync();

        var result = await CreateRepository(context).GetDependencyMapAsync([selectedLabel.Id]);

        result.Servers.Should().ContainSingle()
            .Which.Id.Should().Be(selectedServer.Id);
        result.Servers.Single().Labels.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new TopologyLabelDto
            {
                Id = selectedLabel.Id,
                Key = "team",
                Value = "platform",
                ColorHex = "#3366ff"
            });
        result.Servers.Single().Environment.Should().Be("Production");
    }

    [Fact]
    public async Task GetDependencyMapAsync_ShouldIncludeHostingServerForApplicationLabel()
    {
        using var context = GetDbContext();
        var selectedLabel = new Label
        {
            Id = Guid.NewGuid(),
            Key = "service",
            Value = "payments",
            ColorHex = "#ff4d7e",
            OwnerId = "owner-1"
        };
        var matchingApp = new AppEntity
        {
            Id = Guid.NewGuid(),
            AppName = "Payments API",
            OwnerId = "owner-1",
            Labels = new List<Label> { selectedLabel }
        };
        var siblingApp = new AppEntity
        {
            Id = Guid.NewGuid(),
            AppName = "Shared Agent",
            OwnerId = "owner-1"
        };
        var server = new Server
        {
            Id = Guid.NewGuid(),
            Hostname = "application-host",
            OwnerId = "owner-1",
            PortMappings = new List<PortMapping>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    AppId = matchingApp.Id,
                    Application = matchingApp,
                    PortNumber = 8080,
                    Protocol = "HTTP"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    AppId = siblingApp.Id,
                    Application = siblingApp,
                    PortNumber = 9100,
                    Protocol = "TCP"
                }
            }
        };

        context.Servers.Add(server);
        await context.SaveChangesAsync();

        var result = await CreateRepository(context).GetDependencyMapAsync([selectedLabel.Id]);

        result.Servers.Should().ContainSingle()
            .Which.Id.Should().Be(server.Id);
        result.Servers.Single().Applications.Should().HaveCount(2);
        result.Servers.Single().Applications
            .Single(application => application.Id == matchingApp.Id)
            .Labels.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new TopologyLabelDto
            {
                Id = selectedLabel.Id,
                Key = "service",
                Value = "payments",
                ColorHex = "#ff4d7e"
            });
        result.Servers.Single().Applications
            .Single(application => application.Id == siblingApp.Id)
            .Labels.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDependencyMapAsync_ShouldNotMatchForeignOwnerApplicationLabel()
    {
        using var context = GetDbContext();
        var foreignLabel = new Label
        {
            Id = Guid.NewGuid(),
            Key = "service",
            Value = "foreign",
            OwnerId = "owner-2"
        };
        var foreignApp = new AppEntity
        {
            Id = Guid.NewGuid(),
            AppName = "Foreign App",
            OwnerId = "owner-2",
            Labels = new List<Label> { foreignLabel }
        };
        var ownedServer = new Server
        {
            Id = Guid.NewGuid(),
            Hostname = "owned-host",
            OwnerId = "owner-1",
            PortMappings = new List<PortMapping>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    AppId = foreignApp.Id,
                    Application = foreignApp,
                    PortNumber = 8443,
                    Protocol = "HTTPS"
                }
            }
        };

        context.Servers.Add(ownedServer);
        await context.SaveChangesAsync();

        var result = await CreateRepository(context).GetDependencyMapAsync([foreignLabel.Id]);

        result.Servers.Should().BeEmpty();
        result.Connections.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDependencyMapAsync_ShouldNotExposeForeignOwnerApplicationOnOwnedServer()
    {
        using var context = GetDbContext();
        var foreignApp = new AppEntity
        {
            Id = Guid.NewGuid(),
            AppName = "Foreign App",
            OwnerId = "owner-2"
        };
        var ownedServer = new Server
        {
            Id = Guid.NewGuid(),
            Hostname = "owned-host",
            OwnerId = "owner-1",
            PortMappings = new List<PortMapping>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    AppId = foreignApp.Id,
                    Application = foreignApp,
                    PortNumber = 8443,
                    Protocol = "HTTPS"
                }
            }
        };

        context.Servers.Add(ownedServer);
        await context.SaveChangesAsync();

        var result = await CreateRepository(context).GetDependencyMapAsync();

        result.Servers.Should().ContainSingle();
        result.Servers.Single().Applications.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDependencyMapAsync_WithMultipleLabels_ShouldUseOrSemantics()
    {
        using var context = GetDbContext();
        var labelA = new Label { Id = Guid.NewGuid(), Key = "tier", Value = "api", OwnerId = "owner-1" };
        var labelB = new Label { Id = Guid.NewGuid(), Key = "tier", Value = "worker", OwnerId = "owner-1" };
        var serverA = new Server
        {
            Id = Guid.NewGuid(),
            Hostname = "api-01",
            OwnerId = "owner-1",
            Labels = new List<Label> { labelA }
        };
        var serverB = new Server
        {
            Id = Guid.NewGuid(),
            Hostname = "worker-01",
            OwnerId = "owner-1",
            Labels = new List<Label> { labelB }
        };

        context.Servers.AddRange(serverA, serverB);
        await context.SaveChangesAsync();

        var result = await CreateRepository(context).GetDependencyMapAsync([labelA.Id, labelB.Id]);

        result.Servers.Select(server => server.Id)
            .Should().BeEquivalentTo([serverA.Id, serverB.Id]);
    }

    [Fact]
    public async Task GetDependencyMapAsync_ShouldReturnOnlyConnectionsInsideVisibleOwnerScope()
    {
        using var context = GetDbContext();
        var appA = new AppEntity { Id = Guid.NewGuid(), AppName = "API", OwnerId = "owner-1" };
        var appB = new AppEntity { Id = Guid.NewGuid(), AppName = "Worker", OwnerId = "owner-1" };
        var foreignApp = new AppEntity { Id = Guid.NewGuid(), AppName = "Foreign", OwnerId = "owner-2" };
        var mappingA = new PortMapping
        {
            Id = Guid.NewGuid(),
            AppId = appA.Id,
            Application = appA,
            PortNumber = 8080,
            Protocol = "TCP"
        };
        var mappingB = new PortMapping
        {
            Id = Guid.NewGuid(),
            AppId = appB.Id,
            Application = appB,
            PortNumber = 8081,
            Protocol = "TCP"
        };
        var foreignMapping = new PortMapping
        {
            Id = Guid.NewGuid(),
            AppId = foreignApp.Id,
            Application = foreignApp,
            PortNumber = 9090,
            Protocol = "TCP"
        };
        var visibleServer = new Server
        {
            Id = Guid.NewGuid(),
            Hostname = "visible",
            OwnerId = "owner-1",
            PortMappings = new List<PortMapping> { mappingA, mappingB }
        };
        var foreignServer = new Server
        {
            Id = Guid.NewGuid(),
            Hostname = "foreign",
            OwnerId = "owner-2",
            PortMappings = new List<PortMapping> { foreignMapping }
        };
        var visibleDependency = new AppDependency
        {
            Id = Guid.NewGuid(),
            SourceAppId = appA.Id,
            DestAppId = appB.Id,
            DestPortId = mappingB.Id,
            ConnectionType = "TCP"
        };
        var crossOwnerDependency = new AppDependency
        {
            Id = Guid.NewGuid(),
            SourceAppId = appA.Id,
            DestAppId = foreignApp.Id,
            DestPortId = foreignMapping.Id,
            ConnectionType = "TCP"
        };

        context.Servers.AddRange(visibleServer, foreignServer);
        context.AppDependencies.AddRange(visibleDependency, crossOwnerDependency);
        await context.SaveChangesAsync();

        var result = await CreateRepository(context).GetDependencyMapAsync();

        result.Connections.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ConnectionDto
            {
                SourceAppId = appA.Id,
                TargetAppId = appB.Id
            });
    }
}
