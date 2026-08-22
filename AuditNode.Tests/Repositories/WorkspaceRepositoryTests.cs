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
        mockTenantProvider.Setup(x => x.WorkspaceId).Returns(Guid.NewGuid());
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

    [Fact]
    public async Task GetAccessibleAsync_ShouldReturnOwnedAndMemberWorkspacesOnly()
    {
        using var context = GetDbContext();
        var owned = new Workspace
        {
            Id = Guid.NewGuid(), Name = "Owned", OwnerUserId = "user-a"
        };
        var member = new Workspace
        {
            Id = Guid.NewGuid(), Name = "Member", OwnerUserId = "user-b",
            Members =
            [
                new WorkspaceMember
                {
                    UserId = "user-a", Role = "viewer", InvitedByUserId = "user-b"
                }
            ]
        };
        var inaccessible = new Workspace
        {
            Id = Guid.NewGuid(), Name = "Other", OwnerUserId = "user-c"
        };
        context.Workspaces.AddRange(owned, member, inaccessible);
        await context.SaveChangesAsync();
        var repository = new WorkspaceRepository(context);

        var result = (await repository.GetAccessibleAsync("user-a")).ToList();

        result.Select(x => x.Id).Should().BeEquivalentTo([owned.Id, member.Id]);
        result.Select(x => x.Id).Should().NotContain(inaccessible.Id);
    }

    [Fact]
    public async Task UserHasAccessAsync_ShouldRejectNonMember()
    {
        using var context = GetDbContext();
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(), Name = "Private", OwnerUserId = "owner"
        };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();
        var repository = new WorkspaceRepository(context);

        var result = await repository.UserHasAccessAsync(workspace.Id, "outsider");

        result.Should().BeFalse();
    }
}
