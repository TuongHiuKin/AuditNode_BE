using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Repositories;

public class ApplicationRepositoryTests
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
            OwnerTeam = "Owner1", 
            Risk = "LOW",
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

    [Fact]
    public async Task RegisterApplicationAsync_ShouldCreateNew_WhenAppCodeDoesNotExist()
    {
        // Arrange
        using var context = GetDbContext();
        var repository = new ApplicationRepository(context);
        var appId = Guid.NewGuid();
        var application = new AppEntity
        {
            Id = appId,
            AppCode = "NEWAPP",
            AppName = "New App",
            OwnerTeam = "Team A",
            ServerId = Guid.NewGuid()
        };

        // Act
        var result = await repository.RegisterApplicationAsync(application);

        // Assert
        result.Should().NotBeNull();
        result.AppCode.Should().Be("NEWAPP");
        context.Applications.Should().Contain(a => a.AppCode == "NEWAPP");
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateFields()
    {
        // Arrange
        using var context = GetDbContext();
        var app = new AppEntity
        {
            Id = Guid.NewGuid(),
            AppCode = "UPDATE_ME",
            AppName = "Original Name",
            OwnerTeam = "Original Team",
            ServerId = Guid.NewGuid()
        };
        context.Applications.Add(app);
        await context.SaveChangesAsync();

        var repository = new ApplicationRepository(context);
        app.AppName = "Updated Name";
        app.OwnerTeam = "Updated Team";

        // Act
        await repository.UpdateAsync(app);

        // Assert
        var updatedApp = await context.Applications.FindAsync(app.Id);
        updatedApp.Should().NotBeNull();
        updatedApp!.AppName.Should().Be("Updated Name");
        updatedApp!.OwnerTeam.Should().Be("Updated Team");
    }

    [Fact]
    public async Task RegisterApplicationAsync_ShouldReturnExisting_AndAddPortMapping_WhenAppCodeExists()
    {
        // Arrange
        using var context = GetDbContext();
        var server1Id = Guid.NewGuid();
        var existingApp = new AppEntity
        {
            Id = Guid.NewGuid(),
            AppCode = "EXISTING",
            AppName = "Existing App",
            OwnerTeam = "Team B",
            ServerId = server1Id
        };
        context.Applications.Add(existingApp);
        await context.SaveChangesAsync();

        var repository = new ApplicationRepository(context);
        var server2Id = Guid.NewGuid();
        var newAppRequest = new AppEntity
        {
            Id = Guid.NewGuid(),
            AppCode = "EXISTING",
            AppName = "Updated App Name",
            OwnerTeam = "Team B",
            ServerId = server2Id
        };
        var newPortMapping = new PortMapping
        {
            Id = Guid.NewGuid(),
            ServerId = server2Id,
            PortNumber = 9090,
            Protocol = "TCP"
        };
        newAppRequest.PortMappings.Add(newPortMapping);

        // Act
        var result = await repository.RegisterApplicationAsync(newAppRequest);

        // Assert
        result.Id.Should().Be(existingApp.Id);
        result.AppName.Should().Be("Updated App Name");
        
        var portMappings = await context.PortMappings.Where(pm => pm.AppId == existingApp.Id).ToListAsync();
        portMappings.Should().HaveCount(1);
        portMappings.First().PortNumber.Should().Be(9090);
    }
}
