using AuditNode.Application.Interfaces;
using AuditNode.Application.Services;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public class WorkspaceServiceTests
{
    private readonly Mock<IWorkspaceRepository> _repositoryMock;
    private readonly WorkspaceService _service;

    public WorkspaceServiceTests()
    {
        _repositoryMock = new Mock<IWorkspaceRepository>();
        _service = new WorkspaceService(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetUserWorkspacesAsync_ShouldReturnAllWorkspaces()
    {
        // Arrange
        var userId = "test-user";
        var workspaces = new List<Workspace>
        {
            new Workspace { Id = Guid.NewGuid(), Name = "Workspace 1", Description = "Desc 1" },
            new Workspace { Id = Guid.NewGuid(), Name = "Workspace 2", Description = "Desc 2" }
        };

        _repositoryMock.Setup(r => r.GetAllAsync())
            .ReturnsAsync(workspaces);

        // Act
        var result = await _service.GetUserWorkspacesAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain(w => w.Name == "Workspace 1");
        result.Should().Contain(w => w.Name == "Workspace 2");
        _repositoryMock.Verify(r => r.GetAllAsync(), Times.Once);
    }
}
