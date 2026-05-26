using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Tests.Repositories;

public class TopologyRepositoryTests
{
    private AuditDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuditDbContext(options);
    }

    [Fact]
    public async Task GetApplicationStatusAsync_ShouldReturnStatus_CorrectlyIdentifyingMappedApps()
    {
        // Arrange
        using var context = GetDbContext();
        var app1 = new AppEntity { Id = Guid.NewGuid(), AppName = "App 1", AppCode = "A1", OwnerId = "O1", Risk = "L", ServerId = Guid.NewGuid() };
        var app2 = new AppEntity { Id = Guid.NewGuid(), AppName = "App 2", AppCode = "A2", OwnerId = "O2", Risk = "L", ServerId = Guid.NewGuid() };
        var app3 = new AppEntity { Id = Guid.NewGuid(), AppName = "App 3", AppCode = "A3", OwnerId = "O3", Risk = "L", ServerId = Guid.NewGuid() };
        
        context.Applications.AddRange(app1, app2, app3);

        // App 1 is a source, App 2 is a destination. App 3 is not in any dependency.
        context.AppDependencies.Add(new AppDependency 
        { 
            Id = Guid.NewGuid(), 
            SourceAppId = app1.Id, 
            DestAppId = app2.Id,
            DestPortId = Guid.NewGuid(),
            ConnectionType = "TCP"
        });

        await context.SaveChangesAsync();

        var repository = new TopologyRepository(context);

        // Act
        var result = await repository.GetApplicationStatusAsync();

        // Assert
        result.Should().HaveCount(3);
        result.First(a => a.Id == app1.Id).IsMapped.Should().BeTrue();
        result.First(a => a.Id == app2.Id).IsMapped.Should().BeTrue();
        result.First(a => a.Id == app3.Id).IsMapped.Should().BeFalse();
    }
}
