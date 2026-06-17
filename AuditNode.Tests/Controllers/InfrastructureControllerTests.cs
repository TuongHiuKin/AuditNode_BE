using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class InfrastructureControllerTests
{
    private readonly Mock<IInfrastructureService> _mockService;
    private readonly InfrastructureController _controller;

    public InfrastructureControllerTests()
    {
        _mockService = new Mock<IInfrastructureService>();
        _controller = new InfrastructureController(_mockService.Object);
    }

    [Fact]
    public async Task GetDependenciesCount_ShouldReturnOk_WithCount()
    {
        // Arrange
        var appId = Guid.NewGuid();
        _mockService.Setup(s => s.GetDependenciesCountAsync(appId)).ReturnsAsync(5);

        // Act
        var result = await _controller.GetDependenciesCount(appId);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Value.Should().Be(5);
    }

    [Fact]
    public async Task MigrateApp_ShouldReturnOk_WhenSuccessful()
    {
        // Arrange
        var dto = new MigrateAppDto { PortMappingId = Guid.NewGuid(), TargetServerId = Guid.NewGuid() };
        _mockService.Setup(s => s.MigrateAppAsync(dto)).ReturnsAsync(true);

        // Act
        var result = await _controller.MigrateApp(dto);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task MigrateApp_ShouldReturnNotFound_WhenFailed()
    {
        // Arrange
        var dto = new MigrateAppDto { PortMappingId = Guid.NewGuid(), TargetServerId = Guid.NewGuid() };
        _mockService.Setup(s => s.MigrateAppAsync(dto)).ReturnsAsync(false);

        // Act
        var result = await _controller.MigrateApp(dto);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task PurgeApp_ShouldReturnOk_WhenSuccessful()
    {
        // Arrange
        var appId = Guid.NewGuid();
        _mockService.Setup(s => s.PurgeAppAsync(appId)).ReturnsAsync(true);

        // Act
        var result = await _controller.PurgeApp(appId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetDeployedAppsByServer_ShouldReturnOk_WithData()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var mockApps = new List<DeployedAppDto> { new DeployedAppDto { AppName = "App1" } };
        _mockService.Setup(s => s.GetDeployedAppsByServerAsync(serverId)).ReturnsAsync(mockApps);

        // Act
        var result = await _controller.GetDeployedAppsByServer(serverId);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Value.Should().BeEquivalentTo(mockApps);
    }
}
