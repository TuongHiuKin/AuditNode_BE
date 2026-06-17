using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace AuditNode.Tests.Repositories;

public class WorkspaceRepositoryTests
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
    public async Task GetAllAsync_ShouldReturnAllWorkspaces()
    {
        // Arrange
        using var context = GetDbContext();
        var workspaces = new List<Workspace>
        {
            new Workspace { Id = Guid.NewGuid(), Name = "Workspace 1", Description = "Desc 1" },
            new Workspace { Id = Guid.NewGuid(), Name = "Workspace 2", Description = "Desc 2" }
        };
        context.Workspaces.AddRange(workspaces);
        await context.SaveChangesAsync();

        var repository = new WorkspaceRepository(context);

        // Act
        var result = await repository.GetAllAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain(w => w.Name == "Workspace 1");
        result.Should().Contain(w => w.Name == "Workspace 2");
    }
}
