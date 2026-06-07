using AuditNode.Application.DTOs;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Xunit;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditNode.Tests.Services;

public class DependencyServiceTests
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
    public async Task SyncDependenciesAsync_ShouldInsertNewDependencies()
    {
        // Arrange
        var dbContext = GetDbContext();
        var service = new DependencyService(dbContext);
        var sourceId = Guid.NewGuid();
        var destId = Guid.NewGuid();
        var portId = Guid.NewGuid();

        var dto = new SyncDependenciesDto
        {
            Dependencies = new List<DependencyItemDto>
            {
                new DependencyItemDto { SourceAppId = sourceId, DestAppId = destId, DestPortId = portId }
            }
        };

        // Act
        await service.SyncDependenciesAsync(dto);

        // Assert
        var result = await dbContext.AppDependencies.ToListAsync();
        result.Should().HaveCount(1);
        result[0].SourceAppId.Should().Be(sourceId);
        result[0].DestAppId.Should().Be(destId);
        result[0].DestPortId.Should().Be(portId);
    }

    [Fact]
    public async Task SyncDependenciesAsync_ShouldDeleteRemovedDependencies()
    {
        // Arrange
        var dbContext = GetDbContext();
        var sourceId = Guid.NewGuid();
        var destId = Guid.NewGuid();
        var portId = Guid.NewGuid();
        
        var existing = new AppDependency
        {
            Id = Guid.NewGuid(),
            SourceAppId = sourceId,
            DestAppId = destId,
            DestPortId = portId,
            ConnectionType = "Automatic"
        };
        dbContext.AppDependencies.Add(existing);
        await dbContext.SaveChangesAsync();

        var service = new DependencyService(dbContext);
        var dto = new SyncDependenciesDto { Dependencies = new List<DependencyItemDto>() }; // Empty list means delete all

        // Act
        await service.SyncDependenciesAsync(dto);

        // Assert
        var result = await dbContext.AppDependencies.ToListAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncDependenciesAsync_ShouldHandleBothInsertsAndDeletes()
    {
        // Arrange
        var dbContext = GetDbContext();
        var appA = Guid.NewGuid();
        var appB = Guid.NewGuid();
        var appC = Guid.NewGuid();
        var portB = Guid.NewGuid();
        var portC = Guid.NewGuid();

        // Existing: A -> B (portB)
        dbContext.AppDependencies.Add(new AppDependency
        {
            Id = Guid.NewGuid(),
            SourceAppId = appA,
            DestAppId = appB,
            DestPortId = portB,
            ConnectionType = "Automatic"
        });
        await dbContext.SaveChangesAsync();

        var service = new DependencyService(dbContext);
        // Sync to: B -> C (portC) (Delete A -> B, Insert B -> C)
        var dto = new SyncDependenciesDto
        {
            Dependencies = new List<DependencyItemDto>
            {
                new DependencyItemDto { SourceAppId = appB, DestAppId = appC, DestPortId = portC }
            }
        };

        // Act
        await service.SyncDependenciesAsync(dto);

        // Assert
        var result = await dbContext.AppDependencies.ToListAsync();
        result.Should().HaveCount(1);
        result.Should().Contain(d => d.SourceAppId == appB && d.DestAppId == appC && d.DestPortId == portC);
        result.Should().NotContain(d => d.SourceAppId == appA && d.DestAppId == appB);
    }

    [Fact]
    public async Task SyncDependenciesAsync_ShouldUpdatePortIfChanged()
    {
        // Arrange
        var dbContext = GetDbContext();
        var appA = Guid.NewGuid();
        var appB = Guid.NewGuid();
        var port1 = Guid.NewGuid();
        var port2 = Guid.NewGuid();

        // Existing: A -> B (port1)
        dbContext.AppDependencies.Add(new AppDependency
        {
            Id = Guid.NewGuid(),
            SourceAppId = appA,
            DestAppId = appB,
            DestPortId = port1,
            ConnectionType = "Automatic"
        });
        await dbContext.SaveChangesAsync();

        var service = new DependencyService(dbContext);
        // Sync to: A -> B (port2)
        var dto = new SyncDependenciesDto
        {
            Dependencies = new List<DependencyItemDto>
            {
                new DependencyItemDto { SourceAppId = appA, DestAppId = appB, DestPortId = port2 }
            }
        };

        // Act
        await service.SyncDependenciesAsync(dto);

        // Assert
        var result = await dbContext.AppDependencies.ToListAsync();
        result.Should().HaveCount(1);
        result[0].DestPortId.Should().Be(port2);
    }
}
