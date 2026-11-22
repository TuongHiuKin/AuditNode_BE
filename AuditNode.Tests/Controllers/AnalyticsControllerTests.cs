using AuditNode.API.Controllers;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using AuditNode.Application.DTOs;

namespace AuditNode.Tests.Controllers;

public class AnalyticsControllerTests
{
    private readonly Mock<IAnalyticsRepository> _mockRepo;
    private readonly AnalyticsController _controller;

    public AnalyticsControllerTests()
    {
        _mockRepo = new Mock<IAnalyticsRepository>();
        _controller = new AnalyticsController(_mockRepo.Object);
    }

    [Fact]
    public async Task GetTopology_ShouldReturnOk_WithData()
    {
        // Arrange
        var mockData = new List<TopologyView> { new TopologyView { ServerHostname = "srv1" } };
        _mockRepo.Setup(r => r.GetTopologyCatalogAsync(CatalogView.Mine, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockData);

        // Act
        var result = await _controller.GetTopology(null, null);

        // Assert
        var okResult = result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.Should().BeEquivalentTo(mockData);
    }

    [Fact]
    public async Task GetTopology_ShouldReturnSafe500_OnException()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetTopologyCatalogAsync(It.IsAny<CatalogView>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test error"));

        // Act
        var result = await _controller.GetTopology(null, null);

        // Assert
        var failure = result.Should().BeOfType<ObjectResult>().Subject;
        failure.StatusCode.Should().Be(500);
        failure.Value.Should().BeOfType<ProblemDetails>();
        failure.Value!.ToString().Should().NotContain("Test error");
    }

    [Fact]
    public async Task GetDependencies_ShouldReturnOk_WithData()
    {
        // Arrange
        var mockData = new List<DependencyView> { new DependencyView { SourceAppName = "App1" } };
        _mockRepo.Setup(r => r.GetDependenciesCatalogAsync(CatalogView.Mine, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockData);

        // Act
        var result = await _controller.GetDependencies(null, null);

        // Assert
        var okResult = result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.Should().BeEquivalentTo(mockData);
    }

    [Fact]
    public async Task GetDependencies_ShouldReturnSafe500_OnException()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetDependenciesCatalogAsync(It.IsAny<CatalogView>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test error"));

        // Act
        var result = await _controller.GetDependencies(null, null);

        // Assert
        var failure = result.Should().BeOfType<ObjectResult>().Subject;
        failure.StatusCode.Should().Be(500);
        failure.Value.Should().BeOfType<ProblemDetails>();
        failure.Value!.ToString().Should().NotContain("Test error");
    }
}
