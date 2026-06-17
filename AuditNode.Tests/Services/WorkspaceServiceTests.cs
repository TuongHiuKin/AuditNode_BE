using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Services;
using AuditNode.Infrastructure.Data;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public class WorkspaceServiceTests
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
    public async Task GetUserWorkspacesAsync_ShouldReturnAllWorkspaces()
    {
        // Arrange
        using var context = GetDbContext();
        context.Workspaces.AddRange(new List<Workspace>
        {
            new Workspace { Id = Guid.NewGuid(), Name = "Workspace 1", Description = "Desc 1" },
            new Workspace { Id = Guid.NewGuid(), Name = "Workspace 2", Description = "Desc 2" }
        });
        await context.SaveChangesAsync();

        var service = new WorkspaceService(context);
        var userId = "test-user";

        // Act
        var result = await service.GetUserWorkspacesAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain(w => w.Name == "Workspace 1");
        result.Should().Contain(w => w.Name == "Workspace 2");
    }
}
