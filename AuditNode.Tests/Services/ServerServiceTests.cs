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
    public async Task GetServersAsync_ShouldReturnServers()
    {
        // Arrange
        using var context = GetDbContext();
        var serverId = Guid.NewGuid();
        context.Servers.Add(new Server
        {
            Id = serverId,
            Hostname = "SRV-TEST",
            IpAddress = "10.0.0.1",
            DatacenterId = Guid.NewGuid()
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
        using var context = GetDbContext();
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        
        context.Servers.Add(new Server { Id = ids[0], Hostname = "S1", DatacenterId = Guid.NewGuid() });
        context.Servers.Add(new Server { Id = ids[1], Hostname = "S2", DatacenterId = Guid.NewGuid() });
        context.Servers.Add(new Server { Id = Guid.NewGuid(), Hostname = "S3", DatacenterId = Guid.NewGuid() });
        await context.SaveChangesAsync();

        var service = new ServerService(context);

        // Act
        var result = await service.ExportServersAsync(ids);

        // Assert
        result.Should().HaveCount(2);
        result.Select(s => s.Hostname).Should().Contain(new[] { "S1", "S2" });
        result.Select(s => s.Hostname).Should().NotContain("S3");
    }
}
