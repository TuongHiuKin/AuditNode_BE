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

        var dto = new SyncDependenciesDto
        {
            Dependencies = new List<DependencyItemDto>
            {
                new DependencyItemDto { SourceAppId = sourceId, DestAppId = destId }
            }
        };

        // Act
        await service.SyncDependenciesAsync(dto);

        // Assert
        var result = await dbContext.AppDependencies.ToListAsync();
        result.Should().HaveCount(1);
        result[0].SourceAppId.Should().Be(sourceId);
        result[0].DestAppId.Should().Be(destId);
    }

    [Fact]
    public async Task SyncDependenciesAsync_ShouldDeleteRemovedDependencies()
    {
        // Arrange
        var dbContext = GetDbContext();
        var sourceId = Guid.NewGuid();
        var destId = Guid.NewGuid();
        
        var existing = new AppDependency
        {
            Id = Guid.NewGuid(),
            SourceAppId = sourceId,
            DestAppId = destId,
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

        // Existing: A -> B
        dbContext.AppDependencies.Add(new AppDependency
        {
            Id = Guid.NewGuid(),
            SourceAppId = appA,
            DestAppId = appB,
            ConnectionType = "Automatic"
        });
        await dbContext.SaveChangesAsync();

        var service = new DependencyService(dbContext);
        // Sync to: B -> C (Delete A -> B, Insert B -> C)
        var dto = new SyncDependenciesDto
        {
            Dependencies = new List<DependencyItemDto>
            {
                new DependencyItemDto { SourceAppId = appB, DestAppId = appC }
            }
        };

        // Act
        await service.SyncDependenciesAsync(dto);

        // Assert
        var result = await dbContext.AppDependencies.ToListAsync();
        result.Should().HaveCount(1);
        result.Should().Contain(d => d.SourceAppId == appB && d.DestAppId == appC);
        result.Should().NotContain(d => d.SourceAppId == appA && d.DestAppId == appB);
    }
}
