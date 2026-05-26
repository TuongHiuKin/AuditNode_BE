using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Repositories;

public class ApplicationRepositoryTests
{
    private AuditDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuditDbContext(options);
    }

    [Fact]
    public async Task GetApplicationsAsync_ShouldReturnApplications_WithEagerLoadedServers()
    {
        // Arrange
        using var context = GetDbContext();
        var datacenter = new Datacenter { Id = Guid.NewGuid(), Name = "DC1", Location = "Location 1" };
        var server = new Server { Id = Guid.NewGuid(), Hostname = "SRV-01", IpAddress = "192.168.1.1", OsType = "Linux", Environment = "Prod", Status = "Up", DatacenterId = datacenter.Id };
        var application = new AppEntity 
        { 
            Id = Guid.NewGuid(), 
            AppCode = "APP01", 
            AppName = "Test App", 
            OwnerId = "Owner1", 
            Risk = "Low",
            ServerId = server.Id 
        };
        var portMapping = new PortMapping 
        { 
            Id = Guid.NewGuid(), 
            AppId = application.Id, 
            ServerId = server.Id, 
            PortNumber = 8080, 
            Protocol = "TCP" 
        };

        context.Datacenters.Add(datacenter);
        context.Servers.Add(server);
        context.Applications.Add(application);
        context.PortMappings.Add(portMapping);
        await context.SaveChangesAsync();

        var repository = new ApplicationRepository(context);

        // Act
        var result = await repository.GetApplicationsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        var appDto = result.First();
        appDto.AppCode.Should().Be("APP01");
        appDto.Servers.Should().NotBeNull();
        appDto.Servers.Should().HaveCount(1);
        appDto.Servers.First().Hostname.Should().Be("SRV-01");
        appDto.Servers.First().PortNumber.Should().Be(8080);
    }
}
