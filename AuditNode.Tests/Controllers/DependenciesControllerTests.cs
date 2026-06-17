using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class DependenciesControllerTests
{
    private readonly Mock<IDependencyService> _mockService;
    private readonly DependenciesController _controller;

    public DependenciesControllerTests()
    {
        _mockService = new Mock<IDependencyService>();
        _controller = new DependenciesController(_mockService.Object);
    }

    [Fact]
    public async Task SyncDependencies_ShouldReturnOk_WhenSuccessful()
    {
        // Arrange
        var dto = new SyncDependenciesDto { Dependencies = new List<DependencyItemDto>() };
        _mockService.Setup(s => s.SyncDependenciesAsync(dto)).Returns(Task.CompletedTask);

        // Act
        var result = await _controller.SyncDependencies(dto);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SyncDependencies_ShouldReturnBadRequest_WhenDtoIsNull()
    {
        // Act
        var result = await _controller.SyncDependencies(null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SyncDependencies_ShouldReturnStatusCode500_OnException()
    {
        // Arrange
        var dto = new SyncDependenciesDto { Dependencies = new List<DependencyItemDto>() };
        _mockService.Setup(s => s.SyncDependenciesAsync(dto)).ThrowsAsync(new Exception("Fail"));

        // Act
        var result = await _controller.SyncDependencies(dto);

        // Assert
        var statusResult = result.As<ObjectResult>();
        statusResult.StatusCode.Should().Be(500);
    }
}
