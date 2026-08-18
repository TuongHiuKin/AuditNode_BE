using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public class WorkspaceServiceTests
{
    [Fact]
    public async Task GetUserWorkspacesAsync_ShouldMapOnlyRepositoryAccessibleWorkspaces()
    {
        var repository = new Mock<IWorkspaceRepository>();
        repository.Setup(x => x.GetAccessibleAsync("user-a")).ReturnsAsync(
        [
            new Workspace { Id = Guid.NewGuid(), Name = "Owned", Description = "Owner workspace" },
            new Workspace { Id = Guid.NewGuid(), Name = "Member", Description = "Member workspace" }
        ]);
        var service = new WorkspaceService(repository.Object);

        var result = (await service.GetUserWorkspacesAsync("user-a")).ToList();

        result.Should().HaveCount(2);
        result.Select(x => x.Name).Should().BeEquivalentTo(["Owned", "Member"]);
    }

    [Fact]
    public async Task UserHasAccessAsync_ShouldDelegateToRepository()
    {
        var workspaceId = Guid.NewGuid();
        var repository = new Mock<IWorkspaceRepository>();
        repository.Setup(x => x.UserHasAccessAsync(workspaceId, "user-a")).ReturnsAsync(true);
        var service = new WorkspaceService(repository.Object);

        var result = await service.UserHasAccessAsync(workspaceId, "user-a");

        result.Should().BeTrue();
    }
}
