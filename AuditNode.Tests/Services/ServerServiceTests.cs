using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Services;
using AuditNode.Infrastructure.Data;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public class ServerServiceTests
{
    private AuditDbContext GetDbContext(Guid workspaceId)
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
    public async Task GetServersAsync_ShouldReturnServers()
    {
        // Arrange
        var workspaceId = Guid.Empty;
        using var context = GetDbContext(workspaceId);
        var serverId = Guid.NewGuid();
        var dcId = Guid.NewGuid();
        context.Datacenters.Add(new Datacenter { Id = dcId, Name = "DC1" });
        var app = new AuditNode.Domain.Entities.Application { Id = Guid.NewGuid(), AppCode = "A1", AppName = "App 1", OwnerTeam = "Team A", WorkspaceId = workspaceId };
        context.Applications.Add(app);
        context.Servers.Add(new Server
        {
            Id = serverId,
            Hostname = "SRV-TEST",
            IpAddress = "10.0.0.1",
            DatacenterId = dcId,
            WorkspaceId = workspaceId,
            PortMappings = new List<PortMapping>
            {
                new PortMapping { Id = Guid.NewGuid(), AppId = app.Id, PortNumber = 80, Protocol = "TCP", WorkspaceId = workspaceId, Application = app }
            }
        });
        await context.SaveChangesAsync();

        var service = new ServerService(context);

        // Act
        var result = await service.GetServersAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result.First().Hostname.Should().Be("SRV-TEST");
    }

    [Fact]
    public async Task ExportServersAsync_ShouldReturnSelectedServers()
    {
        // Arrange
        var workspaceId = Guid.Empty;
        using var context = GetDbContext(workspaceId);
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var dcId = Guid.NewGuid();
        context.Datacenters.Add(new Datacenter { Id = dcId, Name = "DC1" });
        var app = new AuditNode.Domain.Entities.Application { Id = Guid.NewGuid(), AppCode = "A1", AppName = "App 1", OwnerTeam = "Team A", WorkspaceId = workspaceId };
        context.Applications.Add(app);
        var pm = new PortMapping { Id = Guid.NewGuid(), AppId = app.Id, PortNumber = 80, Protocol = "TCP", WorkspaceId = workspaceId, Application = app };
        context.Servers.Add(new Server { Id = ids[0], Hostname = "S1", DatacenterId = dcId, WorkspaceId = workspaceId, PortMappings = new List<PortMapping> { pm } });
        context.Servers.Add(new Server { Id = ids[1], Hostname = "S2", DatacenterId = dcId, WorkspaceId = workspaceId, PortMappings = new List<PortMapping>() });
        context.Servers.Add(new Server { Id = Guid.NewGuid(), Hostname = "S3", DatacenterId = dcId, WorkspaceId = workspaceId, PortMappings = new List<PortMapping>() });
        await context.SaveChangesAsync();

        var service = new ServerService(context);

        // Act
        var result = await service.ExportServersAsync(ids);

        // Assert
        result.Should().HaveCount(2);
        result.Select(s => s.Hostname).Should().Contain(new[] { "S1", "S2" });
        result.Select(s => s.Hostname).Should().NotContain("S3");
    }

    [Fact]
    public async Task UpdateServerAsync_ShouldUpdateAndReturnTrue_WhenServerExists()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        using var context = GetDbContext(workspaceId);
        var serverId = Guid.NewGuid();
        context.Servers.Add(new Server { Id = serverId, Hostname = "OldHost", DatacenterId = Guid.NewGuid(), WorkspaceId = workspaceId });
        await context.SaveChangesAsync();

        var service = new ServerService(context);
        var updateDto = new UpdateServerDto { Hostname = "NewHost", OsType = "Linux" };

        // Act
        var result = await service.UpdateServerAsync(serverId, updateDto);

        // Assert
        result.Should().BeTrue();
        var updatedServer = await context.Servers.FindAsync(serverId);
        updatedServer!.Hostname.Should().Be("NewHost");
        updatedServer.OsType.Should().Be("Linux");
    }

    [Fact]
    public async Task UpdateServerAsync_ShouldReturnFalse_WhenServerDoesNotExist()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        using var context = GetDbContext(workspaceId);
        var service = new ServerService(context);
        var updateDto = new UpdateServerDto { Hostname = "NewHost" };

        // Act
        var result = await service.UpdateServerAsync(Guid.NewGuid(), updateDto);

        // Assert
        result.Should().BeFalse();
    }
}
