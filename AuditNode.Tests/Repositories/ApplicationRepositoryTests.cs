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

public class ApplicationRepositoryTests
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
            Risk = "LOW"
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
        appDto.Servers.First().PortMappingId.Should().Be(portMapping.Id);
        appDto.Servers.First().PortNumber.Should().Be(8080);
    }

    [Fact]
    public async Task GetApplicationsAsync_maps_and_filters_application_labels()
    {
        using var context = GetDbContext();
        var app = new AppEntity { Id = Guid.NewGuid(), AppCode = "LAB", AppName = "Labelled" };
        var label = new Label { Id = Guid.NewGuid(), Key = "tier", Value = "critical" };
        context.Applications.Add(app);
        context.Labels.Add(label);
        context.ApplicationLabels.Add(new ApplicationLabel
        {
            ApplicationId = app.Id,
            LabelId = label.Id
        });
        await context.SaveChangesAsync();

        var repository = new ApplicationRepository(context);

        var match = await repository.GetApplicationsAsync("tier", "critical");
        var noMatch = await repository.GetApplicationsAsync("tier", "other");

        match.Should().ContainSingle().Which.Labels.Should().ContainEquivalentOf(new LabelDto
        {
            Key = "tier", Value = "critical"
        });
        noMatch.Should().BeEmpty();
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
            OwnerTeam = "Team A"
        };

        // Act
        var result = await repository.CreateAsync(application, Array.Empty<LabelDto>(), null);

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
            OwnerTeam = "Original Team"
        };
        context.Applications.Add(app);
        await context.SaveChangesAsync();

        var repository = new ApplicationRepository(context);
        app.AppName = "Updated Name";
        app.OwnerTeam = "Updated Team";

        // Act
        await repository.UpdateAsync(app, Array.Empty<LabelDto>(), null);

        // Assert
        var updatedApp = await context.Applications.FindAsync(app.Id);
        updatedApp.Should().NotBeNull();
        updatedApp!.AppName.Should().Be("Updated Name");
        updatedApp!.OwnerTeam.Should().Be("Updated Team");
    }

    [Fact]
    public async Task UpdateAsync_updates_the_explicit_port_mapping()
    {
        // Arrange
        using var context = GetDbContext();
        var appId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var initialApp = new AppEntity
        {
            Id = appId,
            AppCode = "META",
            AppName = "Original Name",
            OwnerTeam = "Original Team"
        };
        var initialPortMapping = new PortMapping
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            ServerId = serverId,
            PortNumber = 80,
            Protocol = "TCP"
        };
        context.Applications.Add(initialApp);
        context.PortMappings.Add(initialPortMapping);
        await context.SaveChangesAsync();

        var repository = new ApplicationRepository(context);
        var newServerId = Guid.NewGuid();
        initialApp.AppName = "Updated Name";
        initialApp.OwnerTeam = "Updated Team";
        initialPortMapping.ServerId = newServerId;
        initialPortMapping.PortNumber = 443;

        // Act
        await repository.UpdateAsync(initialApp, Array.Empty<LabelDto>(), initialPortMapping);

        // Assert
        var updatedApp = await context.Applications.FindAsync(appId);
        updatedApp!.AppName.Should().Be("Updated Name");
        updatedApp.OwnerTeam.Should().Be("Updated Team");

        var updatedPortMapping = await context.PortMappings.FirstOrDefaultAsync(pm => pm.AppId == appId);
        updatedPortMapping.Should().NotBeNull();
        updatedPortMapping!.ServerId.Should().Be(newServerId);
        updatedPortMapping.PortNumber.Should().Be(443);
    }

    [Fact]
    public async Task CreateAsync_persists_application_and_optional_deployment_atomically()
    {
        // Arrange
        using var context = GetDbContext();
        var appId = Guid.NewGuid();
        var initialApp = new AppEntity
        {
            Id = appId,
            AppCode = "NOMAP",
            AppName = "App Without Port Mapping"
        };
        var repository = new ApplicationRepository(context);
        var targetServerId = Guid.NewGuid();
        var deployment = new PortMapping
        {
            Id = Guid.NewGuid(), AppId = appId, ServerId = targetServerId,
            PortNumber = 8080, Protocol = "TCP"
        };

        // Act
        await repository.CreateAsync(initialApp, Array.Empty<LabelDto>(), deployment);

        // Assert
        var portMapping = await context.PortMappings.FirstOrDefaultAsync(pm => pm.AppId == appId);
        portMapping.Should().NotBeNull();
        portMapping!.ServerId.Should().Be(targetServerId);
        portMapping.PortNumber.Should().Be(8080);
    }

    [Fact]
    public async Task GetByIdsAsync_ShouldReturnOnlyRequestedApplications()
    {
        // Arrange
        using var context = GetDbContext();
        var a1 = new AppEntity { Id = Guid.NewGuid(), AppCode = "A1", AppName = "App 1" };
        var a2 = new AppEntity { Id = Guid.NewGuid(), AppCode = "A2", AppName = "App 2" };
        var a3 = new AppEntity { Id = Guid.NewGuid(), AppCode = "A3", AppName = "App 3" };
        context.Applications.AddRange(a1, a2, a3);
        await context.SaveChangesAsync();

        var repository = new ApplicationRepository(context);

        // Act
        var result = await repository.GetByIdsAsync(new[] { a1.Id, a2.Id, a1.Id });

        // Assert
        result.Should().HaveCount(2);
        result.Select(r => r.Id).Should().Contain(new[] { a1.Id, a2.Id });
        result.Select(r => r.Id).Should().NotContain(a3.Id);
    }
}
