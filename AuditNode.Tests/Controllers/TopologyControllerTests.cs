using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class TopologyControllerTests
{
    private readonly Mock<ITopologyRepository> _mockRepo;
    private readonly TopologyController _controller;

    public TopologyControllerTests()
    {
        _mockRepo = new Mock<ITopologyRepository>();
        _controller = new TopologyController(_mockRepo.Object);
    }

    [Fact]
    public async Task GetTree_ShouldReturnOk_WithTreeData()
    {
        // Arrange
        var mockTree = new List<ServerTopologyDto>
        {
            new ServerTopologyDto
            {
                Id = Guid.NewGuid(),
                Hostname = "srv1",
                IpAddress = "10.0.0.1",
                Environment = "PROD",
                Datacenter = "DC1",
                Ports = new List<PortTopologyDto>
                {
                    new PortTopologyDto { AppName = "App1", PortNumber = 80, Protocol = "HTTP", AppCode = "APP1" }
                }
            }
        };

        _mockRepo.Setup(repo => repo.GetTopologyTreeAsync(null, 0, 100))
            .ReturnsAsync(mockTree);

        // Act
        var result = await _controller.GetTree(null, 0, null);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.Should().BeEquivalentTo(mockTree);
    }

    [Fact]
    public async Task GetTree_WithTakeParameter_ShouldRespectTakeParameter()
    {
        // Arrange
        int take = 50;
        _mockRepo.Setup(repo => repo.GetTopologyTreeAsync(null, 0, take))
            .ReturnsAsync(new List<ServerTopologyDto>());

        // Act
        await _controller.GetTree(null, 0, take);

        // Assert
        _mockRepo.Verify(repo => repo.GetTopologyTreeAsync(null, 0, take), Times.Once);
    }

    [Fact]
    public async Task GetTree_WithLargeTake_ShouldCapAt100()
    {
        // Arrange
        int largeTake = 500;
        _mockRepo.Setup(repo => repo.GetTopologyTreeAsync(null, 0, 100))
            .ReturnsAsync(new List<ServerTopologyDto>());

        // Act
        await _controller.GetTree(null, 0, largeTake);

        // Assert
        _mockRepo.Verify(repo => repo.GetTopologyTreeAsync(null, 0, 100), Times.Once);
    }

    [Fact]
    public async Task GetTree_WhenEmpty_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRepo.Setup(repo => repo.GetTopologyTreeAsync(null, 0, 100))
            .ReturnsAsync(new List<ServerTopologyDto>());

        // Act
        var result = await _controller.GetTree(null, 0, null);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.As<IEnumerable<ServerTopologyDto>>().Should().BeEmpty();
    }

    [Fact]
    public async Task GetDependencyMap_WhenEmpty_ShouldReturnEmptyMap()
    {
        // Arrange
        var emptyMap = new DependencyMapDto();
        _mockRepo.Setup(repo => repo.GetDependencyMapAsync())
            .ReturnsAsync(emptyMap);

        // Act
        var result = await _controller.GetDependencyMap();

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.Should().BeEquivalentTo(emptyMap);
    }
}
