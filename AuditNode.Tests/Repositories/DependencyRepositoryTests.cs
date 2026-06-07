using AuditNode.Application.DTOs;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuditNode.Tests.Repositories;

public class DependencyRepositoryTests
{
    private AuditDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options);
    }

    [Fact]
    public async Task SyncDependenciesAsync_ShouldInsertNewAndRemoveOld()
    {
        // Arrange
        using var context = GetDbContext();
        var app1Id = Guid.NewGuid();
        var app2Id = Guid.NewGuid();
        var app3Id = Guid.NewGuid();

        // Existing dependency: 1 -> 2
        var existing = new AppDependency 
        { 
            Id = Guid.NewGuid(), 
            SourceAppId = app1Id, 
            DestAppId = app2Id, 
            DestPortId = Guid.Empty 
        };
        context.AppDependencies.Add(existing);
        await context.SaveChangesAsync();

        var repository = new DependencyRepository(context);

        // Sync request: 1 -> 3 (should remove 1 -> 2, insert 1 -> 3)
        var syncDto = new SyncDependenciesDto
        {
            Dependencies = new List<DependencyItemDto>
            {
                new DependencyItemDto { SourceAppId = app1Id, DestAppId = app3Id, ConnectionType = "HTTPS" }
            }
        };

        // Act
        await repository.SyncDependenciesAsync(syncDto);

        // Assert
        var currentDependencies = await context.AppDependencies.ToListAsync();
        currentDependencies.Should().HaveCount(1);
        currentDependencies.Should().Contain(d => d.SourceAppId == app1Id && d.DestAppId == app3Id);
        currentDependencies.Should().NotContain(d => d.SourceAppId == app1Id && d.DestAppId == app2Id);
    }

    [Fact]
    public async Task SyncDependenciesAsync_ShouldKeepExisting()
    {
        // Arrange
        using var context = GetDbContext();
        var app1Id = Guid.NewGuid();
        var app2Id = Guid.NewGuid();

        var existing = new AppDependency 
        { 
            Id = Guid.NewGuid(), 
            SourceAppId = app1Id, 
            DestAppId = app2Id, 
            DestPortId = Guid.Empty 
        };
        context.AppDependencies.Add(existing);
        await context.SaveChangesAsync();

        var repository = new DependencyRepository(context);

        var syncDto = new SyncDependenciesDto
        {
            Dependencies = new List<DependencyItemDto>
            {
                new DependencyItemDto { SourceAppId = app1Id, DestAppId = app2Id }
            }
        };

        // Act
        await repository.SyncDependenciesAsync(syncDto);

        // Assert
        var currentDependencies = await context.AppDependencies.ToListAsync();
        currentDependencies.Should().HaveCount(1);
        currentDependencies.First().Id.Should().Be(existing.Id); // Should be the same record
    }
}
