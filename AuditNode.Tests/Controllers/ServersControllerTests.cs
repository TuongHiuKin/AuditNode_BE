using AuditNode.Application.Interfaces;
using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class ServersControllerTests
{
    private readonly Mock<IServerService> _mockService;
    private readonly ServersController _controller;

    public ServersControllerTests()
    {
        _mockService = new Mock<IServerService>();
        _controller = new ServersController(_mockService.Object);
    }

    [Fact]
    public async Task ExportServers_ShouldReturnOk_WithSelectedServers()
    {
        // Arrange
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var mockServers = new List<ServerResponseDto>
        {
            new ServerResponseDto { Id = ids[0], Hostname = "S1" },
            new ServerResponseDto { Id = ids[1], Hostname = "S2" }
        };

        _mockService.Setup(s => s.ExportServersAsync(ids))
            .ReturnsAsync(mockServers);

        // Act
        var result = await _controller.ExportServers(ids);

        // Assert
        var okResult = result.Result.As<OkObjectResult>();
        okResult.Should().NotBeNull();
        okResult.Value.Should().BeEquivalentTo(mockServers);
    }

    [Fact]
    public async Task ExportServers_ShouldReturnBadRequest_WhenNoIdsProvided()
    {
        // Act
        var result = await _controller.ExportServers(null!);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateServer_ShouldReturnNoContent_WhenUpdateIsSuccessful()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var updateDto = new UpdateServerDto { Hostname = "NewHost" };
        _mockService.Setup(s => s.UpdateServerAsync(serverId, updateDto)).ReturnsAsync(true);

        // Act
        var result = await _controller.UpdateServer(serverId, updateDto);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task UpdateServer_ShouldReturnNotFound_WhenServerDoesNotExist()
    {
        // Arrange
        var serverId = Guid.NewGuid();
        var updateDto = new UpdateServerDto { Hostname = "NewHost" };
        _mockService.Setup(s => s.UpdateServerAsync(serverId, updateDto)).ReturnsAsync(false);

        // Act
        var result = await _controller.UpdateServer(serverId, updateDto);

        // Assert
        var notFoundResult = result.As<NotFoundObjectResult>();
        notFoundResult.Should().NotBeNull();
        notFoundResult.Value.Should().BeEquivalentTo(new { error = $"Server with ID {serverId} not found." });
    }

    [Fact]
    public async Task UpdateServer_ShouldReturnBadRequest_WhenDtoIsNull()
    {
        // Arrange
        var serverId = Guid.NewGuid();

        // Act
        var result = await _controller.UpdateServer(serverId, null!);

        // Assert
        var badRequestResult = result.As<BadRequestObjectResult>();
        badRequestResult.Should().NotBeNull();
        badRequestResult.Value.Should().BeEquivalentTo(new { error = "Update data is missing." });
    }
}
