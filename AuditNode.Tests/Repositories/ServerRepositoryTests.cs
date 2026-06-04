using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AuditNode.Tests.Repositories;

public class ServerRepositoryTests
{
    private AuditDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnServer_WithDatacenter()
    {
        // Arrange
        using var context = GetDbContext();
        var datacenter = new Datacenter { Id = Guid.NewGuid(), Name = "DC1", Location = "Location 1" };
        var server = new Server { Id = Guid.NewGuid(), Hostname = "SRV-01", IpAddress = "192.168.1.1", DatacenterId = datacenter.Id };
        
        context.Datacenters.Add(datacenter);
        context.Servers.Add(server);
        await context.SaveChangesAsync();

        var repository = new ServerRepository(context);

        // Act
        var result = await repository.GetByIdAsync(server.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Hostname.Should().Be("SRV-01");
        result.Datacenter.Should().NotBeNull();
        result.Datacenter!.Name.Should().Be("DC1");
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateServerFields()
    {
        // Arrange
        using var context = GetDbContext();
        var server = new Server { Id = Guid.NewGuid(), Hostname = "OldName", IpAddress = "10.0.0.1" };
        context.Servers.Add(server);
        await context.SaveChangesAsync();

        var repository = new ServerRepository(context);
        server.Hostname = "NewName";

        // Act
        await repository.UpdateAsync(server);

        // Assert
        var updatedServer = await context.Servers.FindAsync(server.Id);
        updatedServer.Should().NotBeNull();
        updatedServer!.Hostname.Should().Be("NewName");
    }
}
