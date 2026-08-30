using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Moq;
using Xunit;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Services;

public class InfrastructureServiceTests
{
    private static InfrastructureService Service(AuditDbContext context)
    {
        var policy = new Mock<IScopedResourcePolicy>();
        policy.Setup(x => x.CanReadAsync(It.IsAny<Guid>(), "test-user", It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        policy.Setup(x => x.GetReadableIdsAsync(It.IsAny<Guid>(), "test-user", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlySet<Guid>?)null);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns("test-user");
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(x => x.WorkspaceId).Returns(context.CurrentWorkspaceId);
        return new InfrastructureService(context, NullLogger<InfrastructureService>.Instance, policy.Object, user.Object, tenant.Object, Mock.Of<IGlobalCatalogRepository>(), TimeProvider.System);
    }
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
    public async Task GetDependenciesCountAsync_ShouldCountBothInboundAndOutbound()
    {
        // Arrange
        var dbContext = GetDbContext();
        var appId = Guid.NewGuid();
        var otherAppId = Guid.NewGuid();
        var portId = Guid.NewGuid();

        // Outbound: App -> Other
        dbContext.AppDependencies.Add(new AppDependency { Id = Guid.NewGuid(), SourceAppId = appId, DestAppId = otherAppId, DestPortId = Guid.NewGuid() });
        
        // Inbound via DestAppId: Other -> App
        dbContext.AppDependencies.Add(new AppDependency { Id = Guid.NewGuid(), SourceAppId = otherAppId, DestAppId = appId, DestPortId = Guid.NewGuid() });

        // Inbound via PortMapping: Other -> Port
        dbContext.PortMappings.Add(new PortMapping { Id = portId, AppId = appId, ServerId = Guid.NewGuid(), PortNumber = 80 });
        dbContext.AppDependencies.Add(new AppDependency { Id = Guid.NewGuid(), SourceAppId = otherAppId, DestAppId = otherAppId, DestPortId = portId });

        await dbContext.SaveChangesAsync();

        var service = Service(dbContext);

        // Act
        var count = await service.GetDependenciesCountAsync(appId);

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task MigrateAppAsync_ShouldUpdatePortMapping()
    {
        // Arrange
        var dbContext = GetDbContext();
        var portMappingId = Guid.NewGuid();
        var targetServerId = Guid.NewGuid();
        var originalServerId = Guid.NewGuid();

        dbContext.PortMappings.Add(new PortMapping 
        { 
            Id = portMappingId, 
            AppId = Guid.NewGuid(), 
            ServerId = originalServerId, 
            PortNumber = 80 
        });
        dbContext.Servers.Add(new Server
        {
            Id = targetServerId, DatacenterId = Guid.NewGuid(), Hostname = "target",
            IpAddress = "10.0.0.2", OsType = "Linux", Environment = "Prod", Status = "Active"
        });
        await dbContext.SaveChangesAsync();

        var service = Service(dbContext);
        var migrateDto = new MigrateAppDto 
        { 
            PortMappingId = portMappingId, 
            TargetServerId = targetServerId, 
            NewPortNumber = 8080 
        };

        // Act
        var result = await service.MigrateAppAsync(migrateDto);

        // Assert
        result.Should().Be(DeploymentOperationStatus.Success);
        var updated = await dbContext.PortMappings.FindAsync(portMappingId);
        updated!.ServerId.Should().Be(targetServerId);
        updated.PortNumber.Should().Be(8080);
    }

    [Fact]
    public async Task MigrateAppAsync_rejects_server_port_collision()
    {
        var dbContext = GetDbContext();
        var serverId = Guid.NewGuid();
        var mappingId = Guid.NewGuid();
        dbContext.Servers.Add(new Server
        {
            Id = serverId, DatacenterId = Guid.NewGuid(), Hostname = "target", IpAddress = "10.0.0.3"
        });
        dbContext.PortMappings.AddRange(
            new PortMapping { Id = mappingId, AppId = Guid.NewGuid(), ServerId = Guid.NewGuid(), PortNumber = 80 },
            new PortMapping { Id = Guid.NewGuid(), AppId = Guid.NewGuid(), ServerId = serverId, PortNumber = 443 });
        await dbContext.SaveChangesAsync();

        var service = Service(dbContext);
        var result = await service.MigrateAppAsync(new MigrateAppDto
        {
            PortMappingId = mappingId, TargetServerId = serverId, NewPortNumber = 443
        });

        result.Should().Be(DeploymentOperationStatus.PortCollision);
    }

    [Fact]
    public async Task GetDeployedAppsByServerAsync_exposes_real_port_mapping_id()
    {
        var dbContext = GetDbContext();
        var serverId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var mappingId = Guid.NewGuid();
        dbContext.Applications.Add(new AppEntity { Id = appId, AppCode = "APP", AppName = "App" });
        dbContext.PortMappings.Add(new PortMapping
        {
            Id = mappingId, AppId = appId, ServerId = serverId, PortNumber = 443
        });
        await dbContext.SaveChangesAsync();

        var result = await Service(dbContext)
            .GetDeployedAppsByServerAsync(serverId);

        result.Should().ContainSingle().Which.PortMappingId.Should().Be(mappingId);
    }

    [Fact]
    public async Task PurgeAppAsync_ShouldDeleteApplicationAndDependencies()
    {
        // Arrange
        var dbContext = GetDbContext();
        var appId = Guid.NewGuid();
        var portId = Guid.NewGuid();

        dbContext.Applications.Add(new AppEntity { Id = appId, AppCode = "TEST", AppName = "Test App" });
        dbContext.PortMappings.Add(new PortMapping { Id = portId, AppId = appId, ServerId = Guid.NewGuid(), PortNumber = 80 });
        
        // Outbound
        dbContext.AppDependencies.Add(new AppDependency { Id = Guid.NewGuid(), SourceAppId = appId, DestAppId = Guid.NewGuid(), DestPortId = Guid.NewGuid() });
        // Inbound
        dbContext.AppDependencies.Add(new AppDependency { Id = Guid.NewGuid(), SourceAppId = Guid.NewGuid(), DestAppId = appId, DestPortId = Guid.NewGuid() });
        // Inbound via port
        dbContext.AppDependencies.Add(new AppDependency { Id = Guid.NewGuid(), SourceAppId = Guid.NewGuid(), DestAppId = Guid.NewGuid(), DestPortId = portId });

        await dbContext.SaveChangesAsync();

        var service = Service(dbContext);

        // Act
        var result = await service.PurgeAppAsync(appId);

        // Assert
        result.Should().BeTrue();
        (await dbContext.Applications.FindAsync(appId)).Should().BeNull();
        (await dbContext.PortMappings.Where(pm => pm.AppId == appId).ToListAsync()).Should().BeEmpty();
        (await dbContext.AppDependencies.Where(ad => ad.SourceAppId == appId || ad.DestAppId == appId || ad.DestPortId == portId).ToListAsync()).Should().BeEmpty();
    }
}
