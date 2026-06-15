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

        var service = new InfrastructureService(dbContext, NullLogger<InfrastructureService>.Instance);

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
        await dbContext.SaveChangesAsync();

        var service = new InfrastructureService(dbContext, NullLogger<InfrastructureService>.Instance);
        var migrateDto = new MigrateAppDto 
        { 
            PortMappingId = portMappingId, 
            TargetServerId = targetServerId, 
            NewPortNumber = 8080 
        };

        // Act
        var result = await service.MigrateAppAsync(migrateDto);

        // Assert
        result.Should().BeTrue();
        var updated = await dbContext.PortMappings.FindAsync(portMappingId);
        updated!.ServerId.Should().Be(targetServerId);
        updated.PortNumber.Should().Be(8080);
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

        var service = new InfrastructureService(dbContext, NullLogger<InfrastructureService>.Instance);

        // Act
        var result = await service.PurgeAppAsync(appId);

        // Assert
        result.Should().BeTrue();
        (await dbContext.Applications.FindAsync(appId)).Should().BeNull();
        (await dbContext.PortMappings.Where(pm => pm.AppId == appId).ToListAsync()).Should().BeEmpty();
        (await dbContext.AppDependencies.Where(ad => ad.SourceAppId == appId || ad.DestAppId == appId || ad.DestPortId == portId).ToListAsync()).Should().BeEmpty();
    }
}
