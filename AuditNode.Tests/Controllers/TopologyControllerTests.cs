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
    private readonly Mock<ITopologyCommandService> _mockCommandService;
    private readonly TopologyController _controller;

    public TopologyControllerTests()
    {
        _mockRepo = new Mock<ITopologyRepository>();
        _mockCommandService = new Mock<ITopologyCommandService>();
        _controller = new TopologyController(_mockRepo.Object, _mockCommandService.Object);
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
                            new ApplicationNodeDto { Id = Guid.NewGuid(), Name = "App1", Port = 80, Protocol = "HTTP" }
                        }
                    }
                }
            }
        };

        _mockRepo.Setup(repo => repo.GetTopologyTreeAsync(null, 0, 100, null))
            .ReturnsAsync(mockTree);

        // Act
        var result = await _controller.GetTree(null, 0, null, null);

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
        _mockRepo.Setup(repo => repo.GetTopologyTreeAsync(null, 0, take, null))
            .ReturnsAsync(new List<TopologyTreeDto>());

        // Act
        await _controller.GetTree(null, 0, take, null);

        // Assert
        _mockRepo.Verify(repo => repo.GetTopologyTreeAsync(null, 0, take, null), Times.Once);
    }

    [Fact]
    public async Task GetTree_WithLargeTake_ShouldCapAt100()
    {
        // Arrange
        int largeTake = 500;
        _mockRepo.Setup(repo => repo.GetTopologyTreeAsync(null, 0, 100, null))
            .ReturnsAsync(new List<TopologyTreeDto>());

        // Act
        await _controller.GetTree(null, 0, largeTake, null);

        // Assert
        _mockRepo.Verify(repo => repo.GetTopologyTreeAsync(null, 0, 100, null), Times.Once);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    public async Task GetTree_rejects_invalid_pagination(int skip, int take)
    {
        var result = await _controller.GetTree(null, skip, take, null);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _mockRepo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetTree_WhenEmpty_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRepo.Setup(repo => repo.GetTopologyTreeAsync(null, 0, 100, null))
            .ReturnsAsync(new List<TopologyTreeDto>());

        // Act
        var result = await _controller.GetTree(null, 0, null, null);

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
        _mockRepo.Setup(repo => repo.GetDependencyMapAsync(It.IsAny<string>(), It.IsAny<Guid?>(), null))
            .ReturnsAsync(emptyMap);

        // Act
        var result = await _controller.GetDependencyMap(null, null, null);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.Should().BeEquivalentTo(emptyMap);
    }

    [Fact]
    public async Task SaveState_ShouldReturnOk_WhenStateIsValid()
    {
        // Arrange
        var state = new SaveTopologyStateDto
        {
            Version = 0,
            Nodes = new List<TopologyNodeDto>
            {
                new TopologyNodeDto { Id = Guid.NewGuid(), Label = "Node 1" }
            },
            Edges = [],
            Dependencies = []
        };

        _mockRepo.Setup(repo => repo.SaveTopologyStateAsync(state))
            .ReturnsAsync(TopologyStateStatus.Success);

        // Act
        var result = await _controller.SaveState(state);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mockRepo.Verify(repo => repo.SaveTopologyStateAsync(state), Times.Once);
    }

    [Fact]
    public async Task SaveState_ShouldReturnBadRequest_WhenStateIsNull()
    {
        // Act
        var result = await _controller.SaveState(null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SaveState_ShouldRejectMissingVersionOrDependencies()
    {
        var result = await _controller.SaveState(new SaveTopologyStateDto
        {
            Nodes = [],
            Edges = [],
            Dependencies = null
        });

        result.Should().BeOfType<BadRequestObjectResult>();
        _mockRepo.Verify(repo => repo.SaveTopologyStateAsync(It.IsAny<SaveTopologyStateDto>()), Times.Never);
    }

    [Fact]
    public async Task GetTree_WithLabelsParameter_ShouldPassLabelsToRepository()
    {
        // Arrange
        var labels = new List<string> { "env:prod", "tier:db" };
        var mockTree = new List<TopologyTreeDto>();

        _mockRepo.Setup(repo => repo.GetTopologyTreeAsync(null, 0, 100, labels))
            .ReturnsAsync(mockTree);

        // Act
        var result = await _controller.GetTree(null, 0, null, labels);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        _mockRepo.Verify(repo => repo.GetTopologyTreeAsync(null, 0, 100, labels), Times.Once);
    }

    [Fact]
    public async Task GetDependencyMap_WithLabelsParameter_ShouldPassLabelsToRepository()
    {
        // Arrange
        var labels = new List<string> { "env:prod" };
        var mockMap = new DependencyMapDto();

        _mockRepo.Setup(repo => repo.GetDependencyMapAsync(null, null, labels))
            .ReturnsAsync(mockMap);

        // Act
        var result = await _controller.GetDependencyMap(null, null, labels);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        _mockRepo.Verify(repo => repo.GetDependencyMapAsync(null, null, labels), Times.Once);
    }

    [Fact]
    public async Task SaveState_ShouldReturnConflict_WhenVersionIsStale()
    {
        var state = new SaveTopologyStateDto { Version = 3, Nodes = [], Edges = [], Dependencies = [] };
        _mockRepo.Setup(repo => repo.SaveTopologyStateAsync(state)).ReturnsAsync(TopologyStateStatus.Conflict);

        var result = await _controller.SaveState(state);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Theory]
    [InlineData(TopologyCommandStatus.Conflict, typeof(ConflictObjectResult))]
    [InlineData(TopologyCommandStatus.Forbidden, typeof(ObjectResult))]
    [InlineData(TopologyCommandStatus.InvalidRequest, typeof(BadRequestObjectResult))]
    public async Task ExecuteCommands_ShouldMapFailureStatus(TopologyCommandStatus status, Type resultType)
    {
        var batch = new TopologyCommandBatchDto(1, [new TopologyCommandDto { Type = "moveNode" }]);
        _mockCommandService.Setup(service => service.ExecuteAsync(batch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TopologyCommandResult(status, 2, "failure"));

        var result = await _controller.ExecuteCommands(batch, CancellationToken.None);

        result.Should().BeOfType(resultType);
        if (status == TopologyCommandStatus.Forbidden)
            result.As<ObjectResult>().StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task ExecuteCommands_ShouldReturnNewVersion()
    {
        var batch = new TopologyCommandBatchDto(1, [new TopologyCommandDto { Type = "moveNode" }]);
        _mockCommandService.Setup(service => service.ExecuteAsync(batch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TopologyCommandResult(TopologyCommandStatus.Success, 2));

        var result = await _controller.ExecuteCommands(batch, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }
}
