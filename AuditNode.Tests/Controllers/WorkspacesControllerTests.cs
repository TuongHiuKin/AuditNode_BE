using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class WorkspacesControllerTests
{
    private readonly Mock<IWorkspaceService> _mockService;
    private readonly WorkspacesController _controller;

    public WorkspacesControllerTests()
    {
        _mockService = new Mock<IWorkspaceService>();
        _controller = new WorkspacesController(_mockService.Object);
        
        // Mock User
        var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id")
        }, "mock"));

        _controller.ControllerContext = new ControllerContext()
        {
            HttpContext = new DefaultHttpContext() { User = user }
        };
    }

    [Fact]
    public async Task GetWorkspaces_ShouldReturnOk_WithWorkspaces()
    {
        // Arrange
        var mockWorkspaces = new List<WorkspaceDto>
        {
            new WorkspaceDto { Id = Guid.NewGuid(), Name = "Workspace 1" },
            new WorkspaceDto { Id = Guid.NewGuid(), Name = "Workspace 2" }
        };

        _mockService.Setup(s => s.GetUserWorkspacesAsync("test-user-id"))
            .ReturnsAsync(mockWorkspaces);

        // Act
        var result = await _controller.GetWorkspaces();

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.Should().BeEquivalentTo(mockWorkspaces);
    }

    [Fact]
    public async Task GetWorkspaces_ShouldUseAnonymous_WhenNoUserIdentifier()
    {
        // Arrange
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        
        _mockService.Setup(s => s.GetUserWorkspacesAsync("anonymous"))
            .ReturnsAsync(new List<WorkspaceDto>());

        // Act
        await _controller.GetWorkspaces();

        // Assert
        _mockService.Verify(s => s.GetUserWorkspacesAsync("anonymous"), Times.Once);
    }
}
