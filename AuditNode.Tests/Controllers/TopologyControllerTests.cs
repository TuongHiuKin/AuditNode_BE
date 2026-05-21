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
        var mockTree = new List<TopologyTreeDto>
        {
            new TopologyTreeDto
            {
                Id = Guid.NewGuid(),
                Name = "DC1",
                Location = "US",
                Servers = new List<ServerNodeDto>
                {
                    new ServerNodeDto
                    {
                        Id = Guid.NewGuid(),
                        Hostname = "srv1",
                        IpAddress = "10.0.0.1",
                        Applications = new List<ApplicationNodeDto>
                        {
                            new ApplicationNodeDto { Id = Guid.NewGuid(), Name = "App1", Port = 80, Protocol = "HTTP", RiskLevel = "LOW" }
                        }
                    }
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
            .ReturnsAsync(new List<TopologyTreeDto>());

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
            .ReturnsAsync(new List<TopologyTreeDto>());

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
            .ReturnsAsync(new List<TopologyTreeDto>());

        // Act
        var result = await _controller.GetTree(null, 0, null);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.As<IEnumerable<TopologyTreeDto>>().Should().BeEmpty();
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
